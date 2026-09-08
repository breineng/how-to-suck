using System;
using System.Collections.Generic;
using UnityEngine;
namespace HowToSuck
{
 [DefaultExecutionOrder(-19000),DisallowMultipleComponent,RequireComponent(typeof(LocalSettingsController))]
 public sealed class LocalVideoController:MonoBehaviour
 {
  public LocalSettingsController Settings;
  public string LastError {get;private set;}="";
  public event Action Changed;
  public bool TrialActive=>trial.Active;
  public bool RestorePending=>LocalVideoApplicationOwner.Pending;
  public ulong TrialTicket=>trial.Ticket;
  public double SecondsRemaining=>trial.Active?Math.Max(0,trial.Deadline-Time.realtimeSinceStartupAsDouble):0;
  public bool CanConfirm=>trial.CanConfirm(trial.Ticket,Time.realtimeSinceStartupAsDouble)&&Time.frameCount>=issuedFrame+2&&LocalVideoApplicationOwner.IsCurrent(application,applicationTicket)&&LocalVideoApplicationOwner.Matches(trial.Requested);
  public bool DisplayAvailable=>!Application.isBatchMode;
  readonly VideoTrialState trial=new VideoTrialState();
  LocalVideoSettings observedConfirmed;
  int issuedFrame;bool committing;
  VideoApplicationState application;ulong applicationTicket;
  void OnEnable()
  {
   if(Settings==null||Settings.gameObject!=gameObject||!Settings.IsInitialized)throw new InvalidOperationException("Bind initialized personal settings on the same root.");
   Settings.Changed+=OnSettingsChanged;OnSettingsChanged(true);
  }
  static bool Same(LocalVideoSettings a,LocalVideoSettings b)=>a==null?b==null:a.Equals(b);
  void OnSettingsChanged()=>OnSettingsChanged(false);
  void OnSettingsChanged(bool first)
  {
   if(committing)return;var confirmed=Settings.CommittedCopy().Video;
   if(!first&&Same(confirmed,observedConfirmed))return;
   CancelTrial();observedConfirmed=confirmed?.Copy();
   if(!DisplayAvailable){LastError="Настройки экрана доступны в обычном окне игры.";return;}
   var target=confirmed!=null&&Supported(confirmed)?confirmed:SafeDefault();
   LastError=confirmed!=null&&!Supported(confirmed)?"Сохранённый видеорежим недоступен. Применено безопасное окно.":"";
   ApplyTracked(target,confirmed!=null&&!Supported(confirmed)?"Сохранённый режим недоступен. Применяется безопасное окно…":"Применяется подтверждённый видеорежим…",confirmed!=null&&!Supported(confirmed)?"Сохранённый режим недоступен. Применено безопасное окно.":"Видеорежим применён.");Changed?.Invoke();
  }
  public LocalVideoSettings Current()=>LocalVideoApplicationOwner.Current();
  public LocalVideoSettings SafeDefault()=>LocalVideoApplicationOwner.SafeDefault();
  public LocalVideoSettings[] Modes(int mode)
  {
   var list=new List<LocalVideoSettings>();var seen=new HashSet<string>();var current=Current();
   foreach(var resolution in Screen.resolutions){var value=current.Copy();value.Width=resolution.width;value.Height=resolution.height;value.Mode=mode;value.RefreshNumerator=resolution.refreshRateRatio.numerator;value.RefreshDenominator=resolution.refreshRateRatio.denominator;
    string key=value.Width+"x"+value.Height+(mode==0?"@"+value.RefreshNumerator+"/"+value.RefreshDenominator:"");if(value.Valid&&seen.Add(key))list.Add(value);}
   if(mode==3){var safe=SafeDefault();if(seen.Add(safe.Width+"x"+safe.Height))list.Add(safe);current.Mode=3;if(current.Valid&&seen.Add(current.Width+"x"+current.Height))list.Add(current);}
   list.Sort((a,b)=>{int c=a.Width.CompareTo(b.Width);if(c!=0)return c;c=a.Height.CompareTo(b.Height);return c!=0?c:((double)a.RefreshNumerator/a.RefreshDenominator).CompareTo((double)b.RefreshNumerator/b.RefreshDenominator);});return list.ToArray();
  }
  public bool Supported(LocalVideoSettings value)
  {
   if(value==null||!value.Valid||value.Quality>=QualitySettings.names.Length)return false;
   foreach(var mode in Modes(value.Mode))if(mode.Width==value.Width&&mode.Height==value.Height&&(value.Mode!=0||(ulong)mode.RefreshNumerator*value.RefreshDenominator==(ulong)value.RefreshNumerator*mode.RefreshDenominator))return true;
   return false;
  }
  void ApplicationMessage(string message)
  {if(this==null||!isActiveAndEnabled)return;LastError=message;Changed?.Invoke();}
  void ApplyTracked(LocalVideoSettings value,string message,string completed="Видеорежим применён.")
  {
   if(!LocalVideoApplicationOwner.TryGet(out application))return;
   applicationTicket=application.BeginTracked(value,Time.realtimeSinceStartupAsDouble,Time.frameCount,message,completed,ApplicationMessage);
   LastError=application.Message;
  }
  public bool BeginTrial(LocalVideoSettings value)
  {
   LastError="";if(RestorePending){LastError="Дождитесь завершения смены видеорежима.";Changed?.Invoke();return false;}if(!DisplayAvailable||!Supported(value)){LastError="Этот видеорежим сейчас недоступен.";Changed?.Invoke();return false;}
   if(!trial.Begin(value,Current(),Time.realtimeSinceStartupAsDouble)){LastError="Сначала подтвердите или отмените текущий видеорежим.";Changed?.Invoke();return false;}
   if(!LocalVideoApplicationOwner.TryGet(out application)){trial.End();return false;}
   issuedFrame=Time.frameCount;applicationTicket=application.BeginTrial(value);Changed?.Invoke();return true;
  }
  public bool Confirm(ulong ticket)
  {
   if(!trial.CanConfirm(ticket,Time.realtimeSinceStartupAsDouble)||!CanConfirm){LastError="Видеорежим ещё не применён или время подтверждения истекло.";Changed?.Invoke();return false;}
   var candidate=trial.Requested.Copy();bool saved;string error;committing=true;try{saved=Settings.CommitVideo(candidate,out error);}finally{committing=false;}
   if(!saved){LastError=error;Changed?.Invoke();return false;}
   observedConfirmed=candidate;trial.End();LastError="Видеорежим сохранён.";Changed?.Invoke();return true;
  }
  public void CancelTrial()
  {
   if(!trial.Active)return;var previous=trial.Previous.Copy();trial.End();
   if(DisplayAvailable&&LocalVideoApplicationOwner.IsCurrent(application,applicationTicket))ApplyTracked(Supported(previous)?previous:SafeDefault(),"Возвращается предыдущий видеорежим…","Возвращён предыдущий видеорежим.");Changed?.Invoke();
  }
  void Update(){if(trial.Active&&(!LocalVideoApplicationOwner.IsCurrent(application,applicationTicket)||trial.Expired(Time.realtimeSinceStartupAsDouble)))CancelTrial();}
  void OnDisable(){if(Settings!=null)Settings.Changed-=OnSettingsChanged;CancelTrial();application?.DetachListener(applicationTicket);}
  void OnDestroy(){application?.DetachListener(applicationTicket);Changed=null;}
 }
}
