using System;
namespace HowToSuck
{
    [Serializable]
    public sealed class LocalSettingsData : IEquatable<LocalSettingsData>
    {
        public int Schema=1;
        public bool AudioMigrationCompleted;
        public float Master=.7f,Vacuum=1,Truck=1,Impacts=1,UI=1;
        public float MouseSensitivity=.12f;
        public bool InvertY;
        public float FieldOfView=75;
        public float CameraFeedback;
        public LocalSettingsData Copy()=>(LocalSettingsData)MemberwiseClone();
        private static bool In(float n,float min,float max)=>!float.IsNaN(n)&&!float.IsInfinity(n)&&n>=min&&n<=max;
        public bool Valid=>Schema==1&&In(Master,0,1)&&In(Vacuum,0,1)&&In(Truck,0,1)&&In(Impacts,0,1)&&In(UI,0,1)&&
            In(MouseSensitivity,.01f,2f)&&In(FieldOfView,40,110)&&In(CameraFeedback,0,1);
        public bool Equals(LocalSettingsData x)=>x!=null&&Schema==x.Schema&&AudioMigrationCompleted==x.AudioMigrationCompleted&&
            Master==x.Master&&Vacuum==x.Vacuum&&Truck==x.Truck&&Impacts==x.Impacts&&UI==x.UI&&MouseSensitivity==x.MouseSensitivity&&
            InvertY==x.InvertY&&FieldOfView==x.FieldOfView&&CameraFeedback==x.CameraFeedback;
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
