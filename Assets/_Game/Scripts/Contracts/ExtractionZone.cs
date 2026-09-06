using System;
using System.Collections.Generic;
using UnityEngine;

namespace HowToSuck
{
    [DisallowMultipleComponent]
    public sealed class ExtractionZone : MonoBehaviour
    {
        public BoxCollider Area;
        private Collider[] overlaps = new Collider[32];
        private readonly HashSet<int> inside = new HashSet<int>();
        public bool Contains(int playerId) => inside.Contains(playerId);
        public void Clear() => inside.Clear();

        public bool TryValidate(out string error)
        {
            error = null;
            if (Area == null || !Area.isTrigger || !Area.enabled || !Area.gameObject.activeInHierarchy)
                error = "Extraction needs an enabled authored box trigger.";
            else
            {
                var scale = Area.transform.lossyScale;
                var size = Vector3.Scale(Area.size, scale);
                if (!FinitePositive(size.x) || !FinitePositive(size.y) || !FinitePositive(size.z))
                    error = "Extraction box must have finite positive dimensions and scale.";
            }
            return error == null;
        }

        // Fresh geometry each authority step: missed exits, teleports and duplicate body colliders are harmless.
        public void Refresh(IReadOnlyDictionary<int, PlayerMotor> roster)
        {
            inside.Clear();
            if (!isActiveAndEnabled || Area == null || !Area.enabled || !Area.gameObject.activeInHierarchy) return;
            Physics.SyncTransforms();
            Vector3 half = Vector3.Scale(Area.size, Area.transform.lossyScale) * .5f;
            Vector3 center = Area.transform.TransformPoint(Area.center);
            int count;
            while (true)
            {
                count = Physics.OverlapBoxNonAlloc(center, half, overlaps, Area.transform.rotation,
                    ~0, QueryTriggerInteraction.Collide);
                if (count < overlaps.Length) break;
                if (overlaps.Length >= 4096) throw new InvalidOperationException("Extraction overlap exceeds scene budget.");
                Array.Resize(ref overlaps, overlaps.Length * 2);
            }
            for (int i = 0; i < count; i++)
            {
                var collider = overlaps[i];
                if (collider == null) continue;
                var motor = collider.GetComponentInParent<PlayerMotor>();
                if (motor != null && motor.isActiveAndEnabled && roster.TryGetValue(motor.PlayerId, out var registered) && registered == motor)
                    inside.Add(motor.PlayerId);
            }
        }

        private static bool FinitePositive(float value) => value > 0 && !float.IsNaN(value) && !float.IsInfinity(value);
        private void OnDisable() => Clear();
    }
}