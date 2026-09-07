using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
namespace HowToSuck
{
 public sealed class LocalBindingView:MonoBehaviour
 {
  public bool KeyOnlyLabels;
  public Button[] BindButtons;
  public TMP_Text[] BindLabels;
  public Button SaveButton,DefaultsButton,CancelCaptureButton;
  public TMP_Text Message;
  LocalBindingsController controller;UnityAction[] callbacks;
  public void Bind(LocalBindingsController owner)
  {
   if(controller==owner)return;Unbind();controller=owner;if(controller==null)return;
   if(BindButtons.Length!=controller.Slots.Length||BindLabels.Length!=controller.Slots.Length)throw new System.InvalidOperationException("Author the controls screen from the actual current Gameplay bindings, including Fire.");
   callbacks=new UnityAction[BindButtons.Length];for(int i=0;i<callbacks.Length;i++){int index=i;callbacks[i]=()=>controller.BeginCapture(index);BindButtons[i].onClick.AddListener(callbacks[i]);}
   SaveButton.onClick.AddListener(Save);DefaultsButton.onClick.AddListener(Defaults);CancelCaptureButton.onClick.AddListener(Cancel);controller.Changed+=Refresh;Refresh();
  }
  void Save()=>controller.Save();void Defaults()=>controller.PreviewDefaults();void Cancel()=>controller.CancelCapture();
  public bool CancelCurrent(){if(controller==null||!controller.Capturing)return false;controller.CancelCapture();return true;}
  public void Close(){if(controller!=null&&controller.isActiveAndEnabled)controller.CancelDraft();}
  void Refresh()
  {
   if(controller==null)return;
   for(int i=0;i<BindButtons.Length;i++){BindLabels[i].text=KeyOnlyLabels?controller.Label(i):controller.Slots[i].Label+"\n"+controller.Label(i);BindButtons[i].interactable=!controller.Capturing;}
   SaveButton.interactable=DefaultsButton.interactable=!controller.Capturing;CancelCaptureButton.gameObject.SetActive(controller.WaitingForRelease);
   Message.text=string.IsNullOrEmpty(controller.LastError)?"Esc и управление меню не меняются. Выберите действие, затем клавишу. Нажмите Esc для отмены назначения.":controller.LastError;
  }
  void OnEnable(){if(controller!=null){controller.CancelDraft();Refresh();}}
  void OnDisable()=>Close();
  void Unbind(){if(controller==null)return;Close();controller.Changed-=Refresh;if(callbacks!=null)for(int i=0;i<callbacks.Length;i++)BindButtons[i].onClick.RemoveListener(callbacks[i]);SaveButton.onClick.RemoveListener(Save);DefaultsButton.onClick.RemoveListener(Defaults);CancelCaptureButton.onClick.RemoveListener(Cancel);callbacks=null;controller=null;}
  void OnDestroy()=>Unbind();
 }
}
