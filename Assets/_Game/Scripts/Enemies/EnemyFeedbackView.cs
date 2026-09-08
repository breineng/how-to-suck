using UnityEngine;
using HowToSuck.Audio;

namespace HowToSuck
{
    [DefaultExecutionOrder(11000),DisallowMultipleComponent]
    public sealed class EnemyFeedbackView:MonoBehaviour
    {
        EnemyActor actor;
        Renderer[] renderers;
        Color[] colors;
        MaterialPropertyBlock block;
        int health=-1;
        uint phaseRevision;
        float flash,trailAt;
        bool appeared,dead;
        LineRenderer wave;
        ParticleSystem projectile;
        void Awake()
        {
            actor=GetComponent<EnemyActor>();renderers=GetComponentsInChildren<Renderer>(true);colors=new Color[renderers.Length];block=new MaterialPropertyBlock();
            for(int i=0;i<renderers.Length;i++){var m=renderers[i].sharedMaterial;colors[i]=m!=null&&m.HasProperty("_BaseColor")?m.GetColor("_BaseColor"):Color.white;}
        }
        void LateUpdate()
        {
            if(actor==null||actor.InstanceId==0)return;
            if(!appeared)
            {
                appeared=true;health=actor.Health;
                CombatParticles.Burst(transform.position+Vector3.up*.25f,new Color(.5f,.43f,.31f),actor.BossKey.IsValid?110:40,actor.BossKey.IsValid?5:2,.3f,1.2f);
                GameAudioRoot.Current?.Action(actor.BossKey.IsValid?SfxId.BossWarning:SfxId.EnemyLeap,transform.position,!actor.BossKey.IsValid);
            }
            if(actor.Health<health){flash=1;health=actor.Health;}
            flash=Mathf.MoveTowards(flash,0,Time.unscaledDeltaTime*5);
            for(int i=0;i<renderers.Length;i++)if(renderers[i]!=null)
            {renderers[i].GetPropertyBlock(block);block.SetColor("_BaseColor",Color.Lerp(colors[i],new Color(2,.035f,.015f),flash));block.SetColor("_EmissionColor",new Color(.8f,0,0)*flash);renderers[i].SetPropertyBlock(block);}
            if(actor.Health==0&&!dead)
            {
                dead=true;bool boss=actor.BossKey.IsValid;
                CombatParticles.Burst(transform.position+Vector3.up*.65f,boss?new Color(1,.26f,.045f):new Color(.53f,.46f,.33f),boss?180:65,boss?8:4,boss?.45f:.22f,boss?2:1);
                if(boss)GameAudioRoot.Current?.Action(SfxId.BossBurst,transform.position,false);
            }
            var state=actor.Snapshot;
            if(state.PhaseRevision!=phaseRevision)
            {
                phaseRevision=state.PhaseRevision;
                if(state.Phase==EnemyPhase.Tell)GameAudioRoot.Current?.Action(actor.BossKey.IsValid?SfxId.BossWarning:SfxId.EnemyLeap,transform.position,true,actor.BossKey.IsValid?.6f:.45f);
            }
            bool showWave=actor.BossKey.IsValid&&actor.Health>0&&state.Sweep&&(state.Phase==EnemyPhase.Tell||state.Phase==EnemyPhase.Attack);
            if(showWave)
            {
                if(wave==null){var go=new GameObject("Boss ground warning");go.transform.SetParent(transform,false);wave=go.AddComponent<LineRenderer>();wave.sharedMaterial=CombatParticles.Material;wave.loop=true;wave.positionCount=64;wave.startWidth=wave.endWidth=.07f;}
                wave.enabled=true;float radius=state.Phase==EnemyPhase.Tell?(actor.Enraged?6.5f:5):Mathf.Lerp(.6f,actor.Enraged?6.5f:5,Mathf.Clamp01((float)(state.ObservedAt-state.PhaseStartedAt)/actor.Definition.SweepAttackSeconds));
                wave.startColor=wave.endColor=state.Phase==EnemyPhase.Tell?new Color(1,.15f,.025f,.65f):new Color(1,.55f,.05f,1);
                for(int i=0;i<64;i++){float a=i*Mathf.PI*2/64;wave.SetPosition(i,transform.position+new Vector3(Mathf.Cos(a)*radius,.08f,Mathf.Sin(a)*radius));}
            }
            else if(wave!=null)wave.enabled=false;
            if(state.ProjectileActive&&Time.unscaledTime>=trailAt)
            {
                trailAt=Time.unscaledTime+.025f;
                if(projectile==null)projectile=CombatParticles.Create("Enemy projectile trail",state.ProjectilePosition,.35f);
                var emit=new ParticleSystem.EmitParams{position=state.ProjectilePosition,startColor=new Color(1,.24f,.025f),startSize=actor.BossKey.IsValid?.5f:.25f,velocity=Vector3.zero,startLifetime=.28f};projectile.Emit(emit,3);
            }
        }
        void OnDestroy(){if(projectile!=null)Destroy(projectile.gameObject);}
    }
}
