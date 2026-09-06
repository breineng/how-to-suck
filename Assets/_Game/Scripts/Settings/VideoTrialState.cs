using System;
namespace HowToSuck
{
 public sealed class VideoTrialState
 {
  public const double ConfirmSeconds=15;
  public bool Active {get;private set;}
  public ulong Ticket {get;private set;}
  public double Deadline {get;private set;}
  public LocalVideoSettings Requested {get;private set;}
  public LocalVideoSettings Previous {get;private set;}
  private double began;
  public bool Begin(LocalVideoSettings requested,LocalVideoSettings previous,double now)
  {
   if(Active||requested==null||previous==null||!requested.Valid||!previous.Valid||double.IsNaN(now)||double.IsInfinity(now)||now<0||now+ConfirmSeconds<=now||Ticket==ulong.MaxValue)return false;
   Ticket++;began=now;Deadline=now+ConfirmSeconds;Requested=requested.Copy();Previous=previous.Copy();Active=true;return true;
  }
  public bool CanConfirm(ulong ticket,double now)=>Active&&ticket==Ticket&&!double.IsNaN(now)&&!double.IsInfinity(now)&&now>=began&&now<Deadline;
  public bool Expired(double now)=>Active&&(!CanConfirm(Ticket,now));
  public void End(){Active=false;Requested=Previous=null;}
 }
}
