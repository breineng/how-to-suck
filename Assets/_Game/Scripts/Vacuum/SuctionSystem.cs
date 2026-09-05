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
        public SuctionSystem(LootRegistry lootRegistry) {registry=lootRegistry ?? throw new ArgumentNullException(nameof(lootRegistry));}
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
                if(distance<.025f)continue;
                float edge=Mathf.Cos(source.HalfAngle*Mathf.Deg2Rad);
                float dot=Vector3.Dot(source.Forward,offset/distance);
                float center=Mathf.InverseLerp(edge,1f,dot);
                float strength=source.Definition.Power*(.3f+.7f*(1f-distance/source.Range))*(.25f+.75f*center);
                body.maxLinearVelocity=25f;body.maxAngularVelocity=20f;
                body.AddForceAtPosition(-offset/distance*strength,hit.Point,ForceMode.Force);
                source.LastLoad+=body.mass;
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
