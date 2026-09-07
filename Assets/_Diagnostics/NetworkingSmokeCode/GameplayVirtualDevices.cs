#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Reflection;
using HowToSuck;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Utilities;
namespace HowToSuck.Diagnostics
{
    // Explicit diagnostic focus/device injection, not physical-OS-focus evidence.
    public sealed class GameplayVirtualDevices : IDisposable
    {
        private readonly InputSettings original,temporary;
        private readonly Keyboard oldKeyboard,keyboard;
        private readonly Mouse oldMouse,mouse;
        private PlayerInputReader reader;
        private PlayerMotor motor;
        private InputActionAsset actions;
        private ReadOnlyArray<InputDevice>? originalDevices;
        private bool disposed;
        private readonly Action<string> witness;
        private static readonly FieldInfo actionsField=typeof(PlayerInputReader).GetField("localActions",BindingFlags.Instance|BindingFlags.NonPublic);
        private static readonly FieldInfo focusField=typeof(PlayerInputReader).GetField("focused",BindingFlags.Instance|BindingFlags.NonPublic);
        private InputAction moveAction,vacuumAction,interactAction,fireAction;
        private int focusReleaseFrames;
        public int FocusReacquisitions {get;private set;}
        public int CloneBindings {get;private set;}
        public int BoundActionInstanceId=>actions!=null?actions.GetInstanceID():0;
        public bool ReaderFocused=>reader!=null&&focusField!=null&&(bool)focusField.GetValue(reader);
        public bool ActionsEnabled=>moveAction!=null&&moveAction.enabled;
        public Vector2 ActualActionMove=>moveAction!=null?moveAction.ReadValue<Vector2>():Vector2.zero;
        public bool ActualActionVacuum=>vacuumAction!=null&&vacuumAction.IsPressed();
        public bool ActualActionFire=>fireAction!=null&&fireAction.IsPressed();
        public bool ActualActionInteract=>interactAction!=null&&interactAction.IsPressed();
        public ulong ConsecutiveNeutralDynamicFrames {get;private set;}
        public bool NeutralReleaseObserved=>ConsecutiveNeutralDynamicFrames>=2&&focusReleaseFrames==0&&
            !keyboard.wKey.isPressed&&!keyboard.aKey.isPressed&&!keyboard.sKey.isPressed&&!keyboard.dKey.isPressed&&
            !keyboard.eKey.isPressed&&!keyboard.spaceKey.isPressed&&!keyboard.leftShiftKey.isPressed&&!mouse.leftButton.isPressed&&!mouse.rightButton.isPressed&&
            ActualActionMove.sqrMagnitude<.00001f&&!ActualActionVacuum&&!ActualActionInteract&&!ActualActionFire&&
            (reader==null||(reader.LatestIntent.Move.sqrMagnitude<.00001f&&!reader.LatestIntent.VacuumHeld&&!reader.LatestIntent.InteractHeld&&!reader.LatestIntent.SprintHeld));
        public bool Forward,Backward,Left,Right,Vacuum,Interact,Jump,Sprint,Fire;
        public bool Aim;
        public Vector3 LookPoint;
        public int KeyboardId=>keyboard.deviceId;
        public int MouseId=>mouse.deviceId;
        public ulong QueuedDynamicFrames {get;private set;}
        public GameplayVirtualDevices(string nonce,int slot,Action<string> observation=null)
        {
            witness=observation;original=InputSystem.settings;
            try
            {
            temporary=UnityEngine.Object.Instantiate(original);
            temporary.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
#if UNITY_EDITOR
            temporary.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
            InputSystem.settings=temporary;oldMouse=Mouse.current;oldKeyboard=Keyboard.current;
            mouse=InputSystem.AddDevice<Mouse>("HTSGameplayMouse-"+nonce+"-"+slot);
            keyboard=InputSystem.AddDevice<Keyboard>("HTSGameplayKeyboard-"+nonce+"-"+slot);
            if(mouse.native||keyboard.native)throw new InvalidOperationException("Harness must own non-native devices.");
            mouse.MakeCurrent();keyboard.MakeCurrent();InputSystem.onBeforeUpdate+=Pump;
            }
            catch{Dispose();throw;}
        }
        public void Bind(PlayerMotor owner)
        {
            var candidate=owner!=null?owner.GetComponent<PlayerInputReader>():null;
            if(candidate==null||!candidate.IsInitialized)throw new InvalidOperationException("Bind only an initialized local owner's reader.");
            var current=actionsField?.GetValue(candidate) as InputActionAsset;
            if(current==null||focusField==null)throw new InvalidOperationException("Reviewed InputReader clone/focus fields changed.");
            if(owner==motor&&reader==candidate&&actions==current){MaintainFocus();return;}
            RestoreReader();motor=owner;reader=candidate;actions=current;
            originalDevices=actions.devices;actions.devices=new ReadOnlyArray<InputDevice>(new InputDevice[]{keyboard,mouse});
            moveAction=actions.FindAction("Gameplay/Move",true);vacuumAction=actions.FindAction("Gameplay/Vacuum",true);interactAction=actions.FindAction("Gameplay/Interact",true);fireAction=actions.FindAction("Gameplay/Fire",true);
            CloneBindings++;Neutral();focusReleaseFrames=2;
            witness?.Invoke("bind clone="+actions.GetInstanceID()+" reader="+reader.GetInstanceID()+" actualOSFocus="+Application.isFocused);
            AcquireFocus("new cloned action asset");
        }
        private void AcquireFocus(string reason)
        {
            FocusReacquisitions++;focusReleaseFrames=Math.Max(focusReleaseFrames,2);
            witness?.Invoke("simulated focus reacquire: "+reason+"; actualOSFocus="+Application.isFocused);
            reader.SendMessage("OnApplicationFocus",true,SendMessageOptions.RequireReceiver);
        }
        private void MaintainFocus()
        {
            if(reader==null)return;
            if(!ReaderFocused)AcquireFocus("observed Reader focus=false");
            else if(reader.isActiveAndEnabled&&reader.GameplayAvailable&&!reader.MenuOpen&&!ActionsEnabled)AcquireFocus("observed disabled gameplay actions");
        }

