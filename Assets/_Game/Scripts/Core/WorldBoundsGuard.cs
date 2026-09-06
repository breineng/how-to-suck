using System;
using System.Collections.Generic;
using UnityEngine;
using NVector3 = System.Numerics.Vector3;

namespace HowToSuck
{
    [DisallowMultipleComponent]
    public sealed class WorldBoundsGuard : MonoBehaviour
    {
        // World coordinates. This is a diagnostic boundary, not an invisible wall.
        public Bounds AllowedBounds = new Bounds(new Vector3(0,10,0),new Vector3(100,60,100));
        public float MinimumY = -10f;
        public Transform SafePoint;
        [Min(.05f)] public float RetrySeconds = .25f;
        [Range(0,4)] public int SearchRings = 2;
        [Min(.1f)] public float SearchSpacing = .9f;
        [Min(1)] public int MaximumCandidateProbes = 85;
        [Min(.1f)] public float GroundProbeUp = 1f;
        [Min(.1f)] public float GroundProbeDown = 2f;
        [Min(.01f)] public float RecoveryGroundGap = .06f;
        [Min(.001f)] public float CapsuleClearance = .025f;

        public int TotalRecovered { get; private set; }
        public int TotalLost { get; private set; }
        public int TotalCandidateProbes { get; private set; }
        public int RecoveryPendingCount => pending.Count;
        public string LastDiagnostic { get; private set; }

        private string runId;
        private Bounds area;
        private float lowerY,retry,spacing,probeUp,probeDown,groundGap,clearance;
        private int rings,maxProbes;
        private readonly List<NVector3> anchors = new List<NVector3>();
        private readonly HashSet<int> suppressed = new HashSet<int>();
        private readonly Dictionary<int,double> pending = new Dictionary<int,double>();
        private readonly List<int> retired = new List<int>();
        private readonly List<SuckableObject> lost = new List<SuckableObject>();
        private Collider[] occupied = new Collider[32];

        public bool TryValidate(out string error)
        {
            bool valid=WorldBoundsRules.ValidBounds(World07Geometry.Numerics(AllowedBounds.min),
                World07Geometry.Numerics(AllowedBounds.max),MinimumY) &&
                SafePoint!=null && !WorldBoundsRules.Outside(World07Geometry.Numerics(SafePoint.position),
                    World07Geometry.Numerics(AllowedBounds.min),World07Geometry.Numerics(AllowedBounds.max),MinimumY) &&
                Positive(RetrySeconds) && SearchRings>=0 && SearchRings<=4 && Positive(SearchSpacing) &&
                MaximumCandidateProbes>0 && MaximumCandidateProbes<=256 &&
                Positive(GroundProbeUp) && Positive(GroundProbeDown) &&
                Positive(RecoveryGroundGap) && Positive(CapsuleClearance) && RecoveryGroundGap>CapsuleClearance;
            error=valid?null:"World bounds need a finite area/lower limit, an inside safe point and bounded positive recovery settings.";
            return valid;
        }
        private static bool Positive(float value) => WorldBoundsRules.Finite(value)&&value>0;

        public void BeginRun(string id,IReadOnlyList<Transform> fallbackSpawns)
        {
            if(string.IsNullOrWhiteSpace(id))throw new ArgumentException("Bounds guard needs current RunId.");
            if(!TryValidate(out var error))throw new InvalidOperationException(error);
            Clear();runId=id;area=AllowedBounds;lowerY=MinimumY;retry=RetrySeconds;spacing=SearchSpacing;
            rings=SearchRings;maxProbes=MaximumCandidateProbes;probeUp=GroundProbeUp;probeDown=GroundProbeDown;
            groundGap=RecoveryGroundGap;clearance=CapsuleClearance;
            anchors.Add(World07Geometry.Numerics(SafePoint.position));
            if(fallbackSpawns!=null)
                foreach(var spawn in fallbackSpawns)
                    if(spawn!=null)anchors.Add(World07Geometry.Numerics(spawn.position));
        }
        public bool RequiresRecovery(int playerId) => suppressed.Contains(playerId);

