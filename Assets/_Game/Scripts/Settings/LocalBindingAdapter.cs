using System;
using System.Collections.Generic;
using UnityEngine.InputSystem;
namespace HowToSuck
{
 public static class LocalBindingAdapter
 {
  static bool Editable(string action)=>action=="Move"||action=="Sprint"||action=="Jump"||action=="Vacuum"||action=="Fire"||action=="Interact";
  static string Label(string action,string part)
  {if(action=="Move")return part=="Up"?"Вперёд":part=="Down"?"Назад":part=="Left"?"Влево":part=="Right"?"Вправо":part;
   return action=="Sprint"?"Бег":action=="Jump"?"Прыжок":action=="Vacuum"?"Засасывание":action=="Fire"?"Выстрел / выпуск":action=="Interact"?"Взаимодействие":action;}
  public static GameplayBindingPolicy.Slot[] Discover(InputActionAsset asset)
  {
   if(asset==null)throw new ArgumentNullException(nameof(asset));var rows=new List<GameplayBindingPolicy.Slot>();
   var map=asset.FindActionMap("Gameplay",true);
   foreach(var action in map.actions)if(Editable(action.name))foreach(var binding in action.bindings){
    if(binding.isComposite)continue;if(action.name=="Move"&&!binding.isPartOfComposite)continue;
    if(!GameplayBindingPolicy.SyntacticPath(binding.path))throw new InvalidOperationException("Authored editable default is not a concrete keyboard/mouse button: "+action.name);
    rows.Add(new GameplayBindingPolicy.Slot(binding.id.ToString(),action.name,Label(action.name,binding.name),binding.path,binding.isPartOfComposite?binding.name:null));
   }
   var pause=asset.FindAction("Gameplay/Pause",true);if(pause.bindings.Count!=1||pause.bindings[0].path!="<Keyboard>/escape")throw new InvalidOperationException("Esc recovery binding must remain immutable.");
   if(rows.Count==0||rows.Count>32)throw new InvalidOperationException("Bounded authored gameplay bindings required.");
   var result=rows.ToArray();if(!GameplayBindingPolicy.Validate(result,null,out var error))throw new InvalidOperationException(error);return result;
  }
  public static void RequireConfirmedGameplayDefaults(InputActionAsset asset)
  {
   if(asset==null)throw new ArgumentNullException(nameof(asset));
   var fire=asset.FindAction("Gameplay/Fire");var vacuum=asset.FindAction("Gameplay/Vacuum");
   if(fire==null||vacuum==null||fire.bindings.Count!=1||vacuum.bindings.Count!=1||fire.bindings[0].path!="<Mouse>/leftButton"||vacuum.bindings[0].path!="<Mouse>/rightButton")
    throw new InvalidOperationException("Confirmed gameplay input migration is pending: Fire=LMB and Vacuum=RMB must be actual authored actions, not invented settings rows.");
   Discover(asset);
  }
  public static int Index(InputActionAsset asset,GameplayBindingPolicy.Slot slot)
  {var action=asset.FindAction("Gameplay/"+slot.Action,true);for(int i=0;i<action.bindings.Count;i++)if(action.bindings[i].id.ToString()==slot.Id)return i;throw new InvalidOperationException("Authored gameplay binding identity missing: "+slot.Label);}
  public static bool Validate(GameplayBindingPolicy.Slot[] slots,LocalBindingOverride[] values,out string error)
  {
   if(!GameplayBindingPolicy.Validate(slots,values,out error))return false;if(values==null)return true;
   var keyboard=InputSystem.LoadLayout("Keyboard");var keys=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
   foreach(var item in keyboard.controls)if(item.layout.ToString()=="Key")keys.Add(item.name.ToString());
   foreach(var value in values)if(value.Path.StartsWith("<Keyboard>/",StringComparison.OrdinalIgnoreCase)&&!keys.Contains(value.Path.Substring(11))){error="Эта клавиша не поддерживается. Выберите другую или сбросьте управление.";return false;}return true;
  }
  public static void Apply(InputActionAsset owned,LocalBindingOverride[] values)
  {
   var slots=Discover(owned);if(!Validate(slots,values,out var error))throw new InvalidOperationException(error);
   foreach(var slot in slots){var action=owned.FindAction("Gameplay/"+slot.Action,true);int index=Index(owned,slot);action.RemoveBindingOverride(index);}
   if(values!=null)foreach(var value in values)foreach(var slot in slots)if(slot.Id==value.Id)owned.FindAction("Gameplay/"+slot.Action,true).ApplyBindingOverride(Index(owned,slot),value.Path);
  }
 }
}
