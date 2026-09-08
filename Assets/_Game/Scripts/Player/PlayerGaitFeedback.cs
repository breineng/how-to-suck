using UnityEngine;
using HowToSuck.Audio;
namespace HowToSuck
{
    [DefaultExecutionOrder(2000),DisallowMultipleComponent]
    public sealed class PlayerGaitFeedback:MonoBehaviour
    {
        PlayerAnimationView animationView;PlayerMotor motor;PlayerView view;
        bool tracking;float previousPhase,weight;uint revision;
        public uint StepCount {get;private set;}
        public Vector3 CameraOffset {get;private set;}
        public float CameraRoll {get;private set;}
        void Awake(){animationView=GetComponent<PlayerAnimationView>();motor=GetComponent<PlayerMotor>();view=GetComponent<PlayerView>();}
        bool Sample(out float phase,out bool running)
        {
            phase=0;running=false;var root=GameAudioRoot.Current;
            if(view==null||!view.IsLocal||motor==null||!motor.IsGrounded||motor.PlanarSpeed<.15f||
                root==null||!root.Playing||view.Input==null||view.Input.MenuOpen||
                animationView==null||animationView.Animator==null||!animationView.Animator.isActiveAndEnabled)return false;
            var animator=animationView.Animator;
            var state=animator.IsInTransition(0)?animator.GetNextAnimatorStateInfo(0):animator.GetCurrentAnimatorStateInfo(0);
            running=state.IsName("Run");if(!running&&!state.IsName("Walk"))return false;
            phase=Mathf.Repeat(state.normalizedTime,1);return true;
        }
        // Sample the actual Animator clock, including clip speed and preserved
        // Walk/Run transition phase. There is no independent footstep timer.
        public void SampleCamera(float dt)
        {
            bool walking=Sample(out float phase,out bool running);
            float speed=motor!=null?motor.PlanarSpeed:0;
            weight=Mathf.MoveTowards(weight,walking?Mathf.Clamp01(speed/(running?7:4.5f)):0,dt*5);
            if(!walking){CameraOffset=Vector3.Lerp(CameraOffset,Vector3.zero,Mathf.Clamp01(dt*14));CameraRoll=Mathf.Lerp(CameraRoll,0,Mathf.Clamp01(dt*14));return;}
            float angle=phase*Mathf.PI*2,amount=running?.045f:.023f;
            CameraOffset=new Vector3(Mathf.Sin(angle)*amount*.45f,-Mathf.Cos(angle*2)*amount,0)*weight;
            CameraRoll=Mathf.Sin(angle)*(running?1.1f:.55f)*weight;
        }
        void LateUpdate()
        {
            if(!Sample(out float phase,out bool running)){tracking=false;return;}
            if(revision!=motor.PresentationResetRevision){revision=motor.PresentationResetRevision;tracking=false;}
            if(tracking)
            {
                float delta=Mathf.Repeat(phase-previousPhase,1);
                // C28 authored heel strikes are at normalized phase 0 and .5.
                // Ignore a discontinuity instead of replaying accumulated footsteps.
                if(delta>0&&delta<.5f&&Mathf.FloorToInt((previousPhase+delta)*2)>Mathf.FloorToInt(previousPhase*2))
                {
                    var root=GameAudioRoot.Current;
                    bool wood=Physics.Raycast(motor.transform.position+Vector3.up*.2f,Vector3.down,out var ground,.7f,LayerMask.GetMask("World"),QueryTriggerInteraction.Ignore)&&
                        (ground.collider.name.ToLowerInvariant().Contains("wood")||ground.collider.name.ToLowerInvariant().Contains("floor")&&(root.Session.ContractState.ContractId??"").StartsWith("old_house"));
                    root.Action(wood?SfxId.FootstepWood:SfxId.FootstepConcrete,motor.transform.position,false,running?.72f:.53f);StepCount++;
                }
            }
            previousPhase=phase;tracking=true;
        }
        void OnDisable(){tracking=false;weight=0;CameraOffset=Vector3.zero;CameraRoll=0;}
    }
}
