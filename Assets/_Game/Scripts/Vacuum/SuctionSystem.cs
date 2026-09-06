using System;
using System.Collections.Generic;
using UnityEngine;

namespace HowToSuck
{
    public readonly struct SuctionContact
    {
        public readonly SuckableObject Item;
        public readonly Vector3 Point;
        public readonly float Distance;
        public SuctionContact(SuckableObject item,Vector3 point,float distance) {Item=item;Point=point;Distance=distance;}
    }

    public sealed class SuctionSystem
    {
        private readonly LootRegistry registry;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // Value-only bounded journal: no observer callback is invoked from the force loop.
        public SuctionForceJournal DiagnosticAppliedForces { get; } = new SuctionForceJournal();
#endif
        public SuctionSystem(LootRegistry lootRegistry) {registry=lootRegistry ?? throw new ArgumentNullException(nameof(lootRegistry));}
        private readonly Dictionary<Rigidbody,float> appliedBrakes=new Dictionary<Rigidbody,float>();
        // Called once by the authority before all player and truck sources for this physics tick.
        public void BeginStep() {appliedBrakes.Clear();}
        private void ApplyBrake(Rigidbody body,float requested)
        {
            appliedBrakes.TryGetValue(body,out float applied);
            float total=Mathf.Min(applied+requested,body.mass*.95f/Time.fixedDeltaTime);
            if(total>applied)body.AddForce(-body.linearVelocity*(total-applied),ForceMode.Force);
            appliedBrakes[body]=total;
        }
        private Collider[] overlap=new Collider[128];
        private readonly HashSet<Rigidbody> seen=new HashSet<Rigidbody>();
        private readonly List<SuctionContact> contacts=new List<SuctionContact>(128);
        private readonly int itemMask=LayerMask.GetMask("Items");
        private readonly int blockerMask=LayerMask.GetMask("World","Items");
        public IReadOnlyList<SuctionContact> Collect(VacuumEmitter source)
        {
            contacts.Clear();seen.Clear();
            if(source==null || !source.Active || source.Definition==null || source.Range<=0 || !source.HasClearSourcePath()) return contacts;
            int count;
            while(true)
            {
                count=Physics.OverlapSphereNonAlloc(source.Position,source.Range,overlap,itemMask,QueryTriggerInteraction.Ignore);
                if(count<overlap.Length)break;
                Array.Resize(ref overlap,overlap.Length*2);
                if(overlap.Length>8192)throw new InvalidOperationException("Suction overlap exceeds supported scene budget.");
            }
            for(int i=0;i<count;i++)
            {
                var collider=overlap[i];var body=collider.attachedRigidbody;
                if(body==null || !seen.Add(body))continue;
                var item=body.GetComponent<SuckableObject>();
                if(item==null || item.InstanceId==0 || item.RunId!=registry.RunId || !registry.Items.TryGetValue(item.InstanceId,out var registered) || registered!=item || item.State!=SuckableState.Available || item.WorldFrozen)continue;
                if(TryFindSurface(source,item,out Vector3 point))
                    contacts.Add(new SuctionContact(item,point,Vector3.Distance(source.Position,point)));
            }
            contacts.Sort((a,b)=>a.Item.InstanceId.CompareTo(b.Item.InstanceId));
            return contacts;
        }
        public void Apply(VacuumEmitter source)
        {
            var found=Collect(source);source.LastAffectedCount=found.Count;source.LastLoad=0;
            foreach(var hit in found)
            {
                var body=hit.Item.Body;
                if(body==null || body.isKinematic)continue;
                Vector3 offset=hit.Point-source.Position;float distance=offset.magnitude;
                // Close-range physical drag prevents waiting light objects shooting past a busy mouth.
                float brake=Mathf.Min(2f*Mathf.Sqrt(source.Definition.Power*body.mass),body.mass*.95f/Time.fixedDeltaTime);
                if(distance<.025f){ApplyBrake(body,brake);source.LastLoad+=body.mass;continue;}
                float edge=Mathf.Cos(source.HalfAngle*Mathf.Deg2Rad);
                float dot=Vector3.Dot(source.Forward,offset/distance);
                float center=Mathf.InverseLerp(edge,1f,dot);
                float strength=source.Definition.Power*(.3f+.7f*(1f-distance/source.Range))*(.25f+.75f*center);
                body.maxLinearVelocity=25f;body.maxAngularVelocity=20f;
                Vector3 force=-offset/distance*strength*Mathf.Min(1f,distance);
                if(distance<1f)ApplyBrake(body,brake);
                body.AddForceAtPosition(force,hit.Point,ForceMode.Force);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                DiagnosticAppliedForces.Record(source.EmitterId,source.GetInstanceID(),hit.Item.InstanceId,body.GetInstanceID(),force,hit.Point,body.gameObject.activeInHierarchy);
#endif
                source.LastLoad+=body.mass;
            }
        }
        public bool TryFindIntakeSurface(VacuumEmitter source,SuckableObject item,Vector3 centre,float radius,out Vector3 point)
        {
            Vector3 chosen=default;bool found=false;float best=float.PositiveInfinity;
            var right=Vector3.Cross(Vector3.up,source.Forward).normalized;if(right.sqrMagnitude<.1f)right=Vector3.right;
            var up=Vector3.Cross(source.Forward,right).normalized;
            foreach(var collider in item.GameplayColliders)
            {
                if(collider==null || !collider.enabled || collider.isTrigger)continue;
                TryCandidate(collider.ClosestPoint(centre));
                if(!found)for(int x=-1;x<=1;x++)for(int y=-1;y<=1;y++)if(x!=0 || y!=0)
                    TryCandidate(collider.ClosestPoint(centre+right*(x*radius*.65f)+up*(y*radius*.65f)));
            }
            point=chosen;return found;
            void TryCandidate(Vector3 candidate)
            {
                float distance=Vector3.Distance(candidate,centre);if(distance>radius || distance>=best)return;
                Vector3 offset=candidate-source.Position;float rayLength=offset.magnitude;
                if(Vector3.Dot(offset,source.Forward)<-.025f)return;
                if(rayLength>.001f && (!Physics.Raycast(source.Position,offset/rayLength,out var hit,rayLength+.02f,blockerMask,QueryTriggerInteraction.Ignore) || hit.rigidbody!=item.Body))return;
                chosen=candidate;best=distance;found=true;
            }
        }
        public bool TryFindSurface(VacuumEmitter source,SuckableObject item,out Vector3 best)
        {
            best=default;Vector3 chosen=default;float bestDistance=float.PositiveInfinity;bool found=false;
            var origin=source.Position;var forward=source.Forward;
            // The centre ray prefers the side the player is pointing at, which creates useful torque.
            if(Physics.Raycast(origin,forward,out var central,source.Range,blockerMask,QueryTriggerInteraction.Ignore)
                && central.rigidbody==item.Body)
            { best=central.point;return true; }
            foreach(var collider in item.GameplayColliders)
            {
                if(collider==null || !collider.enabled)continue;
                if((collider.ClosestPoint(origin)-origin).sqrMagnitude<.000001f){best=origin;return true;}
                float along=Mathf.Clamp(Vector3.Dot(collider.bounds.center-origin,forward),.01f,source.Range);
                TryCandidate(collider.ClosestPoint(origin+forward*along));
                TryCandidate(collider.ClosestPoint(origin));
                TryCandidate(collider.bounds.center);
                if(!found)
                {
                    var right=Vector3.Cross(Vector3.up,forward).normalized;
                    if(right.sqrMagnitude<.1f)right=Vector3.right;
                    var up=Vector3.Cross(forward,right).normalized;
                    var extents=collider.bounds.extents;
                    float width=Mathf.Abs(right.x)*extents.x+Mathf.Abs(right.y)*extents.y+Mathf.Abs(right.z)*extents.z;
                    float height=Mathf.Abs(up.x)*extents.x+Mathf.Abs(up.y)*extents.y+Mathf.Abs(up.z)*extents.z;
                    for(int x=-1;x<=1;x++)for(int y=-1;y<=1;y++)
                        if(x!=0 || y!=0)TryCandidate(collider.ClosestPoint(collider.bounds.center+right*(x*width*.9f)+up*(y*height*.9f)));
                }
            }
            best=chosen;return found;
            void TryCandidate(Vector3 candidate)
            {
                Vector3 direction=candidate-origin;float distance=direction.magnitude;
                if(distance<.001f || distance>source.Range+.01f || distance>=bestDistance)return;
                if(Vector3.Dot(forward,direction/distance)<Mathf.Cos(source.HalfAngle*Mathf.Deg2Rad))return;
                if(!Physics.Raycast(origin,direction/distance,out var hit,distance+.04f,blockerMask,QueryTriggerInteraction.Ignore) || hit.rigidbody!=item.Body)return;
                float hitDistance=hit.distance;
                if(hitDistance<bestDistance){bestDistance=hitDistance;chosen=hit.point;found=true;}
            }
        }
    }
}
