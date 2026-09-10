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
        private readonly struct PendingForce
        {
            public readonly VacuumEmitter Source;public readonly VacuumDefinition Definition;public readonly SuckableObject Item;
            public readonly Vector3 Force,Point;public readonly float Distance;public readonly double Stiffness;
            public readonly int SourceId,EmitterId;
            public readonly bool Handheld,Swallow;
            public readonly Vector3 HoldTarget;
            public PendingForce(VacuumEmitter source,SuckableObject item,Vector3 force,Vector3 point,float distance,double stiffness)
            {
                Source=source;Definition=source.Definition;Item=item;Force=force;Point=point;Distance=distance;Stiffness=stiffness;SourceId=source.GetInstanceID();EmitterId=source.EmitterId;
                var receiver=source.GetComponent<IntakeReceiver>();
                Handheld=receiver!=null&&!receiver.IsTruck;
                Swallow=Handheld&&receiver.CanAdmit&&receiver.Accepts(item);
                var aim=source.GetComponent<PlayerMotor>()?.AimRay ?? new Ray(source.OriginGuard!=null?source.OriginGuard.position:source.Position,source.OriginGuard!=null?source.OriginGuard.forward:source.Forward);
                HoldTarget=Swallow?receiver.Position:aim.GetPoint(Mathf.Max(2.2f,item.RequiredIntakeSize*.5f+1f));
            }
        }
        private sealed class BodyBatch
        {public Rigidbody Body;public readonly List<PendingForce> Forces=new List<PendingForce>(5);}
        private readonly Dictionary<Rigidbody,BodyBatch> pending=new Dictionary<Rigidbody,BodyBatch>();
        private readonly List<BodyBatch> bodies=new List<BodyBatch>();
        private readonly Stack<BodyBatch> pool=new Stack<BodyBatch>();
        private bool collecting;private float step;
        // Every real force caller uses one Begin -> all player/truck Apply -> Flush transaction.
        // Collect/TryFindSurface remain read-only and do not require a transaction.
        public void BeginStep()=>BeginStep(Time.fixedDeltaTime);
        public void BeginStep(float deltaTime)
        {
            if(collecting)throw new InvalidOperationException("Finish or cancel the previous suction force batch.");
            if(!SuctionStabilityMath.Finite(deltaTime)||deltaTime<=0)throw new ArgumentOutOfRangeException(nameof(deltaTime));
            collecting=true;step=deltaTime;
        }
        public void CancelStep()
        {
            foreach(var batch in bodies){batch.Forces.Clear();batch.Body=null;pool.Push(batch);}
            bodies.Clear();pending.Clear();collecting=false;
        }
        private bool Live(PendingForce hit,Rigidbody body)=>body!=null&&hit.Source!=null&&hit.Source.isActiveAndEnabled&&
            hit.Source.Active&&hit.Source.Definition==hit.Definition&&hit.Definition!=null&&hit.Item!=null&&
            hit.Item.HasPhysicsAuthority&&(!body.isKinematic||hit.Item.IsMounted)&&hit.Item.Body==body&&(hit.Item.State==SuckableState.Available||hit.Item.State==SuckableState.InFlight)&&!hit.Item.WorldFrozen&&hit.Item.RunId==registry.RunId&&
            registry.Items.TryGetValue(hit.Item.InstanceId,out var item)&&item==hit.Item;
        private void Queue(VacuumEmitter source,SuctionContact contact,Vector3 force,double stiffness)
        {
            var body=contact.Item.Body;
            if(!pending.TryGetValue(body,out var batch)){
                batch=pool.Count>0?pool.Pop():new BodyBatch();batch.Body=body;pending.Add(body,batch);bodies.Add(batch);
            }
            // One player contributes once, even if a caller probes/applies twice in a step.
            for(int i=0;i<batch.Forces.Count;i++)if(batch.Forces[i].Source==source)return;
            batch.Forces.Add(new PendingForce(source,contact.Item,force,contact.Point,contact.Distance,stiffness));
        }
        private void ApplyActual(PendingForce hit,Rigidbody body,Vector3 force,Vector3 point)
        {
            if(force.x==0f&&force.y==0f&&force.z==0f)return;
            // A handheld lock pulls through the centre of mass so an off-centre ray cannot whip the prop around.
            if(hit.Source.FocusedItem==hit.Item)point=body.worldCenterOfMass;
            body.AddForceAtPosition(force,point,ForceMode.Force);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // Both attraction and any COM stabilizer are actual applied vectors, with their real
            // point and source identity. Never journal the unsolved requested force.
            DiagnosticAppliedForces.Record(hit.EmitterId,hit.SourceId,hit.Item.InstanceId,body.GetInstanceID(),force,point,body.gameObject.activeInHierarchy);
#endif
        }
        public void Flush()
        {
            if(!collecting)throw new InvalidOperationException("Begin the shared suction step before flushing.");
            try{
                // Canonical summation order makes caller order irrelevant, including opposing sources.
                bodies.Sort((a,b)=>(a.Body!=null?a.Body.GetInstanceID():0).CompareTo(b.Body!=null?b.Body.GetInstanceID():0));
                foreach(var batch in bodies){
                    var body=batch.Body;if(body==null)continue;
                    for(int i=batch.Forces.Count-1;i>=0;i--)if(!Live(batch.Forces[i],body))batch.Forces.RemoveAt(i);
                    if(batch.Forces.Count==0)continue;
                    // Collection is read-only. Only a still-valid committed force
                    // batch can release an authored mounting or wake its ambush.
                    foreach(var hit in batch.Forces)hit.Item.AcceptSuction(hit.Handheld);
                    foreach(var hit in batch.Forces)if(hit.Source.FocusedItem==hit.Item){body.angularVelocity*=Mathf.Exp(-5f*step);break;}
                    batch.Forces.Sort((a,b)=>{int c=a.EmitterId.CompareTo(b.EmitterId);return c!=0?c:a.SourceId.CompareTo(b.SourceId);});
                    body.maxLinearVelocity=25f;body.maxAngularVelocity=20f;
                    if(ApplyHandheld(batch))continue;
                    float nearest=float.PositiveInfinity;double stiffness=0;var total=new SuctionStepVector(0,0,0);
                    foreach(var hit in batch.Forces){nearest=Mathf.Min(nearest,hit.Distance);stiffness+=hit.Stiffness;total+=D(hit.Force);}
                    body.maxLinearVelocity=25f;body.maxAngularVelocity=20f;
                    // Far field remains the exact existing physical force, not a mass-normalized motor.
                    if(nearest>=Mathf.Max(1f,2f*body.linearVelocity.magnitude*step)){
                        foreach(var hit in batch.Forces)ApplyActual(hit,body,hit.Force,hit.Point);
                        continue;
                    }
                    var solution=SuctionStabilityMath.Solve(body.mass,stiffness,step,nearest,D(body.linearVelocity),total);
                    foreach(var hit in batch.Forces)ApplyActual(hit,body,hit.Force*(float)solution.AttractionGain,hit.Point);
                    // Distribute the common stabilizer for honest source attribution. Applying at
                    // the actual COM adds no artificial torque; the surface attraction still does.
                    Vector3 remainder=U(solution.CorrectionForce);Vector3 correction=remainder;
                    for(int i=0;i<batch.Forces.Count;i++){
                        var hit=batch.Forces[i];Vector3 share=i==batch.Forces.Count-1?remainder:correction*(float)(hit.Stiffness/stiffness);
                        remainder-=share;ApplyActual(hit,body,share,body.worldCenterOfMass);
                    }
                }
            }finally{CancelStep();}
        }
        private bool ApplyHandheld(BodyBatch batch)
        {
            var body=batch.Body;int collector=-1;byte players=0;float nearest=float.PositiveInfinity,totalPower=0;Vector3 target=Vector3.zero;
            for(int i=0;i<batch.Forces.Count;i++)
            {
                var hit=batch.Forces[i];if(!hit.Handheld)continue;
                players++;
                if(hit.Swallow&&hit.Distance<nearest){collector=i;nearest=hit.Distance;}
                float power=hit.Definition.Power;totalPower+=power;target+=hit.HoldTarget*power;
                hit.Item.MarkHeldForHauling();
            }
            if(totalPower<=0)return false;
            // A fitting item goes to one intake promptly. Hauling uses a shared target
            // and a real force budget: two/three players can support twice/three times the mass.
            bool swallow=collector>=0;target=swallow?batch.Forces[collector].HoldTarget:target/totalPower;
            var mode=swallow?SuctionMode.Collecting:totalPower<body.mass*Physics.gravity.magnitude*1.08f?SuctionMode.NeedHelp:SuctionMode.Holding;
            foreach(var hit in batch.Forces)if(hit.Handheld)
                hit.Source.PresentPull(mode,players,swallow&&hit.Source==batch.Forces[collector].Source?hit.Item.InstanceId:0);
            Vector3 velocity=Vector3.ClampMagnitude((target-body.worldCenterOfMass)*(swallow?12f:5f),swallow?12f:7f);
            Vector3 acceleration=(velocity-body.linearVelocity)*((1f-Mathf.Exp(-12f*step))/step);
            if(body.useGravity)acceleration-=Physics.gravity;
            Vector3 force=acceleration*body.mass;
            if(swallow)ApplyActual(batch.Forces[collector],body,force,body.worldCenterOfMass);
            else
            {
                force=Vector3.ClampMagnitude(force,totalPower);
                foreach(var hit in batch.Forces)if(hit.Handheld)
                    ApplyActual(hit,body,force*(hit.Definition.Power/totalPower),body.worldCenterOfMass);
            }
            return true;
        }
        private static SuctionStepVector D(Vector3 value)=>new SuctionStepVector(value.x,value.y,value.z);
        private static Vector3 U(SuctionStepVector value)=>new Vector3((float)value.X,(float)value.Y,(float)value.Z);
        private Collider[] overlap=new Collider[128];
        private readonly HashSet<Rigidbody> seen=new HashSet<Rigidbody>();
        private readonly List<SuctionContact> contacts=new List<SuctionContact>(128);
        private readonly int itemMask=LayerMask.GetMask("Items");
        private readonly int blockerMask=LayerMask.GetMask("World","Items");
        public IReadOnlyList<SuctionContact> Collect(VacuumEmitter source)
        {
            contacts.Clear();seen.Clear();
            if(source==null)return contacts;
            if(!source.Active || source.Definition==null || source.Range<=0 || !source.HasClearSourcePath()) {source.FocusedItem=null;return contacts;}
            var receiver=source.GetComponent<IntakeReceiver>();
            if(receiver!=null&&!receiver.IsTruck)
            {
                // Pick with the eye ray, then keep that one physical item while the held pull brings it to the nozzle.
                var aim=source.OriginGuard!=null?source.OriginGuard:source.Source;
                Vector3 eye=aim!=null?aim.position:source.Position,forward=aim!=null?aim.forward:source.Forward;
                var motor=source.GetComponent<PlayerMotor>();
                if(motor!=null){var ray=motor.AimRay;eye=ray.origin;forward=ray.direction;}
                var selected=source.FocusedItem;
                if(selected!=null&&(selected.State!=SuckableState.Available||selected.WorldFrozen||
                    Vector3.Distance(eye,selected.Body.worldCenterOfMass)>source.Range+selected.RequiredIntakeSize||
                    Vector3.Angle(forward,selected.Body.worldCenterOfMass-eye)>65))selected=null;
                if(selected==null&&Physics.Raycast(eye,forward,out var hit,source.Range,blockerMask|LayerMask.GetMask("Enemies"),QueryTriggerInteraction.Ignore))
                    selected=hit.rigidbody!=null?hit.rigidbody.GetComponent<SuckableObject>():null;
                source.FocusedItem=selected;
                if(selected==null||selected.State!=SuckableState.Available||selected.WorldFrozen||selected.RunId!=registry.RunId||
                    !registry.Items.TryGetValue(selected.InstanceId,out var actual)||actual!=selected)return contacts;
                Vector3 point=selected.Body.worldCenterOfMass;
                float best=float.PositiveInfinity;
                foreach(var c in selected.GameplayColliders)
                {
                    if(c==null||!c.enabled)continue;
                    var p=c.ClosestPoint(source.Position);float distance=Vector3.Distance(p,source.Position);
                    if(distance>=best||distance>source.Range)continue;
                    if(distance>.02f&&Physics.Raycast(source.Position,(p-source.Position).normalized,out var block,distance+.015f,blockerMask,QueryTriggerInteraction.Ignore)&&block.rigidbody!=selected.Body)continue;
                    best=distance;point=p;
                }
                if(!float.IsPositiveInfinity(best))contacts.Add(new SuctionContact(selected,point,best));
                return contacts;
            }
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
                if(receiver!=null&&receiver.IsTruck&&!receiver.CanAutomaticallyReceive(item))continue;
                if(item==null || item.InstanceId==0 || item.RunId!=registry.RunId || !registry.Items.TryGetValue(item.InstanceId,out var registered) || registered!=item || (item.State!=SuckableState.Available && item.State!=SuckableState.InFlight) || item.WorldFrozen)continue;
                if(TryFindSurface(source,item,out Vector3 point))
                    contacts.Add(new SuctionContact(item,point,Vector3.Distance(source.Position,point)));
            }
            contacts.Sort((a,b)=>a.Item.InstanceId.CompareTo(b.Item.InstanceId));
            return contacts;
        }
        public void Apply(VacuumEmitter source)
        {
            if(!collecting)throw new InvalidOperationException("Begin the shared suction step before applying sources.");
            if(source==null)return;
            source.PresentPull(SuctionMode.Idle,0);
            var found=Collect(source);source.LastAffectedCount=found.Count;source.LastLoad=0;
            foreach(var hit in found)
            {
                var body=hit.Item.Body;if(body==null||!hit.Item.HasPhysicsAuthority||body.isKinematic&&!hit.Item.IsMounted)continue;
                Vector3 offset=hit.Point-source.Position;float distance=offset.magnitude;
                // Preserve the existing zero-distance drag-only branch, but solve it in the same batch.
                if(distance<.025f){Queue(source,hit,Vector3.zero,source.Definition.Power);source.LastLoad+=body.mass;continue;}
                float edge=Mathf.Cos(source.HalfAngle*Mathf.Deg2Rad);
                float dot=Vector3.Dot(source.Forward,offset/distance);
                float center=Mathf.InverseLerp(edge,1f,dot);
                float strength=source.Definition.Power*(.3f+.7f*(1f-distance/source.Range))*(.25f+.75f*center);
                Vector3 force=-offset/distance*strength*Mathf.Min(1f,distance);
                Queue(source,hit,force,strength*Mathf.Min(1f,distance)/distance);
                source.LastLoad+=body.mass;
            }
        }
        public bool TryFindIntakeSurface(VacuumEmitter source,SuckableObject item,Vector3 centre,float radius,out Vector3 point)
        {
            var truck=source.GetComponent<TruckIntake>();
            if(truck!=null)return TruckDeliveryGeometry.TryNearbySurface(truck,item,out point);
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
