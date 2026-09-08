using System;
using System.Collections.Generic;
using UnityEngine;
namespace HowToSuck
{
    // Captured while Available, after LootRegistry.Register and BEFORE any intake deformation.
    // Source colliders stay on the original object, including while disabled in storage.
    public sealed class ItemLaunchGeometry
    {
        private const float Clearance = .005f;
        private readonly SuckableObject item;
        private readonly LootKey key;
        private readonly Shape[] shapes;
        internal bool SupportsSweptCcd
        {
            get
            {
                foreach (var shape in shapes) if (shape.Mesh != null) return false;
                return true;
            }
        }
        private sealed class Shape
        {
            internal Collider Collider;
            internal Vector3 Position, Centre, Scale, Size;
            internal Quaternion Rotation;
            internal float Radius, Height;
            internal int Direction;
            internal Mesh Mesh;
            internal Bounds MeshBounds;
        }
        private ItemLaunchGeometry(SuckableObject source, Shape[] captured) { item = source; key = source.Key; shapes = captured; }
        public static bool TryCapture(SuckableObject item, out ItemLaunchGeometry geometry, out string error)
        {
            geometry = null; error = null;
            if (item == null || !item.isActiveAndEnabled || !item.HasPhysicsAuthority || !item.Key.IsValid || item.State != SuckableState.Available || item.Body == null)
            { error = "Capture requires the initialized Available authority item."; return false; }
            var result = new List<Shape>();
            foreach (Collider c in item.GameplayColliders)
            {
                if (c == null || !c.enabled || c.isTrigger) continue;
                if (!c.gameObject.activeInHierarchy || c.GetComponentInParent<Rigidbody>(true) != item.Body || !ValidFrame(c.transform, out Vector3 scale))
                { error = "Inactive, foreign, reflected or sheared collider frame: " + c.name; return false; }
                var s = new Shape { Collider = c, Position = item.transform.InverseTransformPoint(c.transform.position),
                    Rotation = Quaternion.Inverse(item.transform.rotation) * c.transform.rotation, Scale = scale };
                if (c is BoxCollider box) { s.Centre = box.center; s.Size = box.size; }
                else if (c is SphereCollider sphere) { s.Centre = sphere.center; s.Radius = sphere.radius; }
                else if (c is CapsuleCollider capsule) { s.Centre = capsule.center; s.Radius = capsule.radius; s.Height = capsule.height; s.Direction = capsule.direction; }
                else if (c is MeshCollider mesh && mesh.convex && mesh.sharedMesh != null)
                { s.Mesh = mesh.sharedMesh; s.MeshBounds = s.Mesh.bounds; s.Centre = s.MeshBounds.center; s.Size = s.MeshBounds.size; }
                else { error = "Unsupported solid collider: " + c.name; return false; }
                if (!Finite(s.Centre) || !Finite(s.Size) || !Finite(s.Radius) || !Finite(s.Height) ||
                    (c is BoxCollider || s.Mesh != null) && (s.Size.x <= 0 || s.Size.y <= 0 || s.Size.z <= 0) ||
                    (c is SphereCollider || c is CapsuleCollider) && s.Radius <= 0 || c is CapsuleCollider && (s.Height <= 0 || s.Direction < 0 || s.Direction > 2))
                { error = "Non-finite or empty collider geometry: " + c.name; return false; }
                result.Add(s);
            }
            if (result.Count == 0) { error = "No enabled authored solid collider was captured."; return false; }
            geometry = new ItemLaunchGeometry(item, result.ToArray()); return true;
        }
        public bool TryFindSafePose(IReadOnlyList<Pose> callerCandidates, out Pose pose, out string error)
        {
            pose = default; error = "No safe caller-supplied pose.";
            if (!Matches(out error)) return false;
            if (callerCandidates == null || callerCandidates.Count == 0 || callerCandidates.Count > 32)
            { error = "Supply 1..32 bounded world-pose candidates."; return false; }
            foreach (var candidate in callerCandidates)
                if (ClearAt(candidate, out error)) { pose = candidate; return true; }
            return false;
        }
        public bool TryLaunchPose(Transform nozzle, Transform shooterRoot, float minimumFrontDistance, out Pose pose, out string error)
        {
            pose = default; error = null;
            if (!Matches(out error)) return false;
            if (nozzle == null || shooterRoot == null || !Finite(nozzle.position) || !Unit(nozzle.rotation) || !Finite(minimumFrontDistance) || minimumFrontDistance < .025f)
            { error = "Invalid physical nozzle/shooter."; return false; }
            Vector3 forward = nozzle.forward;
            Quaternion rotation = item.Body.rotation;
            if (!Unit(rotation)) { error = "Invalid item root rotation."; return false; }
            var origin = new Pose(nozzle.position, rotation);
            float minimumProjection = float.PositiveInfinity;
            foreach (var s in shapes)
            {
                Bounds aabb = At(s, origin);
                float near = Vector3.Dot(aabb.center - origin.position, forward) - Vector3.Dot(aabb.extents, Abs(forward));
                minimumProjection = Mathf.Min(minimumProjection, near);
            }
            // Entire authored body is beyond the mouth plane. Reject, never warp through a wall to achieve this.
            float distance = Mathf.Max(0, -minimumProjection) + minimumFrontDistance;
            var candidate = new Pose(origin.position + forward * distance, rotation);
            if (!ClearAt(candidate, out error) || !ClearPath(origin, candidate, shooterRoot, out error)) return false;
            pose = candidate; return true;
        }
        private bool ClearAt(Pose pose, out string error)
        {
            error = null;
            if (!Finite(pose.position) || !Unit(pose.rotation)) { error = "Invalid candidate pose."; return false; }
            foreach (var s in shapes)
            {
                Bounds b = At(s, pose);
                foreach (var other in Physics.OverlapBox(b.center, b.extents + Vector3.one * Clearance, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (Ignore(other, null)) continue;
                    // A disabled source Collider.bounds is deliberately NEVER read.
                    Vector3 colliderPosition = pose.position + pose.rotation * s.Position;
                    Quaternion colliderRotation = pose.rotation * s.Rotation;
                    if (Physics.ComputePenetration(s.Collider, colliderPosition, colliderRotation, other,
                        other.transform.position, other.transform.rotation, out _, out float distance) && distance > 0)
                    { error = "Endpoint blocked by " + Label(other); return false; }
                }
            }
            return true;
        }
        private bool ClearPath(Pose from, Pose to, Transform shooterRoot, out string error)
        {
            error = null;
            Vector3 delta = to.position - from.position;
            float distance = delta.magnitude;
            foreach (var s in shapes)
            {
                Bounds b = At(s, from);
                Vector3 extents = b.extents + Vector3.one * Clearance;
                // A cast may omit colliders overlapping its origin. Reject those explicitly.
                // The conservative AABB may refuse a tight clear opening; it cannot under-bound these shapes.
                foreach (var other in Physics.OverlapBox(b.center, extents, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
                    if (!Ignore(other, shooterRoot)) { error = "Launch origin envelope blocked by " + Label(other); return false; }
                if (distance <= 0) continue;
                foreach (var hit in Physics.BoxCastAll(b.center, extents, delta / distance, Quaternion.identity, distance, ~0, QueryTriggerInteraction.Ignore))
                    if (!Ignore(hit.collider, shooterRoot)) { error = "Launch path blocked by " + Label(hit.collider); return false; }
            }
            // Shooter is excluded only from the starting path, never from ClearAt. No IgnoreCollision pair is installed.
            return true;
        }
        private bool Ignore(Collider other, Transform shooterRoot) => other == null || other.isTrigger || other.attachedRigidbody == item.Body ||
            other.transform == item.transform || other.transform.IsChildOf(item.transform) ||
            shooterRoot != null && (other.transform == shooterRoot || other.transform.IsChildOf(shooterRoot));
        private static string Label(Collider c) => c.name + " [" + c.GetType().Name + "/" + c.GetInstanceID() + "]";
        private bool Matches(out string error)
        {
            error = null;
            if (item == null || !item.HasPhysicsAuthority || !item.Key.Equals(key) || item.Body == null ||
                !Approximately(item.transform.lossyScale, Vector3.one)) { error = "Item identity/root changed."; return false; }
            foreach (var s in shapes)
            {
                Collider c = s.Collider;
                if (c == null || c.isTrigger || !c.gameObject.activeInHierarchy || c.GetComponentInParent<Rigidbody>(true) != item.Body ||
                    !ValidFrame(c.transform, out var scale) || !Approximately(scale, s.Scale) ||
                    !Approximately(item.transform.InverseTransformPoint(c.transform.position), s.Position) ||
                    Mathf.Abs(Quaternion.Dot(Quaternion.Inverse(item.transform.rotation) * c.transform.rotation, s.Rotation)) < .999999f)
                { error = "Captured collider frame changed or was destroyed."; return false; }
                bool same = c is BoxCollider b && b.center == s.Centre && b.size == s.Size ||
                    c is SphereCollider q && q.center == s.Centre && q.radius == s.Radius ||
                    c is CapsuleCollider k && k.center == s.Centre && k.radius == s.Radius && k.height == s.Height && k.direction == s.Direction ||
                    c is MeshCollider m && m.convex && m.sharedMesh == s.Mesh && s.Mesh != null && s.Mesh.bounds == s.MeshBounds;
                if (!same) { error = "Authored collider dimensions changed."; return false; }
            }
            return true;
        }
        private static Bounds At(Shape s, Pose root)
        {
            Quaternion q = root.rotation * s.Rotation;
            Vector3 centre = root.position + root.rotation * s.Position + q * Vector3.Scale(s.Centre, s.Scale);
            Vector3 e;
            if (s.Collider is SphereCollider) e = Vector3.one * (s.Radius * Mathf.Max(s.Scale.x, s.Scale.y, s.Scale.z));
            else if (s.Collider is CapsuleCollider)
            {
                int d = s.Direction;
                float radius = s.Radius * Mathf.Max(s.Scale[(d+1)%3], s.Scale[(d+2)%3]);
                float segment = Mathf.Max(0, s.Height * s.Scale[d] * .5f - radius);
                Vector3 axis = d == 0 ? Vector3.right : d == 1 ? Vector3.up : Vector3.forward;
                e = Abs(q * axis) * segment + Vector3.one * radius;
            }
            else
            {
                Vector3 h = Vector3.Scale(s.Size, s.Scale) * .5f;
                e = Abs(q * Vector3.right) * h.x + Abs(q * Vector3.up) * h.y + Abs(q * Vector3.forward) * h.z;
            }
            return new Bounds(centre, e * 2f);
        }
        private static bool ValidFrame(Transform t, out Vector3 scale)
        {
            scale = t.lossyScale;
            if (!Finite(scale) || scale.x <= 0 || scale.y <= 0 || scale.z <= 0) return false;
            Matrix4x4 m = t.localToWorldMatrix;
            // TRS approximation is only safe when the actual basis agrees; reject negative/sheared hierarchies.
            return Approximately(m.MultiplyVector(Vector3.right), t.rotation * Vector3.right * scale.x) &&
                Approximately(m.MultiplyVector(Vector3.up), t.rotation * Vector3.up * scale.y) &&
                Approximately(m.MultiplyVector(Vector3.forward), t.rotation * Vector3.forward * scale.z);
        }
        private static bool Approximately(Vector3 a, Vector3 b) => (a-b).sqrMagnitude < 1e-8f;
        private static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
        private static bool Finite(float v) => ItemFireRules.Finite(v);
        private static bool Finite(Vector3 v) => Finite(v.x) && Finite(v.y) && Finite(v.z);
        private static bool Unit(Quaternion q) => Finite(q.x) && Finite(q.y) && Finite(q.z) && Finite(q.w) &&
            Math.Abs((double)q.x*q.x+(double)q.y*q.y+(double)q.z*q.z+(double)q.w*q.w-1) < .001;
    }
}
