// STAGED PROPOSAL ONLY. Integrate explicitly in PlayerAnimationView's one pose lifecycle.
// No independent Update/LateUpdate; no additive offset may survive its RestorePose.
using System;
using UnityEngine;
namespace HowToSuck
{
    public static class WorkerHeadLookPresentation
    {
        // Call once after Animator sampling and BEFORE D changes chest or stabilizes head.
        // saveBoneOnce must deduplicate the main PlayerAnimationView cache.
        public static void CaptureBeforeTorso(Transform neckBone, Transform headBone, Action<Transform> saveBoneOnce)
        {
            ValidateBinding(neckBone, headBone);
            if (saveBoneOnce == null) throw new ArgumentNullException(nameof(saveBoneOnce));
            saveBoneOnce(neckBone);
            saveBoneOnce(headBone);
        }
        // Call once AFTER D torso/head stabilization and arm solve, even when a tool is absent.
        // renderedYawRight = the upright rendered VisualRoot.right, never camera.right in rolled views.
        public static void ApplyAfterTorso(Transform neckBone, Transform headBone, Vector3 renderedYawRight, float renderedPitch)
        {
            ValidateBinding(neckBone, headBone);
            float sq = renderedYawRight.sqrMagnitude;
            if (float.IsNaN(sq) || float.IsInfinity(sq) || sq < .999f || sq > 1.001f)
                throw new ArgumentException("Expected the normalized rendered player yaw right axis.", nameof(renderedYawRight));
            WorkerHeadLookPolicy.Evaluate(renderedPitch, out float neckAngle, out float headAngle);
            Vector3 axis = renderedYawRight.normalized;
            // The head first inherits neck rotation and displacement, then gets its own additional rotation.
            neckBone.rotation = Quaternion.AngleAxis(neckAngle, axis) * neckBone.rotation;
            headBone.rotation = Quaternion.AngleAxis(headAngle, axis) * headBone.rotation;
        }
        public static float PitchFromRenderedAim(Quaternion renderedAim)
        {
            Vector3 forward = renderedAim * Vector3.forward;
            return Mathf.Atan2(-forward.y, Mathf.Sqrt(forward.x * forward.x + forward.z * forward.z)) * Mathf.Rad2Deg;
        }
        private static void ValidateBinding(Transform neckBone, Transform headBone)
        {
            if (neckBone == null || headBone == null || neckBone.name != "neck" || headBone.name != "head" || headBone.parent != neckBone)
                throw new ArgumentException("Bind the actual D neck -> head bones, not the Head renderer or visual root.");
        }
    }
}
