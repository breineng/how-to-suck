using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace HowToSuck
{
    [DisallowMultipleComponent]
    public sealed class PlayerInputReader : MonoBehaviour
    {
        public InputActionAsset Actions;
        [Min(0f)] public float MouseSensitivity = 0.12f;
        public bool MenuOpen { get; private set; }
        public bool IsInitialized => initialized;
        public bool GameplayAvailable { get; private set; } = true;
        public PlayerIntent LatestIntent { get; private set; }
        public event Action<bool> MenuChanged;

        private LocalSettingsController localSettings;
        public float EffectiveMouseSensitivity=>localSettings!=null&&localSettings.isActiveAndEnabled?localSettings.Sensitivity:MouseSensitivity;
        public bool EffectiveInvertY=>localSettings!=null&&localSettings.isActiveAndEnabled&&localSettings.InvertY;
        public void BindLocalSettings(LocalSettingsController owner)
        {
            if(owner!=null&&(owner.Session==null||owner.Session.LocalPlayer!=GetComponent<PlayerMotor>()))
                throw new InvalidOperationException("Personal input settings may only bind the actual local player.");
            localSettings=owner;discardLook=true;
        }
        private UnityEngine.Object localModalOwner;
        private int modalReleasedFrame=-1;
        public bool LocalModalOpen=>localModalOwner!=null;
        public string GameplayBindingDisplay(string actionName)=>(localActions!=null?localActions:Actions)?.FindAction("Gameplay/"+actionName)?.GetBindingDisplayString()??"—";
        public void SetLocalModal(UnityEngine.Object owner,bool open)
        {
            if(owner==null)throw new ArgumentNullException(nameof(owner));
            if(open){if(localModalOwner!=null&&localModalOwner!=owner)throw new InvalidOperationException("A personal menu is already open.");localModalOwner=owner;SetMenuOpen(true);}
            else{if(localModalOwner!=owner)return;localModalOwner=null;modalReleasedFrame=Time.frameCount;}
            // Closing the child screen leaves the pause menu open and requires release of its UI click.
            suppressVacuum=suppressInteract=suppressJump=true;discardLook=true;SendNeutral();ApplyActionState();
        }
        private InputActionAsset localActions;
        private InputAction move, look, sprint, jump, vacuum, interact, pause;
        private IPlayerIntentSink sink;
        private int playerId;
        private bool initialized;
        private bool focused;
        private bool suppressVacuum, suppressInteract, suppressJump;
        private bool discardLook;

        // Two-argument overload is retained for isolated input fixtures; live sessions bind a non-empty run.
        public void Initialize(int id, IPlayerIntentSink intentSink) => Initialize(id, intentSink, null);

        public void Initialize(int id, IPlayerIntentSink intentSink, string runId)
        {
            if (intentSink == null) throw new ArgumentNullException(nameof(intentSink));
            if (initialized) SendNeutral();
            DisposeActions();
            playerId = id;
            sink = intentSink;
            // A live reader must keep its counters across rebinding; the authority may still remember them.
            LatestIntent = new PlayerIntent { RunId = runId, Sequence = LatestIntent.Sequence, JumpPressSequence = LatestIntent.JumpPressSequence, Yaw = transform.eulerAngles.y };
            MenuOpen = false;
            GameplayAvailable = true;
            focused = Application.isFocused;
            if (Actions == null)
            {
                Debug.LogError("PlayerInputReader needs HowToSuck.inputactions.", this);
                initialized = false;
                return;
            }

            localActions = Instantiate(Actions);
            try
            {
                move = localActions.FindAction("Gameplay/Move", true);
                look = localActions.FindAction("Gameplay/Look", true);
                sprint = localActions.FindAction("Gameplay/Sprint", true);
                jump = localActions.FindAction("Gameplay/Jump", true);
                vacuum = localActions.FindAction("Gameplay/Vacuum", true);
                interact = localActions.FindAction("Gameplay/Interact", true);
                pause = localActions.FindAction("Gameplay/Pause", true);
            }
            catch (ArgumentException exception)
            {
                Debug.LogError("Player input action setup is incomplete: " + exception.Message, this);
                DisposeActions();
                initialized = false;
                return;
            }

            initialized = true;
            suppressVacuum = suppressInteract = suppressJump = true;
            discardLook = true;
            ApplyActionState();
            SendNeutral();
        }

        public void SetGameplayAvailable(bool available)
        {
            GameplayAvailable = available;
            if (!available) SetMenuOpen(true);
            suppressVacuum = suppressInteract = suppressJump = true;
            discardLook = true;
            SendNeutral();
            ApplyActionState();
        }

        public void SetMenuOpen(bool open)
        {
            if (MenuOpen == open || (!open && (!GameplayAvailable || LocalModalOpen))) return;
            MenuOpen = open;
            suppressVacuum = suppressInteract = suppressJump = true;
            discardLook = true;
            SendNeutral();
            ApplyActionState();
            MenuChanged?.Invoke(open);
        }

        private void Update()
        {
            if (!initialized) return;
            if (!LocalModalOpen && modalReleasedFrame!=Time.frameCount && GameplayAvailable && focused && pause.WasPressedThisFrame()) SetMenuOpen(!MenuOpen);
            if (!GameplayAvailable || MenuOpen || LocalModalOpen || !focused)
            {
                SendNeutral();
                return;
            }

            // Physical controls remain queryable during the first frame after actions are enabled.
            // A click used on UI must be released before it can become suction.
            if (suppressVacuum && !AnyButtonHeld(vacuum)) suppressVacuum = false;
            if (suppressInteract && !AnyButtonHeld(interact)) suppressInteract = false;
            if (suppressJump && !AnyButtonHeld(jump)) suppressJump = false;

            Vector2 delta = discardLook ? Vector2.zero : look.ReadValue<Vector2>();
            discardLook = false;
            PlayerIntent previous = LatestIntent;
            var intent = new PlayerIntent
            {
                RunId = previous.RunId,
                Sequence = unchecked(previous.Sequence + 1),
                JumpPressSequence = previous.JumpPressSequence,
                Move = Vector2.ClampMagnitude(move.ReadValue<Vector2>(), 1f),
                SprintHeld = sprint.IsPressed(),
                VacuumHeld = !suppressVacuum && vacuum.IsPressed(),
                InteractHeld = !suppressInteract && interact.IsPressed(),
                Yaw = Mathf.Repeat(previous.Yaw + delta.x * EffectiveMouseSensitivity, 360f),
                Pitch = Mathf.Clamp(previous.Pitch + LocalSettingsMath.PitchDelta(delta.y,EffectiveMouseSensitivity,EffectiveInvertY), -80f, 80f)
            };
            if (!suppressJump && jump.WasPressedThisFrame())
                intent.JumpPressSequence = unchecked(intent.JumpPressSequence + 1);
            LatestIntent = intent;
            sink.SubmitIntent(playerId, intent);
        }

        private static bool AnyButtonHeld(InputAction action)
        {
            foreach (InputControl control in action.controls)
                if (control is ButtonControl button && button.isPressed) return true;
            return false;
        }

        private void SendNeutral()
        {
            if (sink == null) return;
            PlayerIntent intent = LatestIntent.Neutral();
            intent.Sequence = unchecked(intent.Sequence + 1);
            LatestIntent = intent;
            sink.SubmitIntent(playerId, intent);
        }

        private void ApplyActionState()
        {
            if (!initialized) return;
            bool active = isActiveAndEnabled && focused && GameplayAvailable;
            SetEnabled(pause, active);
            bool gameplay = active && !MenuOpen && !LocalModalOpen;
            SetEnabled(move, gameplay);
            SetEnabled(look, gameplay);
            SetEnabled(sprint, gameplay);
            SetEnabled(jump, gameplay);
            SetEnabled(vacuum, gameplay);
            SetEnabled(interact, gameplay);
        }

        private static void SetEnabled(InputAction action, bool enable)
        {
            if (enable) action.Enable(); else action.Disable();
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            focused = hasFocus;
            suppressVacuum = suppressInteract = suppressJump = true;
            discardLook = true;
            SendNeutral();
            ApplyActionState();
        }

        private void OnEnable()
        {
            focused = Application.isFocused;
            suppressVacuum = suppressInteract = suppressJump = true;
            discardLook = true;
            ApplyActionState();
        }

        private void OnDisable()
        {
            SendNeutral();
            if (localActions != null) localActions.Disable();
        }

        private void DisposeActions()
        {
            if (localActions == null) return;
            localActions.Disable();
            Destroy(localActions);
            localActions = null;
        }

        private void OnDestroy()
        {
            DisposeActions();
            sink = null;
        }
    }
}