        public void Neutral(){Forward=Backward=Left=Right=Vacuum=Interact=Jump=Sprint=Fire=Aim=false;}
        private void Pump()
        {
            if(disposed||InputState.currentUpdateType!=InputUpdateType.Dynamic)return;
            MaintainFocus();bool release=focusReleaseFrames>0;if(release)focusReleaseFrames--;
            Vector2 delta=Vector2.zero;
            if(!release&&Aim&&reader!=null&&motor!=null)
            {
                Vector3 direction=LookPoint-(motor.transform.position+Vector3.up*motor.Settings.EyeHeight);
                if(direction.sqrMagnitude>.0001f)
                {
                    float yaw=Mathf.Atan2(direction.x,direction.z)*Mathf.Rad2Deg;
                    float pitch=-Mathf.Atan2(direction.y,new Vector2(direction.x,direction.z).magnitude)*Mathf.Rad2Deg;
                    delta=new Vector2(Mathf.DeltaAngle(reader.LatestIntent.Yaw,yaw),reader.LatestIntent.Pitch-Mathf.Clamp(pitch,-80,80))/reader.EffectiveMouseSensitivity;
                    if(reader.EffectiveInvertY)delta.y=-delta.y;
                }
            }
            InputSystem.QueueStateEvent(mouse,new MouseState{delta=delta,buttons=(ushort)(release?0:(Fire?1:0)|(Vacuum?2:0))});
            var keys=new List<Key>(8);if(!release&&Forward)keys.Add(Key.W);if(!release&&Backward)keys.Add(Key.S);if(!release&&Left)keys.Add(Key.A);if(!release&&Right)keys.Add(Key.D);
            if(!release&&Interact)keys.Add(Key.E);if(!release&&Jump)keys.Add(Key.Space);if(!release&&Sprint)keys.Add(Key.LeftShift);
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(keys.ToArray()));QueuedDynamicFrames++;
            bool neutral=release||!(Forward||Backward||Left||Right||Vacuum||Interact||Jump||Sprint||Fire);
            ConsecutiveNeutralDynamicFrames=neutral?ConsecutiveNeutralDynamicFrames+1:0;
        }
        private void RestoreReader()
        {
            Neutral();
            if(actions!=null)actions.devices=originalDevices;
            if(reader!=null){witness?.Invoke("restore actual OS focus="+Application.isFocused);reader.SendMessage("OnApplicationFocus",Application.isFocused,SendMessageOptions.RequireReceiver);}
            actions=null;moveAction=vacuumAction=interactAction=fireAction=null;reader=null;motor=null;
        }
        public void Dispose()
        {
            if(disposed)return;disposed=true;InputSystem.onBeforeUpdate-=Pump;RestoreReader();
            if(mouse!=null&&mouse.added)InputSystem.RemoveDevice(mouse);
            if(keyboard!=null&&keyboard.added)InputSystem.RemoveDevice(keyboard);
            if(oldMouse!=null&&oldMouse.added)oldMouse.MakeCurrent();if(oldKeyboard!=null&&oldKeyboard.added)oldKeyboard.MakeCurrent();
            InputSystem.settings=original;UnityEngine.Object.Destroy(temporary);
        }
    }
}
#endif
