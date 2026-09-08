using UnityEngine;
using UnityEngine.Rendering;

namespace HowToSuck
{
    // The server replicates downed/revived state and the standing location. Bone
    // physics is presentation on each peer; it cannot push cargo or cause damage.
    [DefaultExecutionOrder(13000), DisallowMultipleComponent]
    public sealed class PlayerRagdollView : MonoBehaviour
    {
        public PlayerMotor Motor;
        public PlayerAnimationView Animation;
        public Rigidbody[] Bodies;
        public Collider[] Colliders;
        public Transform[] Bones;
        public Rigidbody Pelvis;
        Vector3[] restPositions,fallenPositions,animatedPositions;
        Quaternion[] restRotations,fallenRotations,animatedRotations;
        bool fallen,blending,blendPoseApplied,physicsFrozen;
        float standAt;
        Vector3 cameraPosition;
        Quaternion cameraRotation;
        Camera downCamera;
        bool cameraApplied;
        public bool IsRagdollActive => fallen;

        void Awake()
        {
            int n=Bones.Length;restPositions=new Vector3[n];restRotations=new Quaternion[n];
            fallenPositions=new Vector3[n];fallenRotations=new Quaternion[n];animatedPositions=new Vector3[n];animatedRotations=new Quaternion[n];
            for(int i=0;i<n;i++){restPositions[i]=Bones[i].localPosition;restRotations[i]=Bones[i].localRotation;}
            foreach(var c in Colliders){c.enabled=false;c.excludeLayers=~LayerMask.GetMask("World");c.includeLayers=LayerMask.GetMask("World");}
            SetPhysics(false);
        }
        void Update()
        {
            RestoreCamera();
            if(blendPoseApplied)
            {
                for(int i=0;i<Bones.Length;i++)Bones[i].SetLocalPositionAndRotation(animatedPositions[i],animatedRotations[i]);
                blendPoseApplied=false;
            }
            if(Motor==null||Animation==null)return;
            if(Motor.IsDowned&&!fallen)Fall();
            else if(!Motor.IsDowned&&fallen)Stand();
            if(fallen&&Animation.World!=null)
            {
                bool freeze=!Animation.World.IsRunning;
                if(freeze!=physicsFrozen){physicsFrozen=freeze;SetPhysics(!freeze);}
            }
        }
        void Fall()
        {
            fallen=true;blending=false;physicsFrozen=false;
            Animation.enabled=false;Animation.Animator.enabled=false;
            if(Animation.RemoteTool!=null)Animation.RemoteTool.gameObject.SetActive(false);
            if(Animation.Head!=null){Animation.Head.enabled=true;Animation.Head.shadowCastingMode=ShadowCastingMode.On;}
            SetPhysics(true);
            Vector3 direction=-transform.forward*.9f+Vector3.up*.12f;
            foreach(var body in Bodies)body.linearVelocity=direction;
            if(Pelvis!=null)Pelvis.AddTorque(transform.right*2.5f,ForceMode.VelocityChange);
        }
        void Stand()
        {
            fallen=false;SetPhysics(false);
            for(int i=0;i<Bones.Length;i++)
            {
                fallenPositions[i]=Bones[i].localPosition;fallenRotations[i]=Bones[i].localRotation;
                Bones[i].SetLocalPositionAndRotation(restPositions[i],restRotations[i]);
            }
            Animation.Animator.enabled=true;Animation.Animator.Rebind();Animation.Animator.Update(0);
            Animation.enabled=true;standAt=Time.unscaledTime;blending=true;
        }
        void SetPhysics(bool active)
        {
            foreach(var body in Bodies)
            {
                if(!body.isKinematic){body.linearVelocity=Vector3.zero;body.angularVelocity=Vector3.zero;}
                body.isKinematic=!active;body.useGravity=active;
                body.collisionDetectionMode=CollisionDetectionMode.ContinuousSpeculative;
            }
            foreach(var c in Colliders)c.enabled=active;
        }
        void LateUpdate()
        {
            if(blending)
            {
                float t=Mathf.SmoothStep(0,1,(Time.unscaledTime-standAt)/.65f);
                for(int i=0;i<Bones.Length;i++)
                {
                    animatedPositions[i]=Bones[i].localPosition;animatedRotations[i]=Bones[i].localRotation;
                    Bones[i].SetLocalPositionAndRotation(Vector3.Lerp(fallenPositions[i],animatedPositions[i],t),Quaternion.Slerp(fallenRotations[i],animatedRotations[i],t));
                }
                blendPoseApplied=true;if(t>=1)blending=false;
            }
            if(!fallen&&!blending)return;
            var view=Animation.View;if(view==null||!view.IsLocal||view.Camera==null)return;
            var camera=view.Camera;cameraPosition=camera.transform.localPosition;cameraRotation=camera.transform.localRotation;
            downCamera=camera;cameraApplied=true;
            Vector3 focus=fallen&&Pelvis!=null?Pelvis.position+Vector3.up*.2f:transform.position+Vector3.up*.55f;
            float yaw=view.Input!=null?view.Input.LatestIntent.Yaw:transform.eulerAngles.y;
            Vector3 offset=Quaternion.Euler(0,yaw,0)*new Vector3(.25f,1.1f,-2.4f);
            if(Physics.SphereCast(focus,.16f,offset.normalized,out var hit,offset.magnitude,LayerMask.GetMask("World"),QueryTriggerInteraction.Ignore))offset=offset.normalized*Mathf.Max(.2f,hit.distance-.05f);
            float stand=fallen?0:Mathf.SmoothStep(0,1,(Time.unscaledTime-standAt)/.65f);
            camera.transform.SetPositionAndRotation(Vector3.Lerp(focus+offset,camera.transform.position,stand),
                Quaternion.Slerp(Quaternion.LookRotation(-offset),camera.transform.rotation,stand));
            if(blending&&view.ViewModelRoot!=null)view.ViewModelRoot.gameObject.SetActive(false);
        }
        void RestoreCamera()
        {
            if(cameraApplied&&downCamera!=null)downCamera.transform.SetLocalPositionAndRotation(cameraPosition,cameraRotation);
            cameraApplied=false;
        }
        void OnDisable(){RestoreCamera();if(Bodies!=null)SetPhysics(false);}
    }
}
