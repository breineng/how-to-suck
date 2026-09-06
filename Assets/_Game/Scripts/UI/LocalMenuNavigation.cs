using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;
namespace HowToSuck
{
 [DisallowMultipleComponent,RequireComponent(typeof(LocalSettingsController))]
 public sealed class LocalMenuNavigation:MonoBehaviour
 {
  public SessionRoot Session;public LocalSettingsController Settings;public InputActionAsset Actions;
  [TextArea(5,16)]public string Credits;
  readonly List<LocalMenuView> views=new List<LocalMenuView>();
  public PlayerInputReader Reader=>Session!=null&&Session.LocalPlayer!=null?Session.LocalPlayer.GetComponent<PlayerInputReader>():null;
  void OnEnable(){if(Session==null||Settings==null||Session.gameObject!=gameObject||Settings.gameObject!=gameObject||Actions==null)throw new InvalidOperationException("Bind the existing root, personal settings and actual gameplay actions.");SceneManager.sceneLoaded+=Loaded;Session.Changed+=Refresh;BindScene();}
  void Loaded(Scene scene,LoadSceneMode mode)=>BindScene();
  void BindScene(){views.Clear();foreach(var view in FindObjectsByType<LocalMenuView>(FindObjectsInactive.Include,FindObjectsSortMode.None)){view.Bind(this);views.Add(view);}}
  void Refresh(){if(Session.Phase==SessionPhase.Loading||Session.Phase==SessionPhase.ShuttingDown||Session.Phase==SessionPhase.Results)foreach(var view in views)if(view!=null)view.Close(false);}
  public string ControlsText()
  {
   var actual=Reader!=null?Reader.Actions:Actions;
   string Label(string action){if(Reader!=null)return Reader.GameplayBindingDisplay(action);var found=actual.FindAction("Gameplay/"+action);return found!=null?found.GetBindingDisplayString():"—";}
   return "Движение: "+Label("Move")+"\nОбзор: "+Label("Look")+"\nБег: "+Label("Sprint")+"\nПрыжок: "+Label("Jump")+
    "\nВсасывание: "+Label("Vacuum")+"\nЭвакуация / взаимодействие: "+Label("Interact")+"\nМеню / назад: Esc\n\n"+
    "Собирайте предметы и выполняйте квоту контракта. Большие предметы можно подтолкнуть к грузовику. Для эвакуации вся команда должна собраться у грузовика и удерживать кнопку взаимодействия.\n\n"+
    "Меню не останавливает время контракта. Кампания, общий баланс и оборудование принадлежат хосту; ваши настройки остаются личными. Условия выплаты при провале указаны на карточке карты.";
  }
  void OnDisable(){SceneManager.sceneLoaded-=Loaded;if(Session!=null)Session.Changed-=Refresh;foreach(var view in views)if(view!=null)view.Unbind(this);views.Clear();}
 }
}
