using System;
using System.Collections.Generic;
using UnityEngine;
using NVector3 = System.Numerics.Vector3;

namespace HowToSuck
{
    // Called exclusively from the registered authority motor's Step; no Update/collision writer.
    public sealed class PlayerContactResponse
    {
        public const float ContactPadding = .04f;
        private readonly ContactVelocityState state;
        private readonly float referencePlayerMass;
        private readonly Dictionary<ulong, Contact> strongest = new Dictionary<ulong, Contact>();
        private readonly List<Contact> ordered = new List<Contact>();
        private Collider[] nearby = new Collider[32];
        private string runId;
        private struct Contact
        {
            public ulong Id; public float Mass, Priority;
            public Vector3 SurfaceVelocity, Direction;
        }
        public Vector3 Velocity => World07Geometry.Unity(state.Velocity);
        public float AppliedDeltaSpeed => state.AppliedDeltaSpeed;
        public int LastDistinctBodies { get; private set; }

        public PlayerContactResponse(PlayerContactSettings settings = null)
        { settings = settings ?? new PlayerContactSettings(); state = new ContactVelocityState(settings); referencePlayerMass = settings.PlayerMass; }

        public Vector3 Step(AuthorityWorld world, PlayerMotor motor, CharacterController capsule,
            Vector3 desiredVelocity, float dt, double now)
        {
            if (world == null || !world.HasAuthority || !world.IsRunning || motor == null ||
                !motor.isActiveAndEnabled || capsule == null || !capsule.enabled ||
                !world.Players.TryGetValue(motor.PlayerId, out var registered) || registered != motor ||
                string.IsNullOrEmpty(world.RunId))
            { Reset(); return Vector3.zero; }
            if (runId != world.RunId) { Reset(); runId = world.RunId; }
            if (!World07Geometry.Finite(desiredVelocity) || !state.BeginStep(now,dt)) return Vector3.zero;
            strongest.Clear(); ordered.Clear(); LastDistinctBodies = 0;
            if (!World07Geometry.TryCapsule(capsule,motor.transform.position,ContactPadding,
                    out var bottom,out var top,out var radius)) return Velocity;
            int count;
            while (true)
            {
                count = Physics.OverlapCapsuleNonAlloc(bottom,top,radius,nearby,
                    LayerMask.GetMask("Items"),QueryTriggerInteraction.Ignore);
                if (count < nearby.Length) break;
                if (nearby.Length >= 8192)
                {
                    nearby = Physics.OverlapCapsule(bottom,top,radius,LayerMask.GetMask("Items"),
                        QueryTriggerInteraction.Ignore);
                    count = nearby.Length; break;
                }
                Array.Resize(ref nearby,nearby.Length*2);
            }
            Vector3 playerVelocity = desiredVelocity + Velocity;
            for (int i=0;i<count;i++)
            {
                var collider = nearby[i]; var body = collider != null ? collider.attachedRigidbody : null;
                if (body == null || body.isKinematic || !body.gameObject.activeInHierarchy) continue;
                var item = body.GetComponent<SuckableObject>();
                if (item == null || item.RunId != runId || item.InstanceId == 0 ||
                    item.State != SuckableState.Available || item.WorldFrozen ||
                    !world.Loot.Items.TryGetValue(item.InstanceId,out var owned) || owned != item) continue;
                // Alternate projections find the nearest surface to the capsule's central segment.
                Vector3 axis = World07Geometry.OnSegment(collider.bounds.center,bottom,top);
                Vector3 point = collider.ClosestPoint(axis);
                for (int pass=0;pass<3;pass++)
                { axis = World07Geometry.OnSegment(point,bottom,top); point = collider.ClosestPoint(axis); }
                Vector3 toward = axis-point;
                if (!World07Geometry.Finite(point) || toward.sqrMagnitude > radius*radius) continue;
                // ClosestPoint is the input point when inside a solid. Use that shape's centre
                // only for this overlap case; coincident ambiguous centres produce no impulse.
                if (toward.sqrMagnitude < 1e-10f) toward = axis-collider.bounds.center;
                if (toward.sqrMagnitude < 1e-10f || !World07Geometry.Finite(toward)) continue;
                toward.Normalize();
                if (Physics.Linecast(point,axis,LayerMask.GetMask("World"),QueryTriggerInteraction.Ignore)) continue;
                Vector3 surface = body.GetPointVelocity(point);
                if (!World07Geometry.Finite(surface)) continue;
                float approach = Mathf.Min(Vector3.Dot(surface,toward),
                    Vector3.Dot(surface-playerVelocity,toward));
                if (approach <= 0 || !WorldBoundsRules.Finite(body.mass) || body.mass <= 0) continue;
                float priority = approach * (body.mass/(body.mass+referencePlayerMass));
                var contact = new Contact { Id=item.InstanceId,Mass=body.mass,Priority=priority,
                    SurfaceVelocity=surface,Direction=toward };
                if (!strongest.TryGetValue(contact.Id,out var prior) || contact.Priority>prior.Priority ||
                    (contact.Priority==prior.Priority && Lexical(contact.Direction,prior.Direction)<0))
                    strongest[contact.Id] = contact;
            }
            foreach (var contact in strongest.Values) ordered.Add(contact);
            ordered.Sort((a,b) => { int score=b.Priority.CompareTo(a.Priority); return score!=0?score:a.Id.CompareTo(b.Id); });
            LastDistinctBodies = ordered.Count;
            foreach (var contact in ordered)
                state.TryContact(contact.Id,contact.Mass,World07Geometry.Numerics(contact.SurfaceVelocity),
                    World07Geometry.Numerics(contact.Direction),World07Geometry.Numerics(playerVelocity));
            return Velocity;
        }
        private static int Lexical(Vector3 a,Vector3 b)
        { int x=a.x.CompareTo(b.x);if(x!=0)return x;int y=a.y.CompareTo(b.y);return y!=0?y:a.z.CompareTo(b.z); }
        public void Reset()
        { state.Reset(); runId=null; strongest.Clear(); ordered.Clear(); LastDistinctBodies=0; }
    }

    internal static class World07Geometry
    {
        public static NVector3 Numerics(Vector3 p) => new NVector3(p.x,p.y,p.z);
        public static Vector3 Unity(NVector3 p) => new Vector3(p.X,p.Y,p.Z);
        public static bool Finite(Vector3 p) => WorldBoundsRules.Finite(Numerics(p));
        public static Vector3 OnSegment(Vector3 point,Vector3 a,Vector3 b)
        {
            Vector3 segment=b-a;float length=segment.sqrMagnitude;
            return length<1e-10f?a:a+segment*Mathf.Clamp01(Vector3.Dot(point-a,segment)/length);
        }
        public static bool TryCapsule(CharacterController c,Vector3 root,float padding,
            out Vector3 bottom,out Vector3 top,out float radius)
        {
            bottom=top=default;radius=0;
            if(c==null || !Finite(root) || !Finite(c.center) || !WorldBoundsRules.Finite(c.height) ||
                !WorldBoundsRules.Finite(c.radius) || c.radius<=0 || c.height<2*c.radius ||
                (c.transform.lossyScale-Vector3.one).sqrMagnitude>1e-6f ||
                Vector3.Dot(c.transform.up,Vector3.up)<.9999f) return false;
            Vector3 center=root+c.transform.TransformVector(c.center);
            float segment=Mathf.Max(0,c.height*.5f-c.radius);
            bottom=center-Vector3.up*segment;top=center+Vector3.up*segment;radius=c.radius+padding;
            return Finite(bottom)&&Finite(top)&&WorldBoundsRules.Finite(radius)&&radius>0;
        }
    }
}
