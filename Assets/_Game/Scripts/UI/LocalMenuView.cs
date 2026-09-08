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
  public Button SettingsButton;
  public CanvasGroup BaseGroup;
  public GameObject Panel,SettingsPanel,ResetConfirmation;
  public TMP_Text Title,Status,CloseLabel;
  public Slider Master,Vacuum,Truck,Impacts,UI,Sensitivity,Fov,Feedback;
  public TMP_Text MasterValue,VacuumValue,TruckValue,ImpactsValue,UIValue,SensitivityValue,FovValue,FeedbackValue;
  public Toggle InvertY;
  public Button ApplyButton,DefaultsButton,ReloadButton,ResetFileButton,ConfirmResetButton,CancelResetButton,CloseButton;
  public GameObject AdvancedButtons;
  public Button VideoOpenButton,BindingsOpenButton;
  public LocalVideoView VideoScreen;public LocalBindingView BindingScreen;
  // Approved concrete style is opt-in per authored Entry/Lobby; existing gameplay menus remain unchanged.
  public bool ConcreteTabsEnabled;
  public GameObject ConcreteTabs;
  public CanvasGroup ConcreteControls,ConcreteAudio,ConcreteVideo;
  public Button ControlsTab,AudioTab,ImageTab;
  public GameObject[] TabMarks;
  public bool IsOpen {get;private set;}
  public LocalMenuNavigation Navigation {get;private set;}
  private int page;private bool refreshing,baseInteractable,baseRaycasts,confirming;
  private GameObject previousSelection;private PlayerInputReader input;
  private SessionMenuView lobbyMenu;private MenuInputController pauseMenu;
  public void Bind(LocalMenuNavigation owner)
  {
   if(Navigation==owner)return;Unbind(Navigation);Navigation=owner;if(owner==null)return;
   lobbyMenu=GetComponentInParent<SessionMenuView>();pauseMenu=GetComponent<MenuInputController>();
   SettingsButton.onClick.AddListener(OpenSettings);
   ApplyButton.onClick.AddListener(Apply);DefaultsButton.onClick.AddListener(Defaults);ReloadButton.onClick.AddListener(Reload);
   ResetFileButton.onClick.AddListener(AskReset);ConfirmResetButton.onClick.AddListener(ConfirmReset);CancelResetButton.onClick.AddListener(CancelReset);CloseButton.onClick.AddListener(Back);
   foreach(var slider in Sliders())slider.onValueChanged.AddListener(Preview);InvertY.onValueChanged.AddListener(PreviewBool);
   if(VideoOpenButton!=null)VideoOpenButton.onClick.AddListener(OpenVideo);if(BindingsOpenButton!=null)BindingsOpenButton.onClick.AddListener(OpenBindings);
   if(VideoScreen!=null)VideoScreen.Bind(owner.Video);if(BindingScreen!=null)BindingScreen.Bind(owner.Bindings);
   owner.Settings.Changed+=Refresh;
   if(ConcreteTabsEnabled){ControlsTab.onClick.AddListener(OpenControlsTab);AudioTab.onClick.AddListener(OpenAudioTab);ImageTab.onClick.AddListener(OpenImageTab);
    if(owner.Bindings!=null)owner.Bindings.Changed+=Refresh;if(owner.Video!=null)owner.Video.Changed+=Refresh;}
   Panel.SetActive(false);ResetConfirmation.SetActive(false);
  }
  Slider[] Sliders()=>new[]{Master,Vacuum,Truck,Impacts,UI,Sensitivity,Fov,Feedback};
  public void OpenSettings()=>Open(0);
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
   BaseGroup.interactable=false;BaseGroup.blocksRaycasts=false;Panel.SetActive(true);Refresh();Select(page==0?(ConcreteTabsEnabled?(Selectable)Sensitivity:Master):CloseButton);
  }
  private void Preview(float unused){if(refreshing||!IsOpen||(page!=0&&(!ConcreteTabsEnabled||page!=5)))return;var d=Navigation.Settings.DraftCopy();d.Master=Master.value;d.Vacuum=Vacuum.value;d.Truck=Truck.value;d.Impacts=Impacts.value;d.UI=UI.value;d.MouseSensitivity=Sensitivity.value;d.InvertY=InvertY.isOn;d.FieldOfView=Fov.value;d.CameraFeedback=Feedback.value;Navigation.Settings.Preview(d);}
  private void PreviewBool(bool unused)=>Preview(0);
  private bool TabOperationPending=>ConcreteTabsEnabled&&Navigation!=null&&
   ((Navigation.Video!=null&&Navigation.Video.TrialActive)||(Navigation.Bindings!=null&&Navigation.Bindings.Capturing));
  private void Apply(){if(IsOpen&&!confirming&&!TabOperationPending){bool saved=Navigation.Settings.Apply();
   if(saved&&ConcreteTabsEnabled&&Navigation.Bindings!=null&&Navigation.Bindings.HasUnapplied)saved=Navigation.Bindings.Save();
   Refresh();Status.text=saved?"Настройки сохранены.":Navigation.Settings.LastError+" "+(Navigation.Bindings!=null?Navigation.Bindings.LastError:"");}}
  public void OpenControlsTab()=>SetPage(0);public void OpenAudioTab()=>SetPage(5);public void OpenImageTab()=>SetPage(3);
  private void Defaults(){if(!IsOpen||confirming||TabOperationPending)return;
   if(!ConcreteTabsEnabled){Navigation.Settings.PreviewDefaults();return;}
   var d=Navigation.Settings.DraftCopy();var defaults=new LocalSettingsData();
   if(page==0){d.MouseSensitivity=defaults.MouseSensitivity;d.InvertY=defaults.InvertY;d.FieldOfView=defaults.FieldOfView;d.CameraFeedback=defaults.CameraFeedback;Navigation.Bindings?.PreviewDefaults();}
   else if(page==5){d.Master=defaults.Master;d.Vacuum=defaults.Vacuum;d.Truck=defaults.Truck;d.Impacts=defaults.Impacts;d.UI=defaults.UI;}
   Navigation.Settings.Preview(d); // Preserve video; changing video always goes through its existing 15-second trial.
  }
  private void Reload(){if(IsOpen&&!confirming){Navigation.Settings.Reload();Refresh();}}
  private void AskReset(){if(!IsOpen)return;confirming=true;Refresh();Select(CancelResetButton);}
  private void ConfirmReset(){if(!confirming)return;bool saved=Navigation.Settings.ResetDefaultsAndSave();confirming=false;Refresh();if(saved)Status.text="Сброшены только личные настройки. Кампания сохранена.";Select(CloseButton);}
  private void CancelReset(){confirming=false;Refresh();Select(ResetFileButton.gameObject.activeInHierarchy?ResetFileButton:CloseButton);}
  public void OpenVideo(){if(IsOpen&&VideoScreen!=null&&Navigation.Video!=null)SetPage(3);}
  public void OpenBindings(){if(IsOpen&&BindingScreen!=null&&Navigation.Bindings!=null)SetPage(4);}
  private void SetPage(int target){if(!IsOpen)return;
   if(ConcreteTabsEnabled){if(TabOperationPending||confirming)return;page=target;Refresh();Select(target==0?(Selectable)Sensitivity:target==5?Master:VideoScreen.Previous[0]);return;}
   if(page==3&&VideoScreen!=null)VideoScreen.Close();if(page==4&&BindingScreen!=null)BindingScreen.Close();page=target;Refresh();Select(CloseButton);
  }
  private void Back(){if(ConcreteTabsEnabled)Close(true);else if(page>=3)SetPage(0);else Close(true);}
  public void Close(bool restoreSelection)
  {
   if(!IsOpen)return;IsOpen=false;confirming=false;
   if(VideoScreen!=null)VideoScreen.Close();if(BindingScreen!=null)BindingScreen.Close();
   if(Navigation!=null&&Navigation.Settings!=null&&Navigation.Settings.isActiveAndEnabled)Navigation.Settings.CancelPreview();
   if(input!=null)input.SetLocalModal(this,false);input=null;
   if(BaseGroup!=null){BaseGroup.interactable=baseInteractable;BaseGroup.blocksRaycasts=baseRaycasts;}
   if(Panel!=null)Panel.SetActive(false);if(ResetConfirmation!=null)ResetConfirmation.SetActive(false);
   if(restoreSelection&&EventSystem.current!=null&&previousSelection!=null&&previousSelection.activeInHierarchy)EventSystem.current.SetSelectedGameObject(previousSelection);
   previousSelection=null;
  }
  public void OnCancel(BaseEventData data){if(!IsOpen)return;if(confirming)CancelReset();
   else if((page==3||ConcreteTabsEnabled)&&VideoScreen!=null&&VideoScreen.CancelCurrent()){}
   else if((page==4||ConcreteTabsEnabled)&&BindingScreen!=null&&BindingScreen.CancelCurrent()){}
   else Back();data.Use();}
  private void Refresh()
  {
   if(!IsOpen||Navigation==null)return;refreshing=true;
   try{
    var settings=Navigation.Settings;var d=settings.DraftCopy();Title.text=page==0?"Настройки":page==3?"Видео":"Клавиши";
    SettingsPanel.SetActive(page==0||(ConcreteTabsEnabled&&(page==3||page==5)));ResetConfirmation.SetActive(confirming);CloseLabel.text=page==0?"Отмена / назад":"Назад";
    if(AdvancedButtons!=null)AdvancedButtons.SetActive(!ConcreteTabsEnabled&&page==0&&Navigation.Video!=null&&Navigation.Bindings!=null);
    if(ConcreteTabsEnabled){
     bool family=page==0||page==3||page==5;ConcreteTabs.SetActive(family);Title.text="НАСТРОЙКИ";
     Group(ConcreteControls,family&&page==0);Group(ConcreteAudio,family&&page==5);Group(ConcreteVideo,family&&page==3);
     // Remain enabled while switching tabs: LocalBindingView.OnDisable would discard an unsaved key draft.
     VideoScreen.gameObject.SetActive(family);BindingScreen.gameObject.SetActive(family);
     ControlsTab.interactable=AudioTab.interactable=ImageTab.interactable=!TabOperationPending&&!confirming;
     if(TabMarks!=null&&TabMarks.Length==3)for(int i=0;i<3;i++)if(TabMarks[i]!=null)TabMarks[i].SetActive(i==(page==0?0:page==5?1:2));
     ApplyButton.gameObject.SetActive(family&&page!=3);DefaultsButton.gameObject.SetActive(family&&page!=3);
     ReloadButton.gameObject.SetActive(family&&page!=3&&!settings.CanWrite);CloseLabel.text="Назад";
     CloseButton.gameObject.SetActive(Navigation.Video==null||!Navigation.Video.TrialActive);
    }else{if(VideoScreen!=null)VideoScreen.gameObject.SetActive(page==3);if(BindingScreen!=null)BindingScreen.gameObject.SetActive(page==4);}
    Master.SetValueWithoutNotify(d.Master);Vacuum.SetValueWithoutNotify(d.Vacuum);Truck.SetValueWithoutNotify(d.Truck);Impacts.SetValueWithoutNotify(d.Impacts);UI.SetValueWithoutNotify(d.UI);
    Sensitivity.SetValueWithoutNotify(d.MouseSensitivity);Fov.SetValueWithoutNotify(d.FieldOfView);Feedback.SetValueWithoutNotify(d.CameraFeedback);InvertY.SetIsOnWithoutNotify(d.InvertY);
    MasterValue.text=Mathf.RoundToInt(d.Master*100)+"%";VacuumValue.text=Mathf.RoundToInt(d.Vacuum*100)+"%";TruckValue.text=Mathf.RoundToInt(d.Truck*100)+"%";ImpactsValue.text=Mathf.RoundToInt(d.Impacts*100)+"%";UIValue.text=Mathf.RoundToInt(d.UI*100)+"%";
    SensitivityValue.text=d.MouseSensitivity.ToString("0.00");FovValue.text=Mathf.RoundToInt(d.FieldOfView)+"°";FeedbackValue.text=Mathf.RoundToInt(d.CameraFeedback*100)+"%";
    Status.text=!string.IsNullOrEmpty(settings.LastError)?settings.LastError:!string.IsNullOrEmpty(settings.RuntimeError)?settings.RuntimeError:(settings.HasUnappliedChanges||ConcreteTabsEnabled&&Navigation.Bindings!=null&&Navigation.Bindings.HasUnapplied)?"Изменения ещё не сохранены.":"Настройки личные. Кампания и таймер контракта не изменяются.";
    foreach(var slider in Sliders())slider.interactable=!confirming;InvertY.interactable=!confirming;
    ApplyButton.interactable=!confirming&&!TabOperationPending&&settings.CanWrite;DefaultsButton.interactable=ReloadButton.interactable=!confirming&&!TabOperationPending;CloseButton.interactable=!confirming;
    ResetFileButton.gameObject.SetActive(!settings.CanWrite&&(!ConcreteTabsEnabled||page!=3));ResetFileButton.interactable=!confirming;
   }finally{refreshing=false;}
  }
  private void LateUpdate()
  {
   // Lobby launchers must not draw or intercept input over another modal.
   bool launchersVisible=lobbyMenu==null&&!IsOpen
    &&(pauseMenu==null||pauseMenu.ConfirmLeavePanel==null||!pauseMenu.ConfirmLeavePanel.activeInHierarchy);
   ShowLauncher(SettingsButton,launchersVisible);
   if(!IsOpen||EventSystem.current==null)return;var selected=EventSystem.current.currentSelectedGameObject;
   var focusRoot=confirming?ResetConfirmation:ConcreteTabsEnabled&&Navigation?.Video!=null&&Navigation.Video.TrialActive?VideoScreen.Confirmation:Panel;
   if(selected==null||!selected.transform.IsChildOf(focusRoot.transform)||!selected.activeInHierarchy||selected.GetComponent<Selectable>() is Selectable selectable&&!selectable.IsInteractable())
    Select(confirming?CancelResetButton:ConcreteTabsEnabled&&Navigation?.Video!=null&&Navigation.Video.TrialActive?VideoScreen.RejectButton:CloseButton);
  }
  private static void Group(CanvasGroup group,bool visible){if(group!=null){group.alpha=visible?1:0;group.interactable=visible;group.blocksRaycasts=visible;}}
  private static void ShowLauncher(Button button,bool visible){if(button!=null&&button.gameObject.activeSelf!=visible)button.gameObject.SetActive(visible);}
  private static void Select(Selectable target){if(EventSystem.current!=null&&target!=null)EventSystem.current.SetSelectedGameObject(target.gameObject);}
  public void Unbind(LocalMenuNavigation owner)
  {
   if(Navigation!=owner||Navigation==null)return;Close(false);Navigation.Settings.Changed-=Refresh;
   SettingsButton.onClick.RemoveListener(OpenSettings);
   if(ConcreteTabsEnabled){ControlsTab.onClick.RemoveListener(OpenControlsTab);AudioTab.onClick.RemoveListener(OpenAudioTab);ImageTab.onClick.RemoveListener(OpenImageTab);
    if(owner.Bindings!=null)owner.Bindings.Changed-=Refresh;if(owner.Video!=null)owner.Video.Changed-=Refresh;}
   ApplyButton.onClick.RemoveListener(Apply);DefaultsButton.onClick.RemoveListener(Defaults);ReloadButton.onClick.RemoveListener(Reload);ResetFileButton.onClick.RemoveListener(AskReset);
   ConfirmResetButton.onClick.RemoveListener(ConfirmReset);CancelResetButton.onClick.RemoveListener(CancelReset);CloseButton.onClick.RemoveListener(Back);
   if(VideoOpenButton!=null)VideoOpenButton.onClick.RemoveListener(OpenVideo);if(BindingsOpenButton!=null)BindingsOpenButton.onClick.RemoveListener(OpenBindings);
   if(VideoScreen!=null)VideoScreen.Bind(null);if(BindingScreen!=null)BindingScreen.Bind(null);
   foreach(var slider in Sliders())slider.onValueChanged.RemoveListener(Preview);InvertY.onValueChanged.RemoveListener(PreviewBool);Navigation=null;
  }
  private void OnDisable()=>Close(false);
  private void OnDestroy(){Unbind(Navigation);}
 }
}