        public void Step(AuthorityWorld world,double now,Func<PlayerMotor,Vector3,bool> recover,
            Action<GameObject> despawn)
        {
            suppressed.Clear();
            if(world==null || !world.HasAuthority || !world.IsRunning || runId!=world.RunId ||
                string.IsNullOrEmpty(runId) || !WorldBoundsRules.Finite(now))return;
            if(recover==null || despawn==null)throw new ArgumentNullException("Authority recovery/despawn owner is required.");
            retired.Clear();
            foreach(var entry in pending)if(!world.Players.ContainsKey(entry.Key))retired.Add(entry.Key);
            foreach(int id in retired)pending.Remove(id);
            bool synced=false;
            foreach(var entry in world.Players)
            {
                var player=entry.Value;
                if(player==null || !player.isActiveAndEnabled){pending.Remove(entry.Key);continue;}
                if(!Outside(player.transform.position)){pending.Remove(entry.Key);continue;}
                suppressed.Add(entry.Key);
                if(pending.TryGetValue(entry.Key,out var nextTry) && now<nextTry)continue;
                pending[entry.Key]=now+retry;
                if(!synced){Physics.SyncTransforms();synced=true;}
                var capsule=player.GetComponent<CharacterController>();
                bool found=WorldBoundsRules.TryChooseSafe(anchors,rings,spacing,maxProbes,
                    (NVector3 desired,out NVector3 result)=>Probe(player,capsule,desired,out result),
                    out var chosen,out var probes);
                TotalCandidateProbes+=probes;
                if(found &&
                    recover(player,World07Geometry.Unity(chosen)) &&
                    Vector3.Distance(player.transform.position,World07Geometry.Unity(chosen))<.001f)
                {
                    TotalRecovered++;pending.Remove(entry.Key);
                    LastDiagnostic="Player "+entry.Key+" recovered after "+probes+" candidate probes.";
                    // Subsequent players must observe the capsule just placed by the motor.
                    Physics.SyncTransforms();
                }
                else LastDiagnostic="Recovery pending for player "+entry.Key+": no free supported capsule or motor declined recovery.";
            }
            lost.Clear();
            foreach(var item in world.Loot.Items.Values)
                if(item!=null && item.RunId==runId && item.State==SuckableState.Available && item.CargoRole==CargoRole.OrdinaryLoot &&
                    Outside(item.transform.position))lost.Add(item);
            foreach(var item in lost)
            {
                if(item==null || item.RunId!=runId ||
                    !world.Loot.Items.TryGetValue(item.InstanceId,out var current) || current!=item ||
                    !item.TryTransition(SuckableState.Available,SuckableState.Lost))continue;
                // No CollectionRecord, money event, register or spawn is called on this path.
                world.Loot.Unregister(item);TotalLost++;despawn(item.gameObject);
            }
            lost.Clear();
        }

        public bool TryFindRecoveryPosition(PlayerMotor player,out Vector3 position)
        {
            position=default;
            if(string.IsNullOrEmpty(runId)||player==null||!player.HasMovementAuthority)return false;
            Physics.SyncTransforms();var capsule=player.GetComponent<CharacterController>();
            bool found=WorldBoundsRules.TryChooseSafe(anchors,rings,spacing,maxProbes,
                (NVector3 desired,out NVector3 result)=>Probe(player,capsule,desired,out result),out var chosen,out var probes);
            TotalCandidateProbes+=probes;if(found)position=World07Geometry.Unity(chosen);return found;
        }
        private bool Outside(Vector3 point) => WorldBoundsRules.Outside(World07Geometry.Numerics(point),
            World07Geometry.Numerics(area.min),World07Geometry.Numerics(area.max),lowerY);

        private bool Probe(PlayerMotor player,CharacterController capsule,NVector3 desired,out NVector3 result)
        {
            result=default;Vector3 candidate=World07Geometry.Unity(desired);
            if(capsule==null || !capsule.enabled || Outside(candidate) ||
                !Physics.Raycast(candidate+Vector3.up*probeUp,Vector3.down,out var ground,probeUp+probeDown,
                    LayerMask.GetMask("World"),QueryTriggerInteraction.Ignore))return false;
            if(!World07Geometry.Finite(ground.normal) || !WorldBoundsRules.Finite(capsule.slopeLimit) ||
                ground.normal.y<Mathf.Cos(capsule.slopeLimit*Mathf.Deg2Rad))return false;
            candidate.y=ground.point.y+groundGap;
            if(!World07Geometry.TryCapsule(capsule,candidate,clearance,out var bottom,out var top,out var radius))return false;
            if(Outside(new Vector3(bottom.x-radius,bottom.y-radius,bottom.z-radius)) ||
                Outside(new Vector3(top.x+radius,top.y+radius,top.z+radius)))return false;
            int count;
            while(true)
            {
                count=Physics.OverlapCapsuleNonAlloc(bottom,top,radius,occupied,
                    LayerMask.GetMask("World","Items","Player","Enemies"),QueryTriggerInteraction.Ignore);
                if(count<occupied.Length)break;
                if(occupied.Length>=8192)
                {
                    occupied=Physics.OverlapCapsule(bottom,top,radius,
                        LayerMask.GetMask("World","Items","Player","Enemies"),QueryTriggerInteraction.Ignore);
                    count=occupied.Length;break;
                }
                Array.Resize(ref occupied,occupied.Length*2);
            }
            for(int i=0;i<count;i++)
            {
                var shape=occupied[i];
                if(shape==null || shape.transform==player.transform || shape.transform.IsChildOf(player.transform))continue;
                return false;
            }
            result=World07Geometry.Numerics(candidate);return true;
        }

        public void Clear()
        {
            runId=null;anchors.Clear();suppressed.Clear();pending.Clear();retired.Clear();lost.Clear();
            TotalRecovered=TotalLost=TotalCandidateProbes=0;LastDiagnostic=null;
        }
    }
}
