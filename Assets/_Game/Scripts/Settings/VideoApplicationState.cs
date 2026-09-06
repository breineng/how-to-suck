using System;
namespace HowToSuck
{
    // One display application at a time. No file/settings ownership and no trial confirmation here.
    public sealed class VideoApplicationState
    {
        readonly Action<LocalVideoSettings> apply;
        readonly Func<LocalVideoSettings,bool> matches;
        readonly Func<LocalVideoSettings> safeDefault;
        LocalVideoSettings pending;
        Action<string> changed;
        string completed;
        double deadline;
        int issuedFrame;
        bool fallback;
        public ulong Ticket { get; private set; }
        public bool Pending => pending!=null;
        public string Message { get; private set; }="";
        public VideoApplicationState(Action<LocalVideoSettings> applyNative,Func<LocalVideoSettings,bool> matchesNative,Func<LocalVideoSettings> safe)
        {apply=applyNative??throw new ArgumentNullException(nameof(applyNative));matches=matchesNative??throw new ArgumentNullException(nameof(matchesNative));safeDefault=safe??throw new ArgumentNullException(nameof(safe));}
        public bool IsCurrent(ulong ticket)=>ticket!=0&&ticket==Ticket;
        void NewRequest()
        {
            if(Ticket==ulong.MaxValue)throw new InvalidOperationException("Display application generation exhausted.");
            Ticket++;pending=null;changed=null;Message="";
        }
        public ulong BeginTrial(LocalVideoSettings value)
        {if(value==null||!value.Valid)throw new ArgumentException("Invalid video mode.");NewRequest();apply(value.Copy());return Ticket;}
        public ulong BeginTracked(LocalVideoSettings value,double now,int frame,string pendingMessage,string completedMessage,Action<string> listener)
        {
            if(value==null||!value.Valid||double.IsNaN(now)||double.IsInfinity(now))throw new ArgumentException("Invalid display application.");
            NewRequest();pending=value.Copy();deadline=now+3;issuedFrame=frame;fallback=false;completed=completedMessage;changed=listener;
            ulong issued=Ticket;apply(pending.Copy());Notify(pendingMessage);return issued;
        }
        public void DetachListener(ulong ticket){if(IsCurrent(ticket))changed=null;}
        void Notify(string message){Message=message;changed?.Invoke(message);}
        public void Tick(double now,int frame)
        {
            if(pending==null||frame<issuedFrame+2)return;
            if(matches(pending)){pending=null;Notify(completed);return;}
            if(now<deadline)return;
            if(!fallback)
            {
                fallback=true;pending=safeDefault().Copy();deadline=now+3;issuedFrame=frame;
                completed="Исходный режим не применился. Применено безопасное окно.";
                apply(pending.Copy());Notify("Режим не подтвердился. Возвращаем безопасное окно…");
            }
            else {pending=null;Notify("Система не применила безопасный видеорежим. Перезапустите игру; неподтверждённый режим не сохранён.");}
        }
    }
}
