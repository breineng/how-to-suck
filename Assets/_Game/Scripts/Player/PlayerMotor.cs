using UnityEngine;
using System.Collections.Generic;

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
        public PlayerContactSettings ContactSettings = new PlayerContactSettings();
        public bool UseC28Mk1AimProfile;
        [Range(0f,.6f)] public float MaximumAimMountDisplacement=.6f;
        public bool NozzlePoseValid { get; private set; } = true;

        // Read-only presentation invalidation; no simulation or networking decision uses it.
        public uint PresentationResetRevision { get; private set; }
        public int PlayerId { get; private set; }
        public PlayerIntent LastIntent { get; private set; }
        public bool IsGrounded => HasMovementAuthority ? controller != null && controller.isGrounded : replicaGrounded;
        public bool HasMovementAuthority { get; private set; } = true;
        private bool roleBound, replicaGrounded;
        public void BindMovementAuthority(bool authority)
        {
            if (roleBound && HasMovementAuthority != authority) throw new System.InvalidOperationException("A player cannot change authority in place.");
            HasMovementAuthority = authority; roleBound = true;
            controller = GetComponent<CharacterController>(); controller.enabled = authority;
        }
        public void ApplyReplicaPresentation(PlayerIntent intent, bool grounded, float vertical, float planarSpeed = 0f)
        {
            if (HasMovementAuthority || !initialized || !intent.IsFinite || float.IsNaN(vertical) || float.IsInfinity(vertical) || float.IsNaN(planarSpeed) || float.IsInfinity(planarSpeed) || planarSpeed < 0f) return;
            LastIntent = intent; replicaGrounded = grounded; verticalVelocity = vertical; PlanarSpeed = planarSpeed;
            NozzlePoseValid = TryGetNozzleLocalPosition(intent.Pitch, out var position);
            if (NozzlePoseValid) NozzleAnchor.localPosition = position;
            if (AuthoritativeAim != null) AuthoritativeAim.rotation = Quaternion.Euler(intent.Pitch, intent.Yaw, 0);
            // NetworkTransform owns root pose; no CharacterController.Move, forces or recovery here.
        }
        public float VerticalVelocity => verticalVelocity;
        // Measured by the sole authoritative Move, using that exact physics step's duration.
        // Replicas receive this presentation value; an interpolated render root is never divided by fixed time.
        public float PlanarSpeed { get; private set; }
        public Vector3 CameraLocalMount => Settings!=null?new Vector3(0,Settings.EyeHeight,Settings.CameraForwardOffset):new Vector3(0,1.62f,0);

        private CharacterController controller;
        private float verticalVelocity;
        private uint consumedJumpSequence;
        private bool initialized;
        private Vector3 previousRenderPosition,currentRenderPosition;
        private double renderPoseFixedTime;
        private readonly HashSet<Rigidbody> pushedBodies=new HashSet<Rigidbody>();
        private Vector3 pushVelocity;
        private float pushImpulseBudget;
        private bool moving;
        private AuthorityWorld contactWorld;
        private PlayerContactResponse contactResponse;
        public Vector3 ExternalVelocity => contactResponse != null ? contactResponse.Velocity : Vector3.zero;

        private void OnDisable() {PlanarSpeed=0f;unchecked{PresentationResetRevision++;}}

        public void BindContactWorld(AuthorityWorld world)
        { contactWorld = world; ResetContactResponse(); }
        public void ResetContactResponse() => contactResponse?.Reset();

        public void SuspendForRecovery(PlayerIntent current)
        {
            unchecked{PresentationResetRevision++;}
            if (current.IsFinite)
            {
                if (PlayerIntent.IsNewer(current.JumpPressSequence, consumedJumpSequence))
                    consumedJumpSequence = current.JumpPressSequence;
                LastIntent = current.Neutral();
            }
            else LastIntent = LastIntent.Neutral();
            verticalVelocity = 0; PlanarSpeed = 0; moving = false; pushImpulseBudget = 0; pushedBodies.Clear();
            ResetContactResponse();
            previousRenderPosition = currentRenderPosition = transform.position;
            renderPoseFixedTime = Time.fixedTimeAsDouble;
        }

        public bool RecoverAt(Vector3 position, PlayerIntent current)
        {
            if (!initialized || controller == null || !controller.enabled || !gameObject.activeInHierarchy ||
                contactWorld == null || !contactWorld.HasAuthority || !contactWorld.IsRunning ||
                !contactWorld.Players.TryGetValue(PlayerId, out var registered) || registered != this ||
                current.RunId != contactWorld.RunId || !current.IsFinite ||
                !WorldBoundsRules.Finite(position.x) || !WorldBoundsRules.Finite(position.y) ||
                !WorldBoundsRules.Finite(position.z)) return false;
            controller.enabled = false;
            try { transform.position = position; }
            finally { controller.enabled = true; }
            SuspendForRecovery(current);
            return true;
        }

        public void Initialize(int playerId)
        {
            unchecked{PresentationResetRevision++;}
            PlayerId = playerId;
            contactResponse = new PlayerContactResponse(ContactSettings ?? new PlayerContactSettings());
            controller = GetComponent<CharacterController>();
            if (Settings == null) Settings = new PlayerSettings();
            controller.height = Mathf.Max(Settings.CapsuleHeight, Settings.CapsuleRadius * 2f);
            controller.radius = Mathf.Max(0.01f, Settings.CapsuleRadius);
            // Native CharacterController grounding stops one skin width above its geometric bottom.
            // Keep the shared body/eye/nozzle root at the floor, without shifting the visual rig.
            controller.center = Vector3.up * (controller.height * 0.5f + controller.skinWidth);
            controller.stepOffset = Mathf.Clamp(Settings.StepOffset, 0f, controller.height);
            controller.slopeLimit = Settings.SlopeLimit;
            controller.minMoveDistance = 0f;
            if (CameraPivot != null) CameraPivot.localPosition = CameraLocalMount;
            if (AuthoritativeAim != null) AuthoritativeAim.localPosition = Vector3.up * Settings.EyeHeight;
            verticalVelocity = 0f; PlanarSpeed = 0f;
            if (!initialized) consumedJumpSequence = 0;
            LastIntent = new PlayerIntent { Yaw = transform.eulerAngles.y, JumpPressSequence = consumedJumpSequence };
            previousRenderPosition=currentRenderPosition=transform.position;renderPoseFixedTime=Time.fixedTimeAsDouble;
            initialized = true;
        }

        public bool TryGetNozzleLocalPosition(float pitch, out Vector3 position)
        {
            position = NozzleAnchor != null ? NozzleAnchor.localPosition : Vector3.zero;
            if (!UseC28Mk1AimProfile) return NozzleAnchor != null;
            var emitter = GetComponent<VacuumEmitter>();
            if (emitter != null && emitter.Definition != null && emitter.Definition.TierId!="mk1")
            {
                if (Settings == null || Mathf.Abs(Settings.EyeHeight-1.62f)>.0001f ||
                    (transform.lossyScale-Vector3.one).sqrMagnitude>.000001f) return false;
                var issued=GetComponent<ToolTierIssue>();
                return issued!=null && issued.ActiveProfile!=null &&
                    !float.IsNaN(MaximumAimMountDisplacement) && !float.IsInfinity(MaximumAimMountDisplacement) &&
                    MaximumAimMountDisplacement>0 && MaximumAimMountDisplacement<=.6f &&
                    issued.ActiveProfile.MaximumDisplacement<=MaximumAimMountDisplacement &&
                    issued.TryMount(emitter.Definition.TierId,pitch,out position);
            }
            if (Settings == null || Mathf.Abs(Settings.EyeHeight-1.62f)>.0001f ||
                (transform.lossyScale-Vector3.one).sqrMagnitude>.000001f ||
                (emitter != null && emitter.Definition != null && emitter.Definition.TierId!="mk1")) return false;
            if (float.IsNaN(MaximumAimMountDisplacement) || float.IsInfinity(MaximumAimMountDisplacement) || MaximumAimMountDisplacement<=0 || MaximumAimMountDisplacement>.6f) return false;
            var result = NozzleAimMountPolicy.Evaluate(pitch,MaximumAimMountDisplacement);
            if (!result.Reachable) return false;
            position = new Vector3((float)result.X,(float)result.Y,(float)result.Z);
            var stance=GetComponent<PlayerIdleStanceView>();if(stance!=null)position+=stance.Frame(pitch).AdditionalMountAimOffset;
            return true;
        }

        public Vector3 GetRenderPosition()
        {
            if(!initialized || (transform.position-currentRenderPosition).sqrMagnitude>.000001f)return transform.position;
            if(Time.fixedTimeAsDouble>renderPoseFixedTime+.000001)return currentRenderPosition;
            float alpha=Mathf.Clamp01((float)((Time.timeAsDouble-Time.fixedTimeAsDouble)/Time.fixedDeltaTime));
            return Vector3.Lerp(previousRenderPosition,currentRenderPosition,alpha);
        }
        private void OnControllerColliderHit(ControllerColliderHit hit)
        {
            // CharacterController does not push rigidbodies itself. Nudge side contacts through physics.
            if(!HasMovementAuthority || !moving || Mathf.Abs(hit.normal.y)>.5f || pushVelocity.sqrMagnitude<.001f)return;
            var body=hit.rigidbody;if(body==null || body.isKinematic)return;
            var item=body.GetComponent<SuckableObject>();
            if(item==null || item.InstanceId==0 || item.State!=SuckableState.Available || item.WorldFrozen || !pushedBodies.Add(body))return;
            Vector3 direction=pushVelocity.normalized;
            float gap=Mathf.Max(0,Vector3.Dot(pushVelocity-body.linearVelocity,direction));
            float impulse=Mathf.Min(body.mass*gap,pushImpulseBudget);
            pushImpulseBudget-=impulse;
            body.AddForceAtPosition(direction*impulse,hit.point,ForceMode.Impulse);
        }

        public void Step(PlayerIntent intent, float dt, double? authorityNow = null)
        {
            if (!HasMovementAuthority || !initialized || !controller.enabled || !gameObject.activeInHierarchy || !intent.IsFinite ||
                float.IsNaN(dt) || float.IsInfinity(dt) || dt <= 0f) return;

            previousRenderPosition=(transform.position-currentRenderPosition).sqrMagnitude>.000001f?transform.position:currentRenderPosition;
            intent.Move = Vector2.ClampMagnitude(intent.Move, 1f);
            intent.Yaw = Mathf.Repeat(intent.Yaw, 360f);
            intent.Pitch = Mathf.Clamp(intent.Pitch, -80f, 80f);
            LastIntent = intent;
            NozzlePoseValid = TryGetNozzleLocalPosition(intent.Pitch, out var nozzlePosition);
            if (NozzlePoseValid) NozzleAnchor.localPosition = nozzlePosition;

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
            pushedBodies.Clear();pushVelocity=horizontal*speed;pushImpulseBudget=125f*dt;moving=true;
            Vector3 desiredVelocity = horizontal * speed + Vector3.up * verticalVelocity;
            Vector3 externalVelocity = contactResponse != null ?
                contactResponse.Step(contactWorld, this, controller, desiredVelocity, dt,
                    authorityNow ?? Time.realtimeSinceStartupAsDouble) : Vector3.zero;
            Vector3 stepStart = transform.position;
            CollisionFlags collisions;
            try{collisions = controller.Move((desiredVelocity + externalVelocity) * dt);}
            finally{moving=false;currentRenderPosition=transform.position;renderPoseFixedTime=Time.fixedTimeAsDouble;}
            var travelled = transform.position - stepStart; travelled.y = 0f;
            PlanarSpeed = travelled.magnitude / dt;
            if ((collisions & CollisionFlags.Above) != 0 && verticalVelocity > 0f) verticalVelocity = 0f;
            if ((collisions & CollisionFlags.Below) != 0 && verticalVelocity < 0f) verticalVelocity = -2f;
        }
    }
}
