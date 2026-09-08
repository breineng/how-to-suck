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
  readonly List<LocalMenuView> views=new List<LocalMenuView>();
  public PlayerInputReader Reader=>Session!=null&&Session.LocalPlayer!=null?Session.LocalPlayer.GetComponent<PlayerInputReader>():null;
  void OnEnable(){if(Session==null||Settings==null||Session.gameObject!=gameObject||Settings.gameObject!=gameObject||Actions==null)throw new InvalidOperationException("Bind the existing root, personal settings and actual gameplay actions.");SceneManager.sceneLoaded+=Loaded;Session.Changed+=Refresh;BindScene();}
  void Loaded(Scene scene,LoadSceneMode mode)=>BindScene();
  void BindScene(){views.Clear();foreach(var view in FindObjectsByType<LocalMenuView>(FindObjectsInactive.Include,FindObjectsSortMode.None)){view.Bind(this);views.Add(view);}}
  void Refresh(){if(Session.Phase==SessionPhase.Loading||Session.Phase==SessionPhase.ShuttingDown||Session.Phase==SessionPhase.Results)foreach(var view in views)if(view!=null)view.Close(false);}
  void OnDisable(){SceneManager.sceneLoaded-=Loaded;if(Session!=null)Session.Changed-=Refresh;foreach(var view in views)if(view!=null)view.Unbind(this);views.Clear();}
 }
}
