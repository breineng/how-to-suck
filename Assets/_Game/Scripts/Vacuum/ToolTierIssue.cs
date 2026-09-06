using System;
using UnityEngine;
using HowToSuck.Audio;
namespace HowToSuck
{
    // One actor owns two render-only instances and one physical endpoint. Prepare precedes input activation.
    [DisallowMultipleComponent,RequireComponent(typeof(PlayerMotor),typeof(VacuumEmitter),typeof(IntakeReceiver))]
    public sealed class ToolTierIssue : MonoBehaviour
    {
        public ToolTierMountProfile ActiveProfile {get;private set;}
        public string IssuedTier {get;private set;}
        public string RunId {get;private set;}
        private Transform local,remote,physicalEnd,container;
        private PlayerMotor motor;private PlayerView view;private PlayerAnimationView animationView;
        private VacuumEmitter emitter;private IntakeReceiver receiver;
        private bool prepared,released;private VacuumAudioEmitter audioEmitter;
        public bool TryMount(string tier,float pitch,out Vector3 position)
        {
            position=default;
            if(!prepared||!isActiveAndEnabled||tier!=IssuedTier||ActiveProfile==null||ActiveProfile.TierId!=tier)return false;
            var stance=GetComponent<PlayerIdleStanceView>();
            return ActiveProfile.TryEvaluate(pitch,stance!=null?stance.Frame(pitch):default,out position);
        }
        public void Prepare(VacuumDefinition definition,string run)
        {
            if(released||!isActiveAndEnabled||definition==null||definition.ViewPrefab==null||!Guid.TryParseExact(run,"N",out _))
                throw new InvalidOperationException("A live issuer, authored view and current run are required.");
            if(prepared){if(run==RunId&&definition.TierId==IssuedTier)return;throw new InvalidOperationException("A prepared player cannot change run/tier in place.");}
            motor=GetComponent<PlayerMotor>();view=GetComponent<PlayerView>();animationView=GetComponent<PlayerAnimationView>();
            emitter=GetComponent<VacuumEmitter>();receiver=GetComponent<IntakeReceiver>();var input=GetComponent<PlayerInputReader>();
            if(emitter.Definition!=definition||view==null||animationView==null||motor.NozzleAnchor==null||motor.AuthoritativeAim==null||motor.NozzleAnchor.parent!=motor.AuthoritativeAim||
                (motor.NozzleAnchor.localScale-Vector3.one).sqrMagnitude>.000001f||receiver.Busy||input==null||input.enabled)
                throw new InvalidOperationException("Issue the tool before enabling player input, with no active ingestion.");
            var authored=definition.ViewPrefab.GetComponent<ToolTierView>();
            Validate(authored,definition.TierId);
            Transform nextContainer=new GameObject("IssuedToolPresentations").transform;
            nextContainer.SetParent(transform,false);nextContainer.gameObject.SetActive(false);
            try {
                var nextLocal=Instantiate(definition.ViewPrefab,nextContainer,false).transform;
                var nextRemote=Instantiate(definition.ViewPrefab,nextContainer,false).transform;
                nextLocal.name=definition.TierId+"_LocalTool";nextRemote.name=definition.TierId+"_RemoteTool";
                var l=nextLocal.GetComponent<ToolTierView>();var r=nextRemote.GetComponent<ToolTierView>();Validate(l,definition.TierId);Validate(r,definition.TierId);
                ActiveProfile=l.MountProfile;IssuedTier=definition.TierId;RunId=run;prepared=true;
                if(!motor.TryGetNozzleLocalPosition(0,out var mount))throw new InvalidOperationException("Selected tier has no verified shared physical/render mount.");
                var oldLocal=view.ViewModelRoot;var oldRemote=animationView.RemoteTool;
                motor.NozzleAnchor.localPosition=mount;motor.NozzleAnchor.localRotation=Quaternion.identity;
                foreach(var tool in new[]{nextLocal,nextRemote}){
                    tool.SetPositionAndRotation(motor.NozzleAnchor.position,motor.NozzleAnchor.rotation);tool.localScale=Vector3.one;
                    tool.GetComponent<VacuumView>().Receiver=receiver;
                }
                var nextEnd=new GameObject("IssuedPhysicalEndPoint").transform;
                nextEnd.SetParent(motor.NozzleAnchor,false);nextEnd.localPosition=l.transform.InverseTransformPoint(l.EndPoint.position);
                nextEnd.localRotation=Quaternion.identity;
                audioEmitter=GetComponent<VacuumAudioEmitter>();
                if(audioEmitter!=null)audioEmitter.StopAudio();emitter.Active=false;
                receiver.PresentationTarget=null;receiver.Target=motor.NozzleAnchor;receiver.VisualEndPoint=nextEnd;
                emitter.Source=motor.NozzleAnchor;emitter.OriginGuard=motor.AuthoritativeAim;
                local=nextLocal;remote=nextRemote;physicalEnd=nextEnd;container=nextContainer;
                view.ViewModelRoot=local;animationView.RemoteTool=remote;
                if(oldLocal!=null&&oldLocal!=local)oldLocal.gameObject.SetActive(false);
                if(oldRemote!=null&&oldRemote!=remote)oldRemote.gameObject.SetActive(false);
                nextLocal.gameObject.SetActive(false);nextRemote.gameObject.SetActive(false);nextContainer.gameObject.SetActive(true);
                view.Initialize(false); // Bind the new local model at its physical mount; owner is assigned by the adapter next.
                if(oldLocal!=null&&oldLocal!=local)Destroy(oldLocal.gameObject);
                if(oldRemote!=null&&oldRemote!=remote&&oldRemote!=oldLocal)Destroy(oldRemote.gameObject);
            } catch {
                if(container==nextContainer)Release();else {Destroy(nextContainer.gameObject);prepared=false;ActiveProfile=null;IssuedTier=RunId=null;}
                throw;
            }
        }
        private static void Validate(ToolTierView binding,string tier)
        {
            if(binding==null||binding.TierId!=tier||binding.Mouth==null||binding.EndPoint==null||
                binding.GetComponent<VacuumView>()==null||binding.GetComponentsInChildren<Collider>(true).Length!=0||
                binding.GetComponentsInChildren<Rigidbody>(true).Length!=0||
                binding.GetComponentsInChildren<AudioSource>(true).Length!=0||binding.GetComponentsInChildren<AudioListener>(true).Length!=0)
                throw new InvalidOperationException("An authored render-only tier view with mouth/end/deformer and no audio or physics components is required.");
            var visual=binding.GetComponent<VacuumView>();
            if(visual.NozzleVisual==null||visual.HoseSegments==null||visual.HoseSegments.Length==0)
                throw new InvalidOperationException("Authored nozzle and hose deformation bindings are required.");
            ValidateRadial(binding.transform,visual.NozzleVisual);
            foreach(var sleeve in visual.HoseSegments)ValidateRadial(binding.transform,sleeve);
            var grip=binding.GetComponent<VacuumGripAnchors>();
            if(grip==null||!grip.RegripDReady||grip.Left==null||grip.Right==null||
                binding.transform.InverseTransformPoint(binding.Mouth.position).sqrMagnitude>.000001f||
                (binding.transform.localScale-Vector3.one).sqrMagnitude>.000001f)
                throw new InvalidOperationException("Verified semantic mouth-origin and physical grip bindings are required.");
            if(tier!="mk1"&&(binding.MountProfile==null||!binding.MountProfile.NativePoseVerified||binding.MountProfile.TierId!=tier))
                throw new InvalidOperationException("MK2-MK4 require an explicitly verified authored mount profile.");
        }
        private static void ValidateRadial(Transform semantic,Transform radial)
        {
            // Each curved hose wrapper has its own authored tangent (+Z). Its local XY
            // plane is radial; aligning it with the mouth would reject the existing C28 hose.
            // Deformation must target an owned wrapper, never an imported mesh transform.
            if(radial==null||radial.parent!=semantic||
                radial.GetComponent<MeshFilter>()!=null||radial.GetComponent<Renderer>()!=null||
                radial.GetComponentsInChildren<Renderer>(true).Length==0||
                !((radial.localScale-Vector3.one).sqrMagnitude<=.000001f))
                throw new InvalidOperationException("Scale only owned authored XY wrappers containing visual children; preserve each hose tangent and imported mesh basis.");
        }
        public void Release()
        {
            if(released||(!prepared&&container==null))return;
            // Terminal detach closes the actual domain paths before any input/view callbacks can run.
            released=true;
            if(receiver!=null)receiver.enabled=false;
            if(emitter!=null){emitter.Active=false;emitter.enabled=false;emitter.Definition=null;}
            if(motor!=null){motor.SuspendForRecovery(motor.LastIntent);motor.enabled=false;}
            var input=GetComponent<PlayerInputReader>();
            try {if(input!=null){input.enabled=false;input.SetGameplayAvailable(false);}}
            finally {
            if(audioEmitter!=null){audioEmitter.StopAudio();audioEmitter.enabled=false;}
            if(receiver!=null){receiver.PresentationTarget=null;if(receiver.VisualEndPoint==physicalEnd)receiver.VisualEndPoint=null;}
            if(view!=null&&view.ViewModelRoot==local)view.ViewModelRoot=null;
            if(animationView!=null&&animationView.RemoteTool==remote)animationView.RemoteTool=null;
            // The local view reparents its model under the camera, so destroying the staging parent alone is insufficient.
            if(local!=null)Destroy(local.gameObject);if(remote!=null)Destroy(remote.gameObject);
            if(physicalEnd!=null)Destroy(physicalEnd.gameObject);if(container!=null)Destroy(container.gameObject);
            local=remote=physicalEnd=container=null;prepared=false;ActiveProfile=null;IssuedTier=RunId=null;
            }
        }
        private void OnDisable()=>Release();
        private void OnDestroy()=>Release();
    }
}
