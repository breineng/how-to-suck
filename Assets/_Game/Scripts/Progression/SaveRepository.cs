using System;
using System.IO;
using System.Security.Cryptography;

namespace HowToSuck
{
    public enum SaveOpenKind { Ready, Created, RecoveredBackup, Corrupt, FutureSchema, UnknownTier, IoError, NotAuthority, Busy, OrphanTemporary }
    public enum SaveCommitKind { Committed, Retryable, Ambiguous, Conflict, NotAuthority, Invalid, Busy }
    public enum SaveFaultPoint { BeforeWrite, AfterTemporaryOpened, AfterTemporaryClosed, BeforeReplace, AfterReplace, BeforeVerification }
    // Small injection seam for real temporary-file tests. Production supplies null.
    public interface ISaveFaults { void At(SaveFaultPoint point); }
    public sealed class SaveOpenResult
    {
        public SaveOpenKind Kind {get;}
        public CampaignState State {get;}
        public string Error {get;}
        public bool Ready=>State!=null;
        internal SaveOpenResult(SaveOpenKind kind,CampaignState state=null,string error=null)
        {Kind=kind;State=state;Error=error;}
    }
    public sealed class SaveCommitResult
    {
        public SaveCommitKind Kind {get;}
        public string Error {get;}
        public bool Success=>Kind==SaveCommitKind.Committed;
        internal SaveCommitResult(SaveCommitKind kind,string error=null){Kind=kind;Error=error;}
    }

