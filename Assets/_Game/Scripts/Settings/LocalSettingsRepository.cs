using System;
using System.IO;
using System.Text;
using System.Security.Cryptography;
using Newtonsoft.Json;
namespace HowToSuck
{
    // Personal preferences only. Never opens a campaign, achievement profile or remote player's repository.
    public sealed class LocalSettingsRepository
    {
        public const string FileName="settings-v1.json";
        public string DirectoryPath {get;}
        public string PathName=>Path.Combine(DirectoryPath,FileName);
        private string baseline;
        private bool opened,writable,primaryWasValid;
        private readonly Action beforeReplace;
        private static readonly UTF8Encoding Utf8=new UTF8Encoding(false,true);
        private static readonly JsonSerializerSettings JsonSettings=new JsonSerializerSettings{TypeNameHandling=TypeNameHandling.None,MaxDepth=8,CheckAdditionalContent=true};
        // Optional bounded IO failure seam for native/pure verification. It cannot alter the data or target path.
        public LocalSettingsRepository(string ownDirectory,Action beforeAtomicReplace=null)
        {if(string.IsNullOrWhiteSpace(ownDirectory))throw new ArgumentException("Own settings directory required");DirectoryPath=Path.GetFullPath(ownDirectory);beforeReplace=beforeAtomicReplace;}
        private FileStream Lock()
        {Directory.CreateDirectory(DirectoryPath);return new FileStream(Path.Combine(DirectoryPath,FileName+".write-lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}
        private static string Digest(byte[] bytes){if(bytes==null)return null;using(var sha=SHA256.Create())return Convert.ToBase64String(sha.ComputeHash(bytes));}
        private static byte[] Read(string path)
        {
            if(!File.Exists(path))return null;
            using(var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete)){
                if(file.Length>65536)throw new InvalidDataException("Settings file exceeds the bounded format");
                using(var memory=new MemoryStream()){file.CopyTo(memory);return memory.ToArray();}
            }
        }
        private static LocalSettingsData Parse(byte[] bytes)
        {
            if(bytes==null)return null;var value=JsonConvert.DeserializeObject<LocalSettingsData>(Utf8.GetString(bytes),JsonSettings);
            if(value==null)throw new InvalidDataException("Settings root is absent");
            if(value.Schema>1)return value;
            if(!value.Valid)throw new InvalidDataException("Settings values are outside supported ranges");return value;
        }
        private void Archive(string source,string kind)
        {
            if(!File.Exists(source))return;
            string target=Path.Combine(DirectoryPath,FileName+"."+kind+"."+DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff")+"."+Guid.NewGuid().ToString("N"));
            File.Copy(source,target,false);
        }
        public LocalSettingsOpenResult Open()
        {
            opened=writable=primaryWasValid=false;
            try{using(Lock()){
                byte[] bytes=Read(PathName);baseline=Digest(bytes);opened=true;
                if(bytes==null){writable=true;return Result(LocalSettingsOpenKind.Missing,new LocalSettingsData(),null,true);}
                LocalSettingsData value;
                try{value=Parse(bytes);}catch(Exception e)when(e is JsonException||e is InvalidDataException||e is DecoderFallbackException){
                    Archive(PathName,"corrupt");
                    try{value=Parse(Read(PathName+".bak"));}catch(Exception backupError)when(backupError is JsonException||backupError is InvalidDataException||backupError is DecoderFallbackException){value=null;}
                    writable=true;
                    if(value!=null&&value.Valid)return Result(LocalSettingsOpenKind.RecoveredBackup,value,"Настройки восстановлены из резервной копии. Повреждённый файл сохранён.",true);
                    return Result(LocalSettingsOpenKind.DefaultsAfterCorruption,new LocalSettingsData(),"Повреждённые настройки сохранены отдельно. Пока применены стандартные значения.",true);
                }
                if(value.Schema>1)return Result(LocalSettingsOpenKind.FutureVersion,new LocalSettingsData(),"Настройки созданы в новой версии игры. Можно играть со стандартными значениями или явно сбросить только настройки.",false);
                primaryWasValid=writable=true;return Result(LocalSettingsOpenKind.Loaded,value,null,true);
            }}catch(Exception e)when(e is IOException||e is UnauthorizedAccessException||e is System.Security.SecurityException){
                return Result(LocalSettingsOpenKind.IoError,new LocalSettingsData(),"Не удалось прочитать личные настройки. Кампания не изменена.",false);
            }
        }
        private static LocalSettingsOpenResult Result(LocalSettingsOpenKind kind,LocalSettingsData data,string error,bool canWrite)=>
            new LocalSettingsOpenResult{Kind=kind,Data=data.Copy(),Error=error,CanWrite=canWrite};
        public bool Save(LocalSettingsData value,out string error,bool explicitReset=false)
        {
            error=null;if(value==null||!value.Valid){error="Значения настроек недопустимы.";return false;}
            if(!opened||(!writable&&!explicitReset)){error="Сначала повторите загрузку настроек или подтвердите сброс только настроек.";return false;}
            string temp=Path.Combine(DirectoryPath,FileName+".tmp."+Guid.NewGuid().ToString("N"));
            try{using(Lock()){
                var current=Read(PathName);if(Digest(current)!=baseline){error="Настройки изменены другим экземпляром игры. Повторите загрузку перед сохранением.";return false;}
                if(explicitReset&&current!=null)Archive(PathName,"before-reset");
                var bytes=Utf8.GetBytes(JsonConvert.SerializeObject(value,Formatting.Indented,JsonSettings)+"\n");
                using(var file=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){file.Write(bytes,0,bytes.Length);file.Flush(true);}
                beforeReplace?.Invoke();
                if(current!=null)File.Replace(temp,PathName,primaryWasValid?PathName+".bak":null,true);else File.Move(temp,PathName);
                baseline=Digest(bytes);writable=primaryWasValid=true;return true;
            }}catch(Exception e)when(e is IOException||e is UnauthorizedAccessException||e is System.Security.SecurityException){
                error="Не удалось сохранить личные настройки. Проверьте доступ к папке и свободное место, затем повторите.";return false;
            }finally{try{if(File.Exists(temp))File.Delete(temp);}catch(IOException){}catch(UnauthorizedAccessException){}}
        }
    }
}
