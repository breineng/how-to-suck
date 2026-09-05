using UnityEngine;

namespace HowToSuck
{
    [DisallowMultipleComponent]
    public sealed class PlayerView : MonoBehaviour
    {
        public PlayerMotor Motor;
        public PlayerInputReader Input;
        public Camera Camera;
        public Transform ViewModelRoot;
        public bool IsLocal = true;

        private PlayerInputReader subscribedInput;
        private AudioListener audioListener;

        private void Start() => Initialize(IsLocal);

        public void Initialize(bool isLocal = true)
        {
            IsLocal = isLocal;
            Unsubscribe();
            if (Input != null && IsLocal)
            {
                subscribedInput = Input;
                subscribedInput.MenuChanged += HandleMenuChanged;
            }
            if (Camera != null)
            {
                Camera.enabled = IsLocal;
                audioListener = Camera.GetComponent<AudioListener>();
                if (audioListener != null) audioListener.enabled = IsLocal;
                if (Motor != null && Motor.Settings != null)
                    Camera.fieldOfView = Motor.Settings.FieldOfView;
            }
            if (ViewModelRoot != null) ViewModelRoot.gameObject.SetActive(IsLocal);
            ApplyCursor();
        }

        private void LateUpdate()
        {
            if (!IsLocal || Motor == null || Input == null || Motor.CameraPivot == null) return;
            PlayerIntent intent = Input.LatestIntent;
            if (intent.IsFinite)
                Motor.CameraPivot.rotation = Quaternion.Euler(intent.Pitch, intent.Yaw, 0f);
        }

        private void HandleMenuChanged(bool open) => ApplyCursor();
        private void OnApplicationFocus(bool focused) => ApplyCursor();

        private void ApplyCursor()
        {
            if (!IsLocal) return;
            bool capture = isActiveAndEnabled && Application.isFocused && Input != null && !Input.MenuOpen;
            Cursor.lockState = capture ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !capture;
        }

        private void OnEnable()
        {
            if (subscribedInput == null && Input != null) Initialize(IsLocal);
        }

        private void OnDisable()
        {
            Unsubscribe();
            if (Camera != null) Camera.enabled = false;
            if (audioListener != null) audioListener.enabled = false;
            if (IsLocal)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        private void Unsubscribe()
        {
            if (subscribedInput != null) subscribedInput.MenuChanged -= HandleMenuChanged;
            subscribedInput = null;
        }

        private void OnDestroy() => Unsubscribe();
    }
}
