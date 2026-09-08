using System;
using HowToSuck.Audio;
using UnityEngine;
namespace HowToSuck
{
    // Owned by this session root, including a guest root. There is no campaign authority or global lookup here.
    [DefaultExecutionOrder(-20000),DisallowMultipleComponent,RequireComponent(typeof(SessionRoot))]
    public sealed class LocalSettingsController : MonoBehaviour
    {
        public SessionRoot Session;
        public GameAudioRoot Audio;
        public bool IsInitialized {get;private set;}
        public string LastError {get;private set;}="";
        public LocalSettingsOpenKind OpenKind {get;private set;}
        public bool CanWrite {get;private set;}
        public bool HasUnappliedChanges=>IsInitialized&&!preview.Equals(confirmed);
        public event Action Changed;
        public float Sensitivity=>preview?.MouseSensitivity??.12f;
        public bool InvertY=>preview!=null&&preview.InvertY;
        public float FieldOfView=>preview?.FieldOfView??75;
        public float Feedback=>preview?.CameraFeedback??1;
        public string StoragePath=>repository?.PathName;
        public string RuntimeError=>Audio!=null?Audio.SettingsMixerError:"";
        private LocalSettingsRepository repository;
        private LocalSettingsData confirmed,preview;
        private PlayerInputReader reader;
        private PlayerView view;
        private bool subscribed;
        public LocalSettingsData DraftCopy()=>preview?.Copy()??new LocalSettingsData();
        public LocalSettingsData CommittedCopy()=>confirmed?.Copy()??new LocalSettingsData();
        public bool CommitVideo(LocalVideoSettings value,out string error)
        {
            error=null;if(!IsInitialized||value==null||!value.Valid){error="Недопустимый видеорежим.";return false;}
            var next=confirmed.Copy();next.Video=value.Copy();
            if(!repository.Save(next,out error)){LastError=error;Changed?.Invoke();return false;}
            confirmed=next;preview.Video=value.Copy();CanWrite=true;LastError="";Changed?.Invoke();return true;
        }
        public bool CommitBindings(LocalBindingOverride[] value,out string error)
        {
            error=null;if(!IsInitialized||!GameplayBindingPolicy.ValidateShape(value)){error="Недопустимые привязки управления.";return false;}
            var next=confirmed.Copy();next.Bindings=GameplayBindingPolicy.Copy(value);
            if(!repository.Save(next,out error)){LastError=error;Changed?.Invoke();return false;}
            confirmed=next;preview.Bindings=GameplayBindingPolicy.Copy(value);CanWrite=true;LastError="";Changed?.Invoke();return true;
        }
        private void Awake(){if(!IsInitialized)Initialize(Application.persistentDataPath);}
        // Explicit own-directory override permits isolated verification without touching the real player's preferences.
        public void Initialize(string ownSettingsDirectory)
        {
            if(IsInitialized)throw new InvalidOperationException("Local settings are already initialized.");
            if(Session==null||Session.gameObject!=gameObject||Audio==null||Audio.gameObject!=gameObject)
                throw new InvalidOperationException("Bind one settings controller explicitly to the SessionRoot and GameAudioRoot on the same object.");
            repository=new LocalSettingsRepository(ownSettingsDirectory);
            Load();IsInitialized=true;
            if(isActiveAndEnabled)Bind();
        }
        private void Load()
        {
            var opened=repository.Open();OpenKind=opened.Kind;CanWrite=opened.CanWrite;LastError=opened.Error??"";
            confirmed=opened.Data.Copy();preview=confirmed.Copy();
            // JSON always wins. Neither an old JSON nor reset/defaults consult PlayerPrefs again.
            if(opened.Kind==LocalSettingsOpenKind.Missing)
            {
                preview.AudioMigrationCompleted=true;
                for(int i=0;i<5;i++){
                    string key="hts.audio."+((AudioBus)i);
                    if(!PlayerPrefs.HasKey(key))continue;
                    float value=PlayerPrefs.GetFloat(key,float.NaN);
                    if(!float.IsNaN(value)&&!float.IsInfinity(value))SetBus(preview,(AudioBus)i,Mathf.Clamp01(value));
                }
                if(repository.Save(preview,out var error))confirmed=preview.Copy();else LastError=error;
            }
        }
        public bool Preview(LocalSettingsData value)
        {
            if(!IsInitialized||value==null||!value.Valid)return false;
            bool migrated=preview.AudioMigrationCompleted;preview=value.Copy();preview.AudioMigrationCompleted=migrated;ApplyRuntime();Changed?.Invoke();return true;
        }
        public bool PreviewVolume(AudioBus bus,float value)
        {
            if(!IsInitialized||(int)bus>=5||float.IsNaN(value)||float.IsInfinity(value)||value<0||value>1)return false;
            var next=DraftCopy();SetBus(next,bus,value);return Preview(next);
        }
        public bool Apply()
        {
            if(!IsInitialized)return false;
            if(!repository.Save(preview,out var error)){LastError=error;Changed?.Invoke();return false;}
            confirmed=preview.Copy();CanWrite=true;LastError="";Changed?.Invoke();return true;
        }
        public void CancelPreview(){if(!IsInitialized)return;preview=confirmed.Copy();ApplyRuntime();Changed?.Invoke();}
        public void PreviewDefaults(){Preview(new LocalSettingsData{AudioMigrationCompleted=true});}
        // Explicit reset archives a newer/corrupt settings file; it never opens or resets campaign data.
        public bool ResetDefaultsAndSave()
        {
            if(!IsInitialized)return false;
            var defaults=new LocalSettingsData{AudioMigrationCompleted=true};
            if(!repository.Save(defaults,out var error,true)){LastError=error;Changed?.Invoke();return false;}
            confirmed=defaults.Copy();preview=defaults;CanWrite=true;LastError="";ApplyRuntime();Changed?.Invoke();return true;
        }
        public void Reload(){if(!IsInitialized)return;Load();ApplyRuntime();Changed?.Invoke();}
        private static void SetBus(LocalSettingsData data,AudioBus bus,float value)
        {switch(bus){case AudioBus.Master:data.Master=value;break;case AudioBus.Vacuum:data.Vacuum=value;break;case AudioBus.Truck:data.Truck=value;break;case AudioBus.Impacts:data.Impacts=value;break;case AudioBus.UI:data.UI=value;break;}}
        private void ApplyRuntime(){if(Audio!=null)Audio.ApplyLocalSettings(this,preview);RefreshPlayer();}
        private void RefreshPlayer()
        {
            var motor=Session!=null?Session.LocalPlayer:null;
            var nextReader=motor!=null?motor.GetComponent<PlayerInputReader>():null;
            var nextView=motor!=null?motor.GetComponent<PlayerView>():null;
            if(reader!=nextReader){if(reader!=null)reader.BindLocalSettings(null);reader=nextReader;if(reader!=null)reader.BindLocalSettings(this);}
            if(view!=nextView){if(view!=null)view.BindLocalSettings(null);view=nextView;if(view!=null)view.BindLocalSettings(this);}
        }
        private void Bind()
        {
            if(!subscribed){Session.Changed+=RefreshPlayer;subscribed=true;}
            Audio.BindLocalSettings(this);ApplyRuntime();
        }
        private void OnEnable(){if(IsInitialized)Bind();}
        private void OnDisable()
        {
            if(Session!=null&&subscribed)Session.Changed-=RefreshPlayer;subscribed=false;
            if(reader!=null)reader.BindLocalSettings(null);if(view!=null)view.BindLocalSettings(null);reader=null;view=null;
            if(IsInitialized){preview=confirmed.Copy();if(Audio!=null){Audio.ApplyLocalSettings(this,preview);Audio.UnbindLocalSettings(this);}}
        }
        private void OnDestroy(){Changed=null;}
    }
}
