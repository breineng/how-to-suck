using System;
namespace HowToSuck
{
    [Serializable]
    public sealed class LocalSettingsData : IEquatable<LocalSettingsData>
    {
        public int Schema=2;
        public string Language=GameLanguages.Automatic;
        public LocalVideoSettings Video;
        public LocalBindingOverride[] Bindings=Array.Empty<LocalBindingOverride>();
        public bool AudioMigrationCompleted;
        public float Master=.7f,Vacuum=.25f,Truck=1,Impacts=1,UI=.5f;
        public float MouseSensitivity=.12f;
        public bool InvertY;
        public float FieldOfView=75;
        public float CameraFeedback=1f;
        public LocalSettingsData Copy(){var result=(LocalSettingsData)MemberwiseClone();result.Video=Video?.Copy();result.Bindings=GameplayBindingPolicy.Copy(Bindings);return result;}
        private static bool In(float n,float min,float max)=>!float.IsNaN(n)&&!float.IsInfinity(n)&&n>=min&&n<=max;
        public bool Valid=>Schema==2&&GameLanguages.IsValidPreference(Language)&&(Video==null||Video.Valid)&&GameplayBindingPolicy.ValidateShape(Bindings)&&In(Master,0,1)&&In(Vacuum,0,1)&&In(Truck,0,1)&&In(Impacts,0,1)&&In(UI,0,1)&&
            In(MouseSensitivity,.01f,2f)&&In(FieldOfView,40,110)&&In(CameraFeedback,0,1);
        public bool Equals(LocalSettingsData x)=>x!=null&&Schema==x.Schema&&(Language??GameLanguages.Automatic)==(x.Language??GameLanguages.Automatic)&&AudioMigrationCompleted==x.AudioMigrationCompleted&&
            Master==x.Master&&Vacuum==x.Vacuum&&Truck==x.Truck&&Impacts==x.Impacts&&UI==x.UI&&MouseSensitivity==x.MouseSensitivity&&
            InvertY==x.InvertY&&FieldOfView==x.FieldOfView&&CameraFeedback==x.CameraFeedback&&
            (Video==null?x.Video==null:Video.Equals(x.Video))&&GameplayBindingPolicy.Equal(Bindings,x.Bindings);
    }
    public enum LocalSettingsOpenKind { Missing,Loaded,RecoveredBackup,DefaultsAfterCorruption,FutureVersion,IoError }
    public sealed class LocalSettingsOpenResult
    {
        public LocalSettingsOpenKind Kind;
        public LocalSettingsData Data;
        public string Error;
        public bool CanWrite;
    }
}
