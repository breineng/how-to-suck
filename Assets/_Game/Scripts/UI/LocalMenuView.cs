using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
namespace HowToSuck
{
 [DisallowMultipleComponent]
 public sealed class LocalMenuView:MonoBehaviour,ICancelHandler
 {
  public Button SettingsButton,HelpButton,CreditsButton;
  public CanvasGroup BaseGroup;
  public GameObject Panel,SettingsPanel,InformationPanel,ResetConfirmation;
  public TMP_Text Title,Information,Status,CloseLabel;
  public Slider Master,Vacuum,Truck,Impacts,UI,Sensitivity,Fov,Feedback;
  public TMP_Text MasterValue,VacuumValue,TruckValue,ImpactsValue,UIValue,SensitivityValue,FovValue,FeedbackValue;
  public Toggle InvertY;
  public Button ApplyButton,DefaultsButton,ReloadButton,ResetFileButton,ConfirmResetButton,CancelResetButton,CloseButton;
  public bool IsOpen {get;private set;}
  public LocalMenuNavigation Navigation {get;private set;}
  private int page;private bool refreshing,baseInteractable,baseRaycasts,confirming;
  private GameObject previousSelection;private PlayerInputReader input;
  public void Bind(LocalMenuNavigation owner)
  {
   if(Navigation==owner)return;Unbind(Navigation);Navigation=owner;if(owner==null)return;
   SettingsButton.onClick.AddListener(OpenSettings);HelpButton.onClick.AddListener(OpenHelp);CreditsButton.onClick.AddListener(OpenCredits);
   ApplyButton.onClick.AddListener(Apply);DefaultsButton.onClick.AddListener(Defaults);ReloadButton.onClick.AddListener(Reload);
   ResetFileButton.onClick.AddListener(AskReset);ConfirmResetButton.onClick.AddListener(ConfirmReset);CancelResetButton.onClick.AddListener(CancelReset);CloseButton.onClick.AddListener(Back);
   foreach(var slider in Sliders())slider.onValueChanged.AddListener(Preview);InvertY.onValueChanged.AddListener(PreviewBool);
   owner.Settings.Changed+=Refresh;Panel.SetActive(false);ResetConfirmation.SetActive(false);
  }
  Slider[] Sliders()=>new[]{Master,Vacuum,Truck,Impacts,UI,Sensitivity,Fov,Feedback};
  public void OpenSettings()=>Open(0);public void OpenHelp()=>Open(1);public void OpenCredits()=>Open(2);
  private void Open(int target)
  {
   if(Navigation==null||!Navigation.Settings.IsInitialized)return;
   var phase=Navigation.Session.Phase;if(phase==SessionPhase.Loading||phase==SessionPhase.ShuttingDown||phase==SessionPhase.Results)return;
   foreach(var shop in GetComponentsInChildren<ShopView>(true))if(shop.IsOpen)return;
   foreach(var recovery in GetComponentsInChildren<CampaignRecoveryView>(true))if(recovery.Panel!=null&&recovery.Panel.activeInHierarchy)return;
   var pause=GetComponent<MenuInputController>();if(pause!=null&&pause.ConfirmLeavePanel!=null&&pause.ConfirmLeavePanel.activeInHierarchy)return;
   if(IsOpen)Close(false);previousSelection=EventSystem.current!=null?EventSystem.current.currentSelectedGameObject:null;
   input=Navigation.Reader;if(input!=null)input.SetLocalModal(this,true);
   page=target;IsOpen=true;confirming=false;baseInteractable=BaseGroup.interactable;baseRaycasts=BaseGroup.blocksRaycasts;
   BaseGroup.interactable=false;BaseGroup.blocksRaycasts=false;Panel.SetActive(true);Refresh();Select(page==0?(Selectable)Master:CloseButton);
  }
  private void Preview(float unused){if(refreshing||!IsOpen||page!=0)return;var d=Navigation.Settings.DraftCopy();d.Master=Master.value;d.Vacuum=Vacuum.value;d.Truck=Truck.value;d.Impacts=Impacts.value;d.UI=UI.value;d.MouseSensitivity=Sensitivity.value;d.InvertY=InvertY.isOn;d.FieldOfView=Fov.value;d.CameraFeedback=Feedback.value;Navigation.Settings.Preview(d);}
  private void PreviewBool(bool unused)=>Preview(0);
  private void Apply(){if(IsOpen&&!confirming){bool saved=Navigation.Settings.Apply();Refresh();if(saved)Status.text="Настройки сохранены.";}}
  private void Defaults(){if(IsOpen&&!confirming)Navigation.Settings.PreviewDefaults();}
  private void Reload(){if(IsOpen&&!confirming){Navigation.Settings.Reload();Refresh();}}
  private void AskReset(){if(!IsOpen)return;confirming=true;Refresh();Select(CancelResetButton);}
  private void ConfirmReset(){if(!confirming)return;bool saved=Navigation.Settings.ResetDefaultsAndSave();confirming=false;Refresh();if(saved)Status.text="Сброшены только личные настройки. Кампания сохранена.";Select(CloseButton);}
  private void CancelReset(){confirming=false;Refresh();Select(ResetFileButton.gameObject.activeInHierarchy?ResetFileButton:CloseButton);}
  private void Back()=>Close(true);
  public void Close(bool restoreSelection)
  {
   if(!IsOpen)return;IsOpen=false;confirming=false;
   if(Navigation!=null&&Navigation.Settings!=null&&Navigation.Settings.isActiveAndEnabled)Navigation.Settings.CancelPreview();
   if(input!=null)input.SetLocalModal(this,false);input=null;
   if(BaseGroup!=null){BaseGroup.interactable=baseInteractable;BaseGroup.blocksRaycasts=baseRaycasts;}
   if(Panel!=null)Panel.SetActive(false);if(ResetConfirmation!=null)ResetConfirmation.SetActive(false);
   if(restoreSelection&&EventSystem.current!=null&&previousSelection!=null&&previousSelection.activeInHierarchy)EventSystem.current.SetSelectedGameObject(previousSelection);
   previousSelection=null;
  }
  public void OnCancel(BaseEventData data){if(!IsOpen)return;if(confirming)CancelReset();else Close(true);data.Use();}
  private void Refresh()
  {
   if(!IsOpen||Navigation==null)return;refreshing=true;
   try{
    var settings=Navigation.Settings;var d=settings.DraftCopy();Title.text=page==0?"Настройки":page==1?"Управление и справка":"Титры";
    SettingsPanel.SetActive(page==0);InformationPanel.SetActive(page!=0);ResetConfirmation.SetActive(confirming);CloseLabel.text=page==0?"Отмена / назад":"Назад";
    if(page==1)Information.text=Navigation.ControlsText();else if(page==2)Information.text=Navigation.Credits+"\n\nВерсия "+Application.version;
    Master.SetValueWithoutNotify(d.Master);Vacuum.SetValueWithoutNotify(d.Vacuum);Truck.SetValueWithoutNotify(d.Truck);Impacts.SetValueWithoutNotify(d.Impacts);UI.SetValueWithoutNotify(d.UI);
    Sensitivity.SetValueWithoutNotify(d.MouseSensitivity);Fov.SetValueWithoutNotify(d.FieldOfView);Feedback.SetValueWithoutNotify(d.CameraFeedback);InvertY.SetIsOnWithoutNotify(d.InvertY);
    MasterValue.text=Mathf.RoundToInt(d.Master*100)+"%";VacuumValue.text=Mathf.RoundToInt(d.Vacuum*100)+"%";TruckValue.text=Mathf.RoundToInt(d.Truck*100)+"%";ImpactsValue.text=Mathf.RoundToInt(d.Impacts*100)+"%";UIValue.text=Mathf.RoundToInt(d.UI*100)+"%";
    SensitivityValue.text=d.MouseSensitivity.ToString("0.00");FovValue.text=Mathf.RoundToInt(d.FieldOfView)+"°";FeedbackValue.text=Mathf.RoundToInt(d.CameraFeedback*100)+"%";
    Status.text=!string.IsNullOrEmpty(settings.LastError)?settings.LastError:!string.IsNullOrEmpty(settings.RuntimeError)?settings.RuntimeError:settings.HasUnappliedChanges?"Изменения ещё не сохранены.":"Настройки личные. Кампания и таймер контракта не изменяются.";
    foreach(var slider in Sliders())slider.interactable=!confirming;InvertY.interactable=!confirming;
    ApplyButton.interactable=!confirming&&settings.CanWrite;DefaultsButton.interactable=ReloadButton.interactable=CloseButton.interactable=!confirming;
    ResetFileButton.gameObject.SetActive(!settings.CanWrite);ResetFileButton.interactable=!confirming;
   }finally{refreshing=false;}
  }
  private void LateUpdate()
  {
   if(!IsOpen||EventSystem.current==null)return;var selected=EventSystem.current.currentSelectedGameObject;
   if(selected==null||!selected.transform.IsChildOf(Panel.transform)||!selected.activeInHierarchy)
    Select(confirming?CancelResetButton:CloseButton);
  }
  private static void Select(Selectable target){if(EventSystem.current!=null&&target!=null)EventSystem.current.SetSelectedGameObject(target.gameObject);}
  public void Unbind(LocalMenuNavigation owner)
  {
   if(Navigation!=owner||Navigation==null)return;Close(false);Navigation.Settings.Changed-=Refresh;
   SettingsButton.onClick.RemoveListener(OpenSettings);HelpButton.onClick.RemoveListener(OpenHelp);CreditsButton.onClick.RemoveListener(OpenCredits);
   ApplyButton.onClick.RemoveListener(Apply);DefaultsButton.onClick.RemoveListener(Defaults);ReloadButton.onClick.RemoveListener(Reload);ResetFileButton.onClick.RemoveListener(AskReset);
   ConfirmResetButton.onClick.RemoveListener(ConfirmReset);CancelResetButton.onClick.RemoveListener(CancelReset);CloseButton.onClick.RemoveListener(Back);
   foreach(var slider in Sliders())slider.onValueChanged.RemoveListener(Preview);InvertY.onValueChanged.RemoveListener(PreviewBool);Navigation=null;
  }
  private void OnDisable()=>Close(false);
  private void OnDestroy(){Unbind(Navigation);}
 }
}
