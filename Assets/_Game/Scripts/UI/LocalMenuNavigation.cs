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
  public LocalVideoController Video;public LocalBindingsController Bindings;
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
   string Label(string action){if(Bindings!=null&&Bindings.isActiveAndEnabled)return Bindings.ActionLabel(action);if(Reader!=null)return Reader.GameplayBindingDisplay(action);var found=actual.FindAction("Gameplay/"+action);return found!=null?found.GetBindingDisplayString():"—";}
   return "Движение: "+Label("Move")+"\nОбзор: "+Label("Look")+"\nБег: "+Label("Sprint")+" · Прыжок: "+Label("Jump")+
    "\nЗасасывание в хранилище: "+Label("Vacuum")+"\nВыстрел / выпуск предмета: "+Label("Fire")+"\nВзаимодействие: "+Label("Interact")+" · Меню / назад: Esc\n\n"+
    "Поглощённые предметы хранятся в пылесосе. Личный сбор не приносит денег. Предметами можно стрелять в противников или сдавать их в грузовик; стоимость учитывается только при сдаче.\n\n"+
    "Для успеха нужны сданная квота, победа над боссом текущего контракта и доставка этого побеждённого босса в грузовик. Одной квоты или победы без доставки недостаточно. Вместимость считается в предметах и улучшается отдельно от модели пылесоса.\n\n"+
    "Меню не останавливает таймер. Кампания принадлежит хосту; настройки и клавиши остаются личными.";
  }
  void OnDisable(){SceneManager.sceneLoaded-=Loaded;if(Session!=null)Session.Changed-=Refresh;foreach(var view in views)if(view!=null)view.Unbind(this);views.Clear();}
 }
}
