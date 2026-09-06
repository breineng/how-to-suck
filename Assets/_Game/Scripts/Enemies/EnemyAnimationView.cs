using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
namespace HowToSuck
{
    // Presentation only. The authority snapshot chooses the role/time; animation cannot emit damage.
    [DisallowMultipleComponent]
    public sealed class EnemyAnimationView : MonoBehaviour
    {
        public Animator Animator;
        public AnimationClip Idle, Move, Tell, Attack, Hit, Defeat, SweepTell, SweepAttack;
        public Transform ProjectileVisual;
        private PlayableGraph graph;
        private AnimationClipPlayable playable;
        private AnimationClip current;
        private bool ready;
        public bool TryValidate(out string error)
        {
            bool valid=Animator!=null&&Animator.avatar!=null&&Animator.avatar.isValid&&!Animator.applyRootMotion&&
                Idle!=null&&Move!=null&&Tell!=null&&Attack!=null&&Hit!=null&&Defeat!=null;
            error=valid?null:"Enemy presentation needs its Generic animator and all six actual animation roles.";return valid;
        }
        public float DefeatDuration=>Defeat!=null?Defeat.length:1.2f;
        public void Present(EnemySnapshot state)
        {
            if(!isActiveAndEnabled||!TryValidate(out _))return;
            AnimationClip clip=state.Phase==EnemyPhase.Move?Move:state.Phase==EnemyPhase.Tell?(state.Sweep&&SweepTell!=null?SweepTell:Tell):
                state.Phase==EnemyPhase.Attack?(state.Sweep&&SweepAttack!=null?SweepAttack:Attack):state.Phase==EnemyPhase.Hit?Hit:state.Phase==EnemyPhase.Defeated?Defeat:Idle;
            if(!ready)
            {
                graph=PlayableGraph.Create("Enemy presentation "+name);graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                AnimationPlayableOutput.Create(graph,"Authored clips",Animator);ready=true;
            }
            if(current!=clip)
            {
                if(playable.IsValid())graph.DestroyPlayable(playable);
                playable=AnimationClipPlayable.Create(graph,clip);playable.SetApplyFootIK(false);playable.SetApplyPlayableIK(false);
                ((AnimationPlayableOutput)graph.GetOutput(0)).SetSourcePlayable(playable);current=clip;graph.Play();
            }
            double time=Math.Max(0,state.ObservedAt-state.PhaseStartedAt);
            bool looping=state.Phase==EnemyPhase.Idle||state.Phase==EnemyPhase.Move||state.Phase==EnemyPhase.Recover;
            playable.SetTime(looping?time%Math.Max(.001,clip.length):Math.Min(time,clip.length));graph.Evaluate(0);
            if(ProjectileVisual!=null)
            { ProjectileVisual.gameObject.SetActive(state.ProjectileActive);if(state.ProjectileActive)ProjectileVisual.position=state.ProjectilePosition; }
        }
        private void OnDisable(){if(ready)graph.Destroy();ready=false;current=null;}
    }
}
