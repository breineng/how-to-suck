using UnityEngine;

namespace HowToSuck
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerMotor : MonoBehaviour
    {
        public Transform CameraPivot;
        public Transform AuthoritativeAim;
        public Transform NozzleAnchor;
        public PlayerSettings Settings = new PlayerSettings();

        public int PlayerId { get; private set; }
        public PlayerIntent LastIntent { get; private set; }
        public bool IsGrounded => controller != null && controller.isGrounded;
        public float VerticalVelocity => verticalVelocity;

        private CharacterController controller;
        private float verticalVelocity;
        private uint consumedJumpSequence;
        private bool initialized;

        public void Initialize(int playerId)
        {
            PlayerId = playerId;
            controller = GetComponent<CharacterController>();
            if (Settings == null) Settings = new PlayerSettings();
            controller.height = Mathf.Max(Settings.CapsuleHeight, Settings.CapsuleRadius * 2f);
            controller.radius = Mathf.Max(0.01f, Settings.CapsuleRadius);
            controller.center = Vector3.up * (controller.height * 0.5f);
            controller.stepOffset = Mathf.Clamp(Settings.StepOffset, 0f, controller.height);
            controller.slopeLimit = Settings.SlopeLimit;
            controller.minMoveDistance = 0f;
            if (CameraPivot != null) CameraPivot.localPosition = Vector3.up * Settings.EyeHeight;
            if (AuthoritativeAim != null) AuthoritativeAim.localPosition = Vector3.up * Settings.EyeHeight;
            verticalVelocity = 0f;
            consumedJumpSequence = 0;
            LastIntent = new PlayerIntent { Yaw = transform.eulerAngles.y };
            initialized = true;
        }

        public void Step(PlayerIntent intent, float dt)
        {
            if (!initialized || !controller.enabled || !gameObject.activeInHierarchy || !intent.IsFinite ||
                float.IsNaN(dt) || float.IsInfinity(dt) || dt <= 0f) return;

            intent.Move = Vector2.ClampMagnitude(intent.Move, 1f);
            intent.Yaw = Mathf.Repeat(intent.Yaw, 360f);
            intent.Pitch = Mathf.Clamp(intent.Pitch, -80f, 80f);
            LastIntent = intent;

            // This is the sole gameplay pose writer. The local camera has a separate pivot.
            transform.rotation = Quaternion.Euler(0f, intent.Yaw, 0f);
            if (AuthoritativeAim != null)
                AuthoritativeAim.rotation = Quaternion.Euler(intent.Pitch, intent.Yaw, 0f);

            bool grounded = controller.isGrounded;
            if (grounded && verticalVelocity < 0f) verticalVelocity = -2f;
            if (PlayerIntent.IsNewer(intent.JumpPressSequence, consumedJumpSequence))
            {
                // An airborne edge is consumed too: landing must not replay an old press.
                consumedJumpSequence = intent.JumpPressSequence;
                if (grounded && !intent.SuppressJump)
                    verticalVelocity = Mathf.Sqrt(2f * Mathf.Abs(Settings.Gravity) * Mathf.Max(0f, Settings.JumpHeight));
            }

            verticalVelocity += Mathf.Min(-0.01f, Settings.Gravity) * dt;
            float speed = intent.SprintHeld ? Settings.SprintSpeed : Settings.WalkSpeed;
            Vector3 horizontal = transform.right * intent.Move.x + transform.forward * intent.Move.y;
            CollisionFlags collisions = controller.Move((horizontal * speed + Vector3.up * verticalVelocity) * dt);
            if ((collisions & CollisionFlags.Above) != 0 && verticalVelocity > 0f) verticalVelocity = 0f;
            if ((collisions & CollisionFlags.Below) != 0 && verticalVelocity < 0f) verticalVelocity = -2f;
        }
    }
}