    // Synchronous, one owned repository/operation. Never construct/open this in the guest composition branch.
    public sealed class SaveRepository : IDisposable
    {
        public const string MainName="campaign-v1.json",TemporaryName="campaign-v1.tmp",BackupName="campaign-v1.bak",LockName="campaign-v1.lock";
        public string DirectoryPath {get;}
        public CampaignState Confirmed {get;private set;}
        public CampaignTierCatalog Tiers {get;}
        private readonly Func<bool> authority;
        private readonly ISaveFaults faults;
        private FileStream lease;
        private bool busy,disposed;
        private SaveOpenResult lastOpen;
        private byte[] observedMain,observedBackup,observedTemporary;
        // Only bytes captured through our exclusive writer handle belong to this exact immutable candidate.
        private CampaignState temporaryOwner,openingCandidate;
        private byte[] ownedTemporaryBytes,openingExpectedBytes;
        private SaveReadResult openingExpected;
        private string openingBackup;
        private SaveOpenKind openingKind;
        private string P(string name)=>Path.Combine(DirectoryPath,name);
        public bool IsAuthority=>!disposed&&authority();
        public SaveRepository(string rootDirectory,CampaignTierCatalog tiers,Func<bool> hasAuthority,ISaveFaults failureInjection=null)
        {
            if(string.IsNullOrWhiteSpace(rootDirectory)||!Path.IsPathRooted(rootDirectory))throw new ArgumentException("An explicit absolute own-campaign directory is required.");
            DirectoryPath=Path.GetFullPath(rootDirectory);Tiers=tiers??throw new ArgumentNullException(nameof(tiers));
            authority=hasAuthority??throw new ArgumentNullException(nameof(hasAuthority));faults=failureInjection;
            // No directory, file, lock or read is performed by construction.
        }
        private bool Own()=>!disposed&&authority();
        private void Lock()
        {
            if(lease!=null)return;
            Directory.CreateDirectory(DirectoryPath);
            lease=new FileStream(P(LockName),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        }
        public SaveOpenResult Open()
        {
            if(!Own())return new SaveOpenResult(SaveOpenKind.NotAuthority,error:"Only the current solo/host owns campaign storage.");
            if(busy)return new SaveOpenResult(SaveOpenKind.Busy,error:"A campaign operation is already running.");
            if(Confirmed!=null)return new SaveOpenResult(SaveOpenKind.Busy,error:"Repository is already open; resolve its exact pending operation or create a new lifetime.");
            busy=true;
            try {
                Lock();
                // Inspect before BOTH valid-main acceptance and backup recovery; neither may consume an orphan.
                var temporary=Read(TemporaryName);
                if(Blocked(temporary))return SetOpen(OpenFailure(temporary));
                if(temporary.Kind!=SaveReadKind.Missing&&!IsOwnedTemporary(openingCandidate,Bytes(TemporaryName)))
                    return SetOpen(new SaveOpenResult(SaveOpenKind.OrphanTemporary,error:"Unowned temporary campaign retained. Resolve it explicitly before opening; no campaign file was overwritten."));
                if(openingCandidate!=null)return RetryOpening();
                var main=Read(MainName);
                if(main.Kind==SaveReadKind.Valid){Confirmed=main.State;return SetOpen(new SaveOpenResult(SaveOpenKind.Ready,Confirmed));}
                if(Blocked(main))return SetOpen(OpenFailure(main));
                var backup=Read(BackupName);
                if(Blocked(backup))return SetOpen(OpenFailure(backup));
                if(backup.Kind==SaveReadKind.Valid)
                {
                    // Preserve the actual corrupt main separately. Do not rotate it over the valid backup.
                    byte[] prior=Fingerprint(MainName);
                    string archive=prior==null?null:ArchiveName("corrupt-main");
                    return BeginOpening(backup.State,main,archive,prior,SaveOpenKind.RecoveredBackup);
                }
                if(main.Kind==SaveReadKind.Missing&&backup.Kind==SaveReadKind.Missing)
                {
                    var candidate=new CampaignState(Guid.NewGuid().ToString("N"));
                    return BeginOpening(candidate,main,null,Fingerprint(MainName),SaveOpenKind.Created);
                }
                observedMain=Fingerprint(MainName);observedBackup=Fingerprint(BackupName);observedTemporary=Fingerprint(TemporaryName);
                return SetOpen(new SaveOpenResult(SaveOpenKind.Corrupt,error:"No valid main/backup campaign. Originals are retained; explicit Start New Campaign is required."));
            }catch(Exception e) when(IsIo(e)){return SetOpen(new SaveOpenResult(SaveOpenKind.IoError,error:e.Message));}
            finally{busy=false;}
        }
        // Explicit user action only. Token binds the exact reported corrupt files, not a generic overwrite switch.
        public SaveOpenResult StartNewAfterCorruption(SaveOpenResult observed)
        {
            if(!Own())return new SaveOpenResult(SaveOpenKind.NotAuthority,error:"Only own campaign may be reset.");
            if(busy)return new SaveOpenResult(SaveOpenKind.Busy,error:"Campaign operation pending.");
            if(!ReferenceEquals(observed,lastOpen)||observed==null||observed.Kind!=SaveOpenKind.Corrupt)
                return new SaveOpenResult(SaveOpenKind.Corrupt,error:"The current corruption report is required.");
            busy=true;
            try {
                Lock();
                if(!Equal(observedMain,Fingerprint(MainName))||!Equal(observedBackup,Fingerprint(BackupName))||!Equal(observedTemporary,Fingerprint(TemporaryName)))
                    return SetOpen(new SaveOpenResult(SaveOpenKind.IoError,error:"Campaign files changed after the corruption report. Reopen; nothing was overwritten."));
                // Archive every original before mutation. A failure here leaves the originals untouched.
                Archive(MainName,observedMain);Archive(BackupName,observedBackup);Archive(TemporaryName,observedTemporary);
                return BeginOpening(new CampaignState(Guid.NewGuid().ToString("N")),Read(MainName),ArchiveName("reset-main"),observedMain,SaveOpenKind.Created);
            }catch(Exception e) when(IsIo(e)){return SetOpen(new SaveOpenResult(SaveOpenKind.IoError,error:e.Message));}
            finally{busy=false;}
        }
        // The exact expected and candidate snapshots survive errors unchanged. Never recalculate their delta here.
        public SaveCommitResult Commit(CampaignState expected,CampaignState candidate)
        {
            if(!Own())return new SaveCommitResult(SaveCommitKind.NotAuthority,"Guest or disposed repository cannot save a campaign.");
            if(busy)return new SaveCommitResult(SaveCommitKind.Busy,"Another campaign operation is active.");
            if(expected==null||candidate==null||Confirmed==null||!expected.SameValues(Confirmed)||
                candidate.CampaignId!=expected.CampaignId||!Tiers.Contains(candidate.CurrentTierId))
                return new SaveCommitResult(SaveCommitKind.Invalid,"Expected confirmed own campaign and a valid candidate are required.");
            busy=true;
            try {
                Lock();
                var main=Read(MainName);
                if(main.Kind==SaveReadKind.Valid&&candidate.SameValues(main.State))
                {Confirmed=candidate;return new SaveCommitResult(SaveCommitKind.Committed);}
                if(main.Kind==SaveReadKind.IoError)return new SaveCommitResult(SaveCommitKind.Retryable,main.Error);
                if(main.Kind!=SaveReadKind.Valid||!expected.SameValues(main.State))
                    return new SaveCommitResult(SaveCommitKind.Conflict,"Disk is neither the exact expected nor candidate campaign; no overwrite: "+main.Error);
                var backup=ReadWithFingerprint(BackupName,out var expectedBackupBytes);
                if(backup.Kind==SaveReadKind.FutureSchema||backup.Kind==SaveReadKind.UnknownTier||backup.Kind==SaveReadKind.IoError)
                    return new SaveCommitResult(SaveCommitKind.Conflict,"Backup must not be overwritten: "+backup.Error);
                return Install(candidate,main,BackupName,Fingerprint(MainName),expectedBackupBytes);
            }catch(Exception e) when(IsIo(e)){return new SaveCommitResult(SaveCommitKind.Retryable,e.Message);}
            finally{busy=false;}
        }
        private SaveCommitResult Install(CampaignState candidate,SaveReadResult expected,string backupName,byte[] expectedBytes,byte[] expectedBackupBytes=null)
        {
            bool replacing=false;
            try {
                if(!Own())return new SaveCommitResult(SaveCommitKind.NotAuthority,"Authority was lost before writing.");
                var valid=CampaignSaveData.Decode(CampaignSaveData.Encode(candidate),Tiers);
                if(valid.Kind!=SaveReadKind.Valid)return new SaveCommitResult(SaveCommitKind.Invalid,valid.Error);
                byte[] encoded=CampaignSaveData.Encode(candidate);
                var conflict=CheckTemporary(candidate);
                if(conflict!=null)return conflict;
                faults?.At(SaveFaultPoint.BeforeWrite);
                conflict=CheckTemporary(candidate); // Fault seam / external writer may have added a file since the first check.
                if(conflict!=null)return conflict;
                bool exists=Bytes(TemporaryName)!=null;
                // CreateNew never truncates a newly appearing file. Open also never truncates until ownership is checked under its exclusive handle.
                using(var temporary=new FileStream(P(TemporaryName),exists?FileMode.Open:FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None))
                {
                    var prior=StreamBytes(temporary);
                    if(exists&&!IsOwnedTemporary(candidate,prior))return TempConflict();
                    try {
                        temporary.SetLength(0);temporary.Position=0;
                        faults?.At(SaveFaultPoint.AfterTemporaryOpened);
                        temporary.Write(encoded,0,encoded.Length);temporary.Flush(true);
                    }finally {RememberTemporary(candidate,temporary,encoded);}
                }
                faults?.At(SaveFaultPoint.AfterTemporaryClosed);
                var check=Read(TemporaryName);
                if(check.Kind!=SaveReadKind.Valid||!candidate.SameValues(check.State)||CheckTemporary(candidate)!=null)
                    return TempConflict();
                faults?.At(SaveFaultPoint.BeforeReplace);
                if(!Own())return new SaveCommitResult(SaveCommitKind.NotAuthority,"Authority was lost before replacement.");
                var current=Read(MainName);
                if(current.Kind==SaveReadKind.IoError)return new SaveCommitResult(SaveCommitKind.Retryable,current.Error);
                // Recheck immediately before the atomic operation. Cooperating processes also share the lifetime lock.
                if(!Equal(expectedBytes,Fingerprint(MainName))||current.Kind!=expected.Kind||current.Kind==SaveReadKind.Valid&&!current.State.SameValues(expected.State))
                    return new SaveCommitResult(SaveCommitKind.Conflict,"Main changed before replacement.");
                // Revalidate the owned bytes AFTER BeforeReplace as well; a matching main is insufficient.
                conflict=CheckTemporary(candidate);
                var finalTemporary=Read(TemporaryName);
                if(conflict!=null||finalTemporary.Kind!=SaveReadKind.Valid||!candidate.SameValues(finalTemporary.State))return TempConflict();
                // The SHA belongs to the bytes decoded at this attempt's compatibility check.
                // A different compatible backup is also a conflict; never adopt it at replacement time.
                if(backupName==BackupName&&!Equal(expectedBackupBytes,Fingerprint(BackupName)))
                    return new SaveCommitResult(SaveCommitKind.Conflict,"Backup changed after its compatibility check; no replacement.");
                replacing=true;
                if(current.Kind==SaveReadKind.Missing)File.Move(P(TemporaryName),P(MainName));
                else File.Replace(P(TemporaryName),P(MainName),backupName==null?null:P(backupName),false);
                faults?.At(SaveFaultPoint.AfterReplace);
                faults?.At(SaveFaultPoint.BeforeVerification);
                var installed=Read(MainName);
                if(installed.Kind!=SaveReadKind.Valid||!candidate.SameValues(installed.State))
                    return new SaveCommitResult(SaveCommitKind.Ambiguous,"Replaced file did not verify; reconcile this same candidate on Retry.");
                if(!Own())return new SaveCommitResult(SaveCommitKind.Ambiguous,"Authority changed after replacement; do not publish memory. Reopen own campaign later.");
                Confirmed=candidate;
                return new SaveCommitResult(SaveCommitKind.Committed);
            }catch(Exception e) when(IsIo(e))
            {return new SaveCommitResult(replacing?SaveCommitKind.Ambiguous:SaveCommitKind.Retryable,e.Message);}
        }
        private SaveOpenResult BeginOpening(CampaignState candidate,SaveReadResult expected,string backup,byte[] expectedBytes,SaveOpenKind kind)
        {
            openingCandidate=candidate;openingExpected=expected;openingBackup=backup;openingExpectedBytes=expectedBytes;openingKind=kind;
            return RetryOpening();
        }
        private SaveOpenResult RetryOpening()
        {
            // Same-lifetime retry keeps its original candidate/GUID, including ambiguous post-replace completion.
            var main=Read(MainName);
            if(main.Kind==SaveReadKind.Valid&&openingCandidate.SameValues(main.State))
            {Confirmed=openingCandidate;return SetOpen(OpeningSuccess());}
            var saved=Install(openingCandidate,openingExpected,openingBackup,openingExpectedBytes);
            return SetOpen(saved.Success?OpeningSuccess():
                new SaveOpenResult(SaveOpenKind.IoError,error:"Opening candidate remains pending; retry Open on this repository lifetime: "+saved.Error));
        }
        private SaveOpenResult OpeningSuccess()=>new SaveOpenResult(openingKind,Confirmed,
            openingKind==SaveOpenKind.RecoveredBackup?"Recovered a validated backup; original corrupt main was retained.":null);
        private static SaveCommitResult TempConflict()=>new SaveCommitResult(SaveCommitKind.Conflict,"Temporary campaign is incompatible, unowned, changed, or invalid; retained without overwrite.");
        private SaveCommitResult CheckTemporary(CampaignState candidate)
        {
            var bytes=Bytes(TemporaryName);
            return bytes==null||IsOwnedTemporary(candidate,bytes)?null:TempConflict();
        }
        private bool IsOwnedTemporary(CampaignState candidate,byte[] bytes)=>candidate!=null&&ReferenceEquals(candidate,temporaryOwner)&&
            bytes!=null&&Equal(bytes,ownedTemporaryBytes)&&Prefix(bytes,CampaignSaveData.Encode(candidate));
        private void RememberTemporary(CampaignState candidate,FileStream stream,byte[] encoded)
        {
            // Capture complete OR partial own-write bytes while no other handle can modify them. Never adopt bytes after close.
            temporaryOwner=null;ownedTemporaryBytes=null;
            try {var bytes=StreamBytes(stream);if(Prefix(bytes,encoded)){temporaryOwner=candidate;ownedTemporaryBytes=bytes;}}
            catch(Exception e) when(IsIo(e)){/* If ownership cannot be proved, a future retry must preserve the orphan. */}
        }
        private static bool Prefix(byte[] prefix,byte[] whole)
        {if(prefix==null||prefix.Length>whole.Length)return false;for(int i=0;i<prefix.Length;i++)if(prefix[i]!=whole[i])return false;return true;}
        private static byte[] StreamBytes(FileStream stream)
        {
            if(stream.Length>CampaignSaveData.MaximumBytes)return new byte[CampaignSaveData.MaximumBytes+1];
            stream.Position=0;byte[] bytes=new byte[stream.Length];int offset=0;
            while(offset<bytes.Length){int n=stream.Read(bytes,offset,bytes.Length-offset);if(n==0)throw new IOException("Temporary changed during owned-handle read.");offset+=n;}
            return bytes;
        }
        private SaveOpenResult SetOpen(SaveOpenResult value){lastOpen=value;return value;}
        private static bool Blocked(SaveReadResult value)=>value.Kind==SaveReadKind.FutureSchema||value.Kind==SaveReadKind.UnknownTier||value.Kind==SaveReadKind.IoError;
        private static SaveOpenResult OpenFailure(SaveReadResult value)=>new SaveOpenResult(
            value.Kind==SaveReadKind.FutureSchema?SaveOpenKind.FutureSchema:value.Kind==SaveReadKind.UnknownTier?SaveOpenKind.UnknownTier:SaveOpenKind.IoError,error:value.Error);
        private SaveReadResult ReadWithFingerprint(string name,out byte[] fingerprint)
        {
            fingerprint=null;
            try {
                // Share.Read prevents modification/removal while both hash and decode use the same handle.
                using(var stream=new FileStream(P(name),FileMode.Open,FileAccess.Read,FileShare.Read))
                {
                    using(var sha=SHA256.Create())fingerprint=sha.ComputeHash(stream);
                    return CampaignSaveData.Decode(StreamBytes(stream),Tiers);
                }
            }catch(FileNotFoundException){return new SaveReadResult(SaveReadKind.Missing);}
            catch(DirectoryNotFoundException){return new SaveReadResult(SaveReadKind.Missing);}
            catch(Exception e) when(IsIo(e)){return new SaveReadResult(SaveReadKind.IoError,error:e.Message);}
        }
        private SaveReadResult Read(string name)
        {
            try {var bytes=Bytes(name);return bytes==null?new SaveReadResult(SaveReadKind.Missing):CampaignSaveData.Decode(bytes,Tiers);}
            catch(Exception e) when(IsIo(e)){return new SaveReadResult(SaveReadKind.IoError,error:e.Message);}
        }
        private byte[] Bytes(string name)
        {
            try {
                using(var stream=new FileStream(P(name),FileMode.Open,FileAccess.Read,FileShare.Read))
                {
                    if(stream.Length>CampaignSaveData.MaximumBytes)return new byte[CampaignSaveData.MaximumBytes+1];
                    byte[] bytes=new byte[stream.Length];int offset=0;
                    while(offset<bytes.Length){int n=stream.Read(bytes,offset,bytes.Length-offset);if(n==0)throw new IOException("Save changed during read.");offset+=n;}
                    return bytes;
                }
            }catch(FileNotFoundException){return null;}
            catch(DirectoryNotFoundException){return null;}
        }
        private byte[] Fingerprint(string name)
        {
            try {using(var stream=new FileStream(P(name),FileMode.Open,FileAccess.Read,FileShare.Read))
                using(var sha=SHA256.Create())return sha.ComputeHash(stream);}
            catch(FileNotFoundException){return null;}
            catch(DirectoryNotFoundException){return null;}
        }
        private string ArchiveName(string source)=>source+"-"+Guid.NewGuid().ToString("N")+".json";
        private void Archive(string name,byte[] bytes)
        {
            if(bytes==null)return;
            // Copy original bytes from disk, including oversized corruption; never archive the bounded-read sentinel.
            File.Copy(P(name),P(ArchiveName(name)),false);
        }
        private static bool Equal(byte[] a,byte[] b)
        {if(a==null||b==null)return a==b;if(a.Length!=b.Length)return false;for(int i=0;i<a.Length;i++)if(a[i]!=b[i])return false;return true;}
        private static bool IsIo(Exception e)=>e is IOException||e is UnauthorizedAccessException||e is System.Security.SecurityException;
        public void Dispose(){if(busy)throw new InvalidOperationException("Cannot dispose during a campaign operation.");if(disposed)return;disposed=true;lease?.Dispose();lease=null;}
    }
}