using UnityEngine;

namespace HowToSuck
{
    public enum SuctionMode : byte { Idle, Collecting, Holding, NeedHelp }
    public sealed class VacuumEmitter : MonoBehaviour
    {
        public VacuumDefinition Definition;
        public Transform Source;
        public Transform OriginGuard;
        public float Range=5f;
        public float HalfAngle=30f;
        public int EmitterId=1;
        public bool Active;
        public Vector3 Position => Source!=null ? Source.position : transform.position;
        public Vector3 Forward => Source!=null ? Source.forward : transform.forward;
        public int LastAffectedCount { get; internal set; }
        public float LastLoad { get; internal set; }
        public SuckableObject FocusedItem { get; internal set; }
        public SuctionMode PullMode { get; private set; }
        public byte PullingPlayers { get; private set; }
        public ulong CollectingItemId { get; private set; }
        public void PresentPull(SuctionMode mode,byte players,ulong collectingItemId=0)
        {PullMode=mode;PullingPlayers=players;CollectingItemId=mode==SuctionMode.Collecting?collectingItemId:0;}
        public bool HasClearSourcePath()
        {
            if(Physics.CheckSphere(Position,.015f,LayerMask.GetMask("World"),QueryTriggerInteraction.Ignore))return false;
            if(OriginGuard==null)return true;
            Vector3 delta=Position-OriginGuard.position;
            return delta.sqrMagnitude<.0001f || !Physics.SphereCast(OriginGuard.position,.04f,delta.normalized,out _,delta.magnitude,LayerMask.GetMask("World"),QueryTriggerInteraction.Ignore);
        }
        private void OnDrawGizmosSelected()
        {
            Gizmos.color=Color.cyan;
            var rotation=Quaternion.LookRotation(Forward);
            var origin=Position;
            for(int i=0;i<12;i++)
            {
                float angle=i*Mathf.PI/6;
                var edge=origin+rotation*new Vector3(Mathf.Cos(angle)*Mathf.Tan(HalfAngle*Mathf.Deg2Rad)*Range,
                    Mathf.Sin(angle)*Mathf.Tan(HalfAngle*Mathf.Deg2Rad)*Range,Range);
                Gizmos.DrawLine(origin,edge);
            }
        }
    }
}
