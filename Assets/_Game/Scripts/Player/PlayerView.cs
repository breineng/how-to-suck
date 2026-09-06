using System.Collections.Generic;
using UnityEngine;

namespace HowToSuck
{
    [DefaultExecutionOrder(1000)]
    [DisallowMultipleComponent]
    public sealed class PlayerView : MonoBehaviour
    {
        public PlayerMotor Motor;
        public PlayerInputReader Input;
        public Camera Camera;
        public Transform ViewModelRoot;
        public bool IsLocal = true;

        private sealed class ModelMount
        {
            public PlayerMotor Motor;
            public Vector3 Position, Scale, NozzlePosition;
            public Quaternion Rotation;
            public Transform Intake;
        }
        private readonly Dictionary<Transform, ModelMount> mounts = new Dictionary<Transform, ModelMount>();
        private readonly List<Transform> expiredMounts = new List<Transform>();
        private PlayerInputReader subscribedInput;
        private Camera boundCamera;
        private AudioListener audioListener;
        private Transform boundModel;
        private ModelMount mount;
        private IntakeReceiver publishedReceiver;
        private Transform publishedIntake;

        private void Start() => Initialize(IsLocal);

        public void Initialize(bool isLocal = true)
        {
            IsLocal = isLocal;
            Unsubscribe();
            if (Input != null && IsLocal && isActiveAndEnabled)
            {
                subscribedInput = Input;
                subscribedInput.MenuChanged += HandleMenuChanged;
            }
            BindCamera();
            BindLocalTool();
            ApplyCursor();
        }

        private void BindCamera()
        {
            if (boundCamera != Camera)
            {
                if (boundCamera != null) boundCamera.enabled = false;
                if (audioListener != null) audioListener.enabled = false;
                boundCamera = Camera;
                audioListener = boundCamera != null ? boundCamera.GetComponent<AudioListener>() : null;
            }
            bool active = IsLocal && isActiveAndEnabled;
            if (boundCamera == null) return;
            if (Motor != null && Motor.CameraPivot != null && boundCamera.transform != Motor.CameraPivot &&
                boundCamera.transform.parent != Motor.CameraPivot)
            {
                boundCamera.transform.SetParent(Motor.CameraPivot, false);
                boundCamera.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            }
            boundCamera.enabled = active;
            if (audioListener != null) audioListener.enabled = active;
            if (Motor != null && Motor.Settings != null) boundCamera.fieldOfView = Motor.Settings.FieldOfView;
        }

        private void LateUpdate()
        {
            BindCamera();
            BindLocalTool();
            if (!IsLocal || Motor == null || Motor.CameraPivot == null) return;
            if (Input != null)
            {
                PlayerIntent intent = Input.LatestIntent;
                if (intent.IsFinite) Motor.CameraPivot.rotation = Quaternion.Euler(intent.Pitch, intent.Yaw, 0f);
            }
            Vector3 renderedBase = Motor.GetRenderPosition();
            var yaw = Quaternion.Euler(0, Motor.CameraPivot.eulerAngles.y, 0);
            Motor.CameraPivot.position = renderedBase + yaw * Motor.CameraLocalMount;
            if (boundModel != null && mount != null)
            {
                // Moving the viewpoint does not move the tool's gameplay mount.
                Vector3 aimOrigin = renderedBase + Vector3.up * Motor.CameraLocalMount.y;
                float pitch = Input != null ? Input.LatestIntent.Pitch : Motor.LastIntent.Pitch;
                if (!Motor.TryGetNozzleLocalPosition(pitch,out var nozzle)) { boundModel.gameObject.SetActive(false); ReleasePresentation(); return; }
                Vector3 position = mount.Position + nozzle - mount.NozzlePosition;
                boundModel.SetPositionAndRotation(aimOrigin + Motor.CameraPivot.rotation * position,
                    Motor.CameraPivot.rotation * mount.Rotation);
            }
        }

        private void BindLocalTool()
        {
            if (Motor == null || Motor.AuthoritativeAim == null || Motor.CameraPivot == null)
            { ReleasePresentation(); return; }
            if (ViewModelRoot != boundModel || (mount != null && mount.Motor != Motor))
            {
                ReleasePresentation();
                if (boundModel != null) boundModel.gameObject.SetActive(false);
                boundModel = ViewModelRoot;
                mount = null;
                // Keep reusable tier mounts, discard destroyed instances without retaining their managed wrappers.
                expiredMounts.Clear();
                foreach (var pair in mounts) if (pair.Key == null) expiredMounts.Add(pair.Key);
                foreach (var key in expiredMounts) mounts.Remove(key);
                if (boundModel != null)
                {
                    if (!mounts.TryGetValue(boundModel, out mount) || mount.Motor != Motor)
                    {
                        if (mount != null && mount.Intake != null) Destroy(mount.Intake.gameObject);
                        // A new model arrives at its authored gameplay mount, before render reparenting.
                        mount = new ModelMount { Motor = Motor,
                            Position = Motor.AuthoritativeAim.InverseTransformPoint(boundModel.position),
                            Rotation = Quaternion.Inverse(Motor.AuthoritativeAim.rotation) * boundModel.rotation,
                            NozzlePosition = Motor.NozzleAnchor.localPosition,
                            Scale = boundModel.localScale };
                        var receiver = Motor.GetComponent<IntakeReceiver>();
                        if (receiver != null)
                        {
                            mount.Intake = new GameObject("RenderedIntake").transform;
                            mount.Intake.gameObject.hideFlags = HideFlags.DontSave;
                            mount.Intake.SetPositionAndRotation(receiver.Position, receiver.Rotation);
                            mount.Intake.SetParent(boundModel, true);
                        }
                        mounts[boundModel] = mount;
                    }
                }
            }
            bool active = IsLocal && isActiveAndEnabled;
            if (boundModel == null || mount == null) { ReleasePresentation(); return; }
            boundModel.gameObject.SetActive(active);
            if (!active) { ReleasePresentation(); return; }
            if (boundModel.parent != Motor.CameraPivot)
            {
                boundModel.SetParent(Motor.CameraPivot, false);
                boundModel.localScale = mount.Scale;
            }
            var currentReceiver = Motor.GetComponent<IntakeReceiver>();
            if (publishedReceiver != currentReceiver || publishedIntake != mount.Intake) ReleasePresentation();
            publishedReceiver = currentReceiver;
            publishedIntake = mount.Intake;
            if (publishedReceiver != null) publishedReceiver.PresentationTarget = publishedIntake;
        }

        private void ReleasePresentation()
        {
            if (publishedReceiver != null && publishedReceiver.PresentationTarget == publishedIntake)
                publishedReceiver.PresentationTarget = null;
            publishedReceiver = null;
            publishedIntake = null;
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
        private void OnEnable() => Initialize(IsLocal);
        private void OnDisable()
        {
            Unsubscribe(); ReleasePresentation();
            if (boundCamera != null) boundCamera.enabled = false;
            if (audioListener != null) audioListener.enabled = false;
            if (boundModel != null) boundModel.gameObject.SetActive(false);
            if (IsLocal) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
        }
        private void Unsubscribe()
        {
            if (subscribedInput != null) subscribedInput.MenuChanged -= HandleMenuChanged;
            subscribedInput = null;
        }
        private void OnDestroy()
        {
            Unsubscribe(); ReleasePresentation();
            foreach (var value in mounts.Values) if (value.Intake != null) Destroy(value.Intake.gameObject);
            mounts.Clear();
        }
    }
}