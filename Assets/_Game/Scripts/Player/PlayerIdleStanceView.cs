using UnityEngine;

namespace HowToSuck
{
    // One blend feeds the physical mount and both rendered body/tool paths.
    [DefaultExecutionOrder(-100), DisallowMultipleComponent]
    public sealed class PlayerIdleStanceView : MonoBehaviour
    {
        public float Weight {get;private set;}
        // Readable diagnostic: 0 means the optional pelvis shift had no safe leg reach.
        public float LowerBodyOffsetFraction {get;internal set;}=1;
        private PlayerMotor motor;
        private PlayerAnimationView view;
        private VacuumEmitter emitter;
        private float quietTime;
        private uint revision;
        public WorkerStanceCandidatePolicy.Frame Frame(float pitch)=>WorkerStanceCandidatePolicy.Evaluate(pitch,Weight);
        private void OnEnable(){motor=GetComponent<PlayerMotor>();view=GetComponent<PlayerAnimationView>();emitter=GetComponent<VacuumEmitter>();Weight=quietTime=0;revision=motor!=null?motor.PresentationResetRevision:0;}
        private void Update()
        {
            if(motor==null||view==null||!motor.isActiveAndEnabled||!view.isActiveAndEnabled)return;
            if(revision!=motor.PresentationResetRevision){Weight=quietTime=0;revision=motor.PresentationResetRevision;}
            if(view.World!=null&&!view.World.IsRunning)return;
            bool quiet=motor.IsGrounded&&motor.PlanarSpeed<.15f&&motor.LastIntent.Move.sqrMagnitude<.0001f&&
                view.Animator!=null&&view.Animator.isActiveAndEnabled&&!view.Animator.IsInTransition(0)&&view.Animator.GetCurrentAnimatorStateInfo(0).IsName("Idle")&&
                view.StopMotion!=null&&view.StopMotion.Reference!=null&&!view.StopMotion.Active&&!view.StopMotion.Releasing&&
                emitter!=null&&emitter.Definition!=null&&emitter.Definition.TierId=="mk1";
            quietTime=quiet?quietTime+Time.deltaTime:0;
            Weight=Mathf.MoveTowards(Weight,quietTime>.2f?1:0,Time.deltaTime/.18f);
        }
        private void OnDisable(){Weight=quietTime=0;}
    }
}
