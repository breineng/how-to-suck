using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
namespace HowToSuck
{
 [DefaultExecutionOrder(-18500),DisallowMultipleComponent,RequireComponent(typeof(LocalSettingsController))]
 public sealed class LocalBindingsController:MonoBehaviour
 {
  public LocalSettingsController Settings;public SessionRoot Session;public InputActionAsset Actions;
  public string LastError {get;private set;}="";
  public event Action Changed;
  public bool Capturing=>waiting||operation!=null;
  public bool WaitingForRelease=>waiting;
  public bool HasUnapplied=>!GameplayBindingPolicy.Equal(draft,confirmed);
  GameplayBindingPolicy.Slot[] slots;
  public GameplayBindingPolicy.Slot[] Slots=>slots;
  LocalBindingOverride[] observedFile;
  InputActionAsset owned;LocalBindingOverride[] confirmed=Array.Empty<LocalBindingOverride>(),draft=Array.Empty<LocalBindingOverride>();
  InputActionRebindingExtensions.RebindingOperation operation;
  string profileWarning="";
  PlayerInputReader reader;bool waiting,committing;int pendingSlot;double waitDeadline;
  void OnEnable()
  {
   if(Settings==null||Session==null||Settings.gameObject!=gameObject||Session.gameObject!=gameObject||Actions==null||!Settings.IsInitialized)throw new InvalidOperationException("Bind local settings, session and authored actions explicitly on the same root.");
   slots=LocalBindingAdapter.Discover(Actions);owned=Instantiate(Actions);owned.name=Actions.name+" (Personal bindings)";owned.hideFlags=HideFlags.DontSave;owned.Disable();
   Settings.Changed+=SettingsChanged;Session.Changed+=BindReader;LoadConfirmed(true);BindReader();
  }
  void SettingsChanged(){if(!committing)LoadConfirmed(false);}
  void LoadConfirmed(bool force)
  {
   var values=Settings.CommittedCopy().Bindings;
   if(!force&&GameplayBindingPolicy.Equal(observedFile,values))return;
   observedFile=GameplayBindingPolicy.Copy(values);CancelCapture();
   if(!LocalBindingAdapter.Validate(slots,values,out var error)){profileWarning=LastError=error;values=Array.Empty<LocalBindingOverride>();}
   else profileWarning=LastError="";
   confirmed=GameplayBindingPolicy.Copy(values);draft=GameplayBindingPolicy.Copy(values);LocalBindingAdapter.Apply(owned,draft);ApplyReader();Changed?.Invoke();
  }
  void BindReader()
  {
   var next=Session.LocalPlayer!=null?Session.LocalPlayer.GetComponent<PlayerInputReader>():null;
   if(reader==next)return;
   if(reader!=null)reader.ApplyLocalBindings(null);reader=next;ApplyReader();
  }
  void ApplyReader(){if(reader!=null)reader.ApplyLocalBindings(confirmed);}
  public string Label(int slot)
  {var spec=slots[slot];return owned.FindAction("Gameplay/"+spec.Action,true).GetBindingDisplayString(LocalBindingAdapter.Index(owned,slots[slot]));}
  public string ActionLabel(string name)=>owned!=null?owned.FindAction("Gameplay/"+name)?.GetBindingDisplayString()??"—":"—";
  public bool BeginCapture(int slot)
  {
   if(!isActiveAndEnabled||owned==null||Capturing||slot<0||slot>=slots.Length)return false;
   if(reader!=null&&!reader.LocalModalOpen){LastError="Откройте меню управления перед изменением клавиш.";Changed?.Invoke();return false;}
   pendingSlot=slot;waiting=true;waitDeadline=Time.realtimeSinceStartupAsDouble+10;LastError="Отпустите нажатые кнопки, затем нажмите новую клавишу. Esc — отмена.";Changed?.Invoke();return true;
  }
  static bool AnyHeld()
  {
   if(Keyboard.current!=null)foreach(var key in Keyboard.current.allKeys)if(key.isPressed)return true;
   var m=Mouse.current;return m!=null&&(m.leftButton.isPressed||m.rightButton.isPressed||m.middleButton.isPressed||m.forwardButton.isPressed||m.backButton.isPressed);
  }
  void Update()
  {
   if(!waiting)return;if(Time.realtimeSinceStartupAsDouble>=waitDeadline){waiting=false;LastError="Кнопки не были отпущены. Повторите назначение.";Changed?.Invoke();return;}
   if(AnyHeld())return;waiting=false;
   var action=owned.FindAction("Gameplay/"+slots[pendingSlot].Action,true);
   operation=action.PerformInteractiveRebinding(LocalBindingAdapter.Index(owned,slots[pendingSlot])).WithExpectedControlType("Button")
    .WithControlsHavingToMatchPath("<Keyboard>").WithControlsHavingToMatchPath("<Mouse>").WithControlsExcluding("<Pointer>/position").WithControlsExcluding("<Pointer>/delta")
    .WithCancelingThrough("<Keyboard>/escape").WithMatchingEventsBeingSuppressed().WithActionEventNotificationsBeingSuppressed().WithTimeout(10)
    .OnApplyBinding((op,path)=>Accept(path)).OnComplete(Finished).OnCancel(Canceled);
   LastError="Нажмите новую клавишу или кнопку мыши. Esc — отмена.";
   try{operation.Start();}catch(Exception e)when(e is InvalidOperationException||e is ArgumentException){var failed=operation;operation=null;failed?.Dispose();LastError="Не удалось начать назначение. Закройте управление и повторите.";}
   Changed?.Invoke();
  }
  void Accept(string path)
  {
   var next=GameplayBindingPolicy.With(slots,draft,pendingSlot,path);
   if(!LocalBindingAdapter.Validate(slots,next,out var error)){LastError=error;return;}
   draft=next;LocalBindingAdapter.Apply(owned,draft);LastError="Привязка изменена. Нажмите «Сохранить управление».";
  }
  void Finished(InputActionRebindingExtensions.RebindingOperation op){if(operation==op)operation=null;op.Dispose();Changed?.Invoke();}
  void Canceled(InputActionRebindingExtensions.RebindingOperation op){LastError="Назначение отменено.";Finished(op);}
  public void CancelCapture()
  {
   if(waiting){waiting=false;LastError="Назначение отменено.";Changed?.Invoke();}
   var active=operation;if(active!=null){operation=null;active.Cancel();}
  }
  public void CancelDraft(){CancelCapture();draft=GameplayBindingPolicy.Copy(confirmed);if(owned!=null)LocalBindingAdapter.Apply(owned,draft);LastError=profileWarning;Changed?.Invoke();}
  public void PreviewDefaults(){CancelCapture();draft=Array.Empty<LocalBindingOverride>();LocalBindingAdapter.Apply(owned,draft);LastError="Стандартные клавиши выбраны. Сохраните или отмените изменения.";Changed?.Invoke();}
  public bool Save()
  {
   string error=null;if(Capturing||!LocalBindingAdapter.Validate(slots,draft,out error)){LastError=Capturing?"Завершите назначение клавиши.":error;Changed?.Invoke();return false;}
   bool saved;committing=true;try{saved=Settings.CommitBindings(draft,out error);}finally{committing=false;}
   if(!saved){LastError=error;Changed?.Invoke();return false;}
   confirmed=GameplayBindingPolicy.Copy(draft);observedFile=GameplayBindingPolicy.Copy(confirmed);profileWarning="";ApplyReader();LastError="Управление сохранено.";Changed?.Invoke();return true;
  }
  void OnDisable()
  {
   if(Settings!=null)Settings.Changed-=SettingsChanged;if(Session!=null)Session.Changed-=BindReader;
   CancelCapture();if(reader!=null)reader.ApplyLocalBindings(null);reader=null;
   if(owned!=null){owned.Disable();Destroy(owned);owned=null;}
  }
  void OnDestroy(){Changed=null;}
 }
}
