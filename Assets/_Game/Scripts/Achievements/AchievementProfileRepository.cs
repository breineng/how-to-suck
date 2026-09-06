using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
namespace HowToSuck
{
    public sealed class AchievementFutureSchemaException:IOException { public AchievementFutureSchemaException():base("A newer achievement profile exists; preserve it and update the game."){} }
    [Serializable] public sealed class AchievementProfile
    {
        public int Schema=1;
        public string ProfileId,SteamId="";
        public string[] UnlockedLocal=Array.Empty<string>(),PendingSteam=Array.Empty<string>();
        public AchievementProfile Copy()=>new AchievementProfile{Schema=Schema,ProfileId=ProfileId,SteamId=SteamId,
            UnlockedLocal=(string[])UnlockedLocal.Clone(),PendingSteam=(string[])PendingSteam.Clone()};
    }
    // One atomic envelope avoids torn index/profile writes when binding the unbound profile or switching account.
    // Each contained profile retains its own immutable ProfileId and SteamId; there is no campaign ID or balance.
    [Serializable] public sealed class AchievementProfileStore
    {
        public int Schema=1;
        public string ActiveProfileId;
        public AchievementProfile[] Profiles=Array.Empty<AchievementProfile>();
        public AchievementProfileStore Copy()
        {var p=new AchievementProfile[Profiles.Length];for(int i=0;i<p.Length;i++)p[i]=Profiles[i].Copy();return new AchievementProfileStore{Schema=Schema,ActiveProfileId=ActiveProfileId,Profiles=p};}
        public AchievementProfile Active
        {get{foreach(var p in Profiles)if(p.ProfileId==ActiveProfileId)return p;throw new InvalidDataException("Missing active achievement profile.");}}
        public static AchievementProfileStore Fresh()
        {var p=new AchievementProfile{ProfileId=Guid.NewGuid().ToString("N")};return new AchievementProfileStore{ActiveProfileId=p.ProfileId,Profiles=new[]{p}};}
        public void Validate()
        {
            if(Schema>1)throw new AchievementFutureSchemaException();
            if(Profiles!=null)foreach(var profile in Profiles)if(profile!=null&&profile.Schema>1)throw new AchievementFutureSchemaException();
            if(Schema!=1||Profiles==null||Profiles.Length<1||Profiles.Length>256)throw new InvalidDataException("Unsupported achievement store.");
            var ids=new HashSet<string>(StringComparer.Ordinal);var accounts=new HashSet<string>(StringComparer.Ordinal);int unbound=0;
            foreach(var p in Profiles)
            {
                if(p==null||p.Schema!=1||!AchievementDefinitions.ValidRun(p.ProfileId)||!ids.Add(p.ProfileId)||p.SteamId==null)
                    throw new InvalidDataException("Invalid achievement profile identity.");
                if(p.SteamId.Length==0){if(++unbound>1)throw new InvalidDataException("Multiple unbound profiles.");}
                else if(!ulong.TryParse(p.SteamId,out var id)||id==0||id.ToString(System.Globalization.CultureInfo.InvariantCulture)!=p.SteamId||!accounts.Add(p.SteamId))
                    throw new InvalidDataException("Invalid/duplicate achievement SteamID.");
                var unlocked=KnownUnique(p.UnlockedLocal);var pending=KnownUnique(p.PendingSteam);
                if(!pending.IsSubsetOf(unlocked))throw new InvalidDataException("Pending achievements must already be unlocked locally.");
            }
            if(!ids.Contains(ActiveProfileId))throw new InvalidDataException("Invalid active profile.");
        }
        private static HashSet<string> KnownUnique(string[] values)
        {
            if(values==null||values.Length>8)throw new InvalidDataException("Invalid achievement IDs.");
            var set=new HashSet<string>(StringComparer.Ordinal);
            foreach(string value in values)if(AchievementDefinitions.IndexOfId(value)<0||!set.Add(value))throw new InvalidDataException("Unknown/duplicate achievement ID.");
            return set;
        }
    }
    public interface IAchievementProfileCodec { string Encode(AchievementProfileStore store); AchievementProfileStore Decode(string json); }
    public interface IAchievementProfileRepository : IDisposable
    {
        AchievementProfileStore Open();
        void Save(AchievementProfileStore store);
    }
    public sealed class AchievementProfileRepository : IAchievementProfileRepository
    {
        private readonly string directory,path,backup;
        private readonly IAchievementProfileCodec codec;
        private FileStream ownership;
        private bool opened,blocked,disposed;
        public string RecoveryNotice{get;private set;}
        public AchievementProfileRepository(string absoluteDirectory,IAchievementProfileCodec codec)
        {
            if(string.IsNullOrWhiteSpace(absoluteDirectory)||!Path.IsPathFullyQualified(absoluteDirectory))throw new ArgumentException("Absolute own achievement directory required.");
            directory=Path.GetFullPath(absoluteDirectory);path=Path.Combine(directory,"achievements-v1.json");backup=path+".bak";
            this.codec=codec??throw new ArgumentNullException(nameof(codec));
        }
        public AchievementProfileStore Open()
        {
            if(disposed||opened)throw new InvalidOperationException("Open one achievement repository once.");
            Directory.CreateDirectory(directory);
            ownership=new FileStream(Path.Combine(directory,"achievements-v1.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
            opened=true;
            if(!File.Exists(path)&&!File.Exists(backup)){var fresh=AchievementProfileStore.Fresh();Save(fresh);return fresh;}
            Exception first=null;
            try{return Read(path);}catch(AchievementFutureSchemaException){blocked=true;throw;}catch(Exception e) when(!(e is AchievementFutureSchemaException)&&(e is IOException||e is InvalidDataException||e is ArgumentException||e is FormatException)){first=e;}
            try
            {
                var restored=Read(backup);
                if(File.Exists(path))File.Copy(path,path+".corrupt-"+DateTime.UtcNow.ToString("yyyyMMddHHmmssffff"),false);
                RecoveryNotice="Достижения восстановлены из резервной копии; повреждённый файл сохранён.";
                return restored; // Preserve the good backup during the next replacement.
            }
            catch(AchievementFutureSchemaException){blocked=true;throw;}
            catch(Exception e) when(!(e is AchievementFutureSchemaException)&&(e is IOException||e is InvalidDataException||e is ArgumentException||e is FormatException))
            {blocked=true;throw new InvalidDataException("Both achievement saves are unreadable; no automatic reset or overwrite.",first);}
        }
        private AchievementProfileStore Read(string file)
        {
            var info=new FileInfo(file);if(!info.Exists||info.Length>1024*1024)throw new InvalidDataException("Achievement save missing/too large.");
            AchievementProfileStore value;
            try{value=codec.Decode(File.ReadAllText(file,Encoding.UTF8));}
            catch(Exception e){throw new InvalidDataException("Invalid achievement JSON.",e);}
            if(value==null)throw new InvalidDataException("Empty achievement JSON.");value.Validate();return value;
        }
        public void Save(AchievementProfileStore store)
        {
            if(!opened||disposed||blocked)throw new InvalidOperationException("Achievement repository is unavailable; existing files preserved.");
            if(store==null)throw new ArgumentNullException(nameof(store));store.Validate();
            byte[] bytes=new UTF8Encoding(false).GetBytes(codec.Encode(store));if(bytes.Length>1024*1024)throw new InvalidDataException("Achievement save too large.");
            string temp=Path.Combine(directory,"achievements-v1."+Guid.NewGuid().ToString("N")+".tmp");
            try
            {
                using(var f=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){f.Write(bytes,0,bytes.Length);f.Flush(true);}
                // Read and validate before replacing any durable history.
                Read(temp);
                if(File.Exists(path))
                {
                    // A recovered corrupt primary must not replace the known-good backup.
                    if(RecoveryNotice!=null){File.Replace(temp,path,null);RecoveryNotice=null;}
                    else File.Replace(temp,path,backup);
                }
                else File.Move(temp,path);
            }
            finally{if(File.Exists(temp))File.Delete(temp);}
        }
        public void Dispose(){if(disposed)return;disposed=true;ownership?.Dispose();ownership=null;}
    }
}