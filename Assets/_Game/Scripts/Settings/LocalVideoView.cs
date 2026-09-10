using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
namespace HowToSuck
{
 public sealed class LocalVideoView:MonoBehaviour
 {
  public Button[] Previous,Next;public TMP_Text[] Values;
  public Button PreviewButton,ConfirmButton,RejectButton;
  public GameObject Confirmation;public TMP_Text Message,Countdown;
  LocalVideoController controller;LocalVideoSettings[] modes;UnityAction[] previousActions,nextActions;
  int modeIndex,resolutionIndex,qualityIndex,vsyncIndex,rebuildFrame=-1;bool wasTrial,wasRestore;ulong displayedTicket;
  static readonly int[] ModeValues={3,1,0};static readonly string[] ModeNames={"Окно","Без рамки","Полноэкранный"};
  static int Cycle(int value,int delta,int length)=>length>0?(value+delta+length)%length:0;
  public void Bind(LocalVideoController owner)
  {
   if(controller==owner)return;Unbind();controller=owner;if(controller==null)return;
   if(Previous.Length!=4||Next.Length!=4||Values.Length!=4)throw new System.InvalidOperationException("Bind four actual video selector rows.");
   previousActions=new UnityAction[4];nextActions=new UnityAction[4];for(int i=0;i<4;i++){int index=i;previousActions[i]=()=>Change(index,-1);nextActions[i]=()=>Change(index,1);Previous[i].onClick.AddListener(previousActions[i]);Next[i].onClick.AddListener(nextActions[i]);}
   controller.Changed+=Refresh;PreviewButton.onClick.AddListener(Preview);ConfirmButton.onClick.AddListener(Confirm);RejectButton.onClick.AddListener(Reject);RebuildCurrent();Refresh();
  }
  void RebuildCurrent()
  {
   if(controller==null)return;var actual=controller.Current();modeIndex=System.Array.IndexOf(ModeValues,actual.Mode);if(modeIndex<0)modeIndex=0;
   modes=controller.Modes(ModeValues[modeIndex]);resolutionIndex=0;
   for(int i=0;i<modes.Length;i++)if(modes[i].Width==actual.Width&&modes[i].Height==actual.Height){resolutionIndex=i;if(actual.Mode!=0||(ulong)modes[i].RefreshNumerator*actual.RefreshDenominator==(ulong)actual.RefreshNumerator*modes[i].RefreshDenominator)break;}
   qualityIndex=Mathf.Clamp(actual.Quality,0,System.Math.Max(0,QualitySettings.names.Length-1));vsyncIndex=Mathf.Clamp(actual.Vsync,0,2);
  }
  void Change(int row,int delta)
  {
   if(controller==null||controller.TrialActive)return;
   if(row==0){modeIndex=Cycle(modeIndex,delta,3);modes=controller.Modes(ModeValues[modeIndex]);resolutionIndex=Mathf.Clamp(resolutionIndex,0,System.Math.Max(0,modes.Length-1));}
   else if(row==1)resolutionIndex=Cycle(resolutionIndex,delta,modes.Length);
   else if(row==2)qualityIndex=Cycle(qualityIndex,delta,QualitySettings.names.Length);
   else vsyncIndex=Cycle(vsyncIndex,delta,3);Refresh();
  }
  void Preview()
  {
   if(controller==null||modes==null||modes.Length==0||controller.TrialActive)return;
   var value=modes[resolutionIndex].Copy();value.Quality=qualityIndex;value.Vsync=vsyncIndex;
   if(controller.BeginTrial(value))displayedTicket=controller.TrialTicket;Refresh();
  }
  void Confirm(){controller.Confirm(displayedTicket);Refresh();}
  void Reject(){controller.CancelTrial();Refresh();}
  public bool CancelCurrent(){if(controller==null||!controller.TrialActive)return false;Reject();return true;}
  public void Close(){if(controller!=null)controller.CancelTrial();}
  void Refresh()
  {
   if(controller==null)return;bool active=controller.TrialActive;if((wasTrial&&!active)||(wasRestore&&!controller.RestorePending))rebuildFrame=Time.frameCount+2;wasTrial=active;wasRestore=controller.RestorePending;Confirmation.SetActive(active);
   for(int i=0;i<4;i++)Previous[i].interactable=Next[i].interactable=!active&&!controller.RestorePending&&controller.DisplayAvailable;
   PreviewButton.interactable=!active&&!controller.RestorePending&&controller.DisplayAvailable&&modes!=null&&modes.Length>0;ConfirmButton.interactable=active&&controller.CanConfirm;
   HowToSuck.LocalizedText.Set(Values[0], ModeNames[modeIndex]);var selected=modes!=null&&modes.Length>0?modes[resolutionIndex]:null;
   HowToSuck.LocalizedText.Set(Values[1], selected==null?"Нет доступных режимов":selected.Width+" × "+selected.Height+(selected.Mode==0?" · "+((double)selected.RefreshNumerator/selected.RefreshDenominator).ToString("0.##")+" Гц":""));
   HowToSuck.LocalizedText.Set(Values[2], QualitySettings.names.Length>0?QualitySettings.names[qualityIndex]:"—");HowToSuck.LocalizedText.Set(Values[3], vsyncIndex==0?"Выключена":vsyncIndex==1?"Каждый кадр":"Каждый второй кадр");
   HowToSuck.LocalizedText.Set(Message, controller.LastError);if(active)HowToSuck.LocalizedText.Set(Countdown, $"Оставить этот видеорежим?\nВозврат через {System.Math.Ceiling(controller.SecondsRemaining)} с.\nEsc или «Вернуть» отменяют изменение.");
  }
  void Update(){if(controller==null)return;if(rebuildFrame>=0&&Time.frameCount>=rebuildFrame){rebuildFrame=-1;RebuildCurrent();Refresh();}if(controller.TrialActive)Refresh();}
  void OnEnable(){if(controller!=null){RebuildCurrent();Refresh();}}
  void OnDisable()=>Close();
  void Unbind(){if(controller==null)return;Close();controller.Changed-=Refresh;if(previousActions!=null)for(int i=0;i<4;i++){Previous[i].onClick.RemoveListener(previousActions[i]);Next[i].onClick.RemoveListener(nextActions[i]);}PreviewButton.onClick.RemoveListener(Preview);ConfirmButton.onClick.RemoveListener(Confirm);RejectButton.onClick.RemoveListener(Reject);controller=null;}
  void OnDestroy()=>Unbind();
 }
}
