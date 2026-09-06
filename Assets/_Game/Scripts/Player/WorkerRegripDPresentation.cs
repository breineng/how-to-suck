using System;
using UnityEngine;
namespace HowToSuck
{
    [Serializable] public sealed class WorkerRegripDBindings
    {
        public bool Enabled;
        public Transform Pelvis, NeckBone, HeadBone;
        public Vector3 IdleSpineAxis, IdleChestAxis;
        public float IdlePelvisHeight;
    }
    public enum WorkerGripStatus { Legacy, NoTool, Applied, Failed }
    // Explicit presentation calls inside PlayerAnimationView's single saved-local-pose lifecycle.
    public static class WorkerRegripDPresentation
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // At most one extra record per actual Arm lifetime. Weak keys do not retain despawned rigs.
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<PlayerAnimationView.Arm,object> policyWitnesses = new System.Runtime.CompilerServices.ConditionalWeakTable<PlayerAnimationView.Arm,object>();
        [Serializable] private sealed class NativePolicyFailure
        {
            public int frame,playerId,motorInstanceId,viewInstanceId,stateHash,nextStateHash;
            public string utc,run,rootName,clips,stopStatus,stopFailure;
            public ulong worldTick; public uint intentSequence,resetRevision;
            public bool left,ownerFound,local,authority,grounded,worldRunning,transition,stopEnabled,stopActive;
            public double engineTime,realtime,stopElapsed,stopDuration;
            public float pitch,deltaTime,intentYaw,intentPitch,planarSpeed,upperLength,lowerLength,normalizedTime,nextNormalizedTime;
            public Vector2 intentMove;
            public Vector3 shoulderWorld,elbowWorld,handWorld,goalWorld,contactWorld,heldAxisWorld;
            public Vector3 shoulderRoot,elbowRoot,goalRoot,heldAxisRoot,rootPosition,rootScale,motorPosition,renderPosition,pelvisShift,toolPosition;
            public Quaternion rootRotation,toolRotation;
        }
        private static void RecordPolicyFailure(PlayerAnimationView.Arm arm,Transform root,VacuumGripAnchors anchors,float pitch,bool left,
            Vector3 a,Vector3 b,Vector3 c,Vector3 goal,Vector3 contact,Vector3 heldAxis,float upper,float lower)
        {
            if(policyWitnesses.TryGetValue(arm,out _))return;
            policyWitnesses.Add(arm,new object());
            try {
                var view=root.GetComponentInParent<PlayerAnimationView>();var motor=view!=null?view.Motor:null;
                var animator=view!=null?view.Animator:null;var stop=view!=null?view.StopMotion:null;var world=view!=null?view.World:null;
                var state=animator!=null?animator.GetCurrentAnimatorStateInfo(0):default;var next=animator!=null?animator.GetNextAnimatorStateInfo(0):default;
                string clips="";if(animator!=null)foreach(var clip in animator.GetCurrentAnimatorClipInfo(0))clips+=clip.clip.name+":"+clip.weight.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+" ";
                var intent=motor!=null?motor.LastIntent:default;
                var row=new NativePolicyFailure {
                    frame=Time.frameCount,utc=DateTime.UtcNow.ToString("O"),engineTime=Time.timeAsDouble,realtime=Time.realtimeSinceStartupAsDouble,
                    playerId=motor!=null?motor.PlayerId:0,motorInstanceId=motor!=null?motor.GetInstanceID():0,viewInstanceId=view!=null?view.GetInstanceID():0,
                    ownerFound=view!=null,local=view!=null&&view.View!=null&&view.View.IsLocal,authority=motor!=null&&motor.HasMovementAuthority,
                    run=world!=null?world.RunId:intent.RunId,worldTick=world!=null?world.TickCount:0,worldRunning=world!=null&&world.IsRunning,
                    left=left,pitch=pitch,deltaTime=Time.deltaTime,grounded=motor!=null&&motor.IsGrounded,planarSpeed=motor!=null?motor.PlanarSpeed:0,
                    intentSequence=intent.Sequence,intentYaw=intent.Yaw,intentPitch=intent.Pitch,intentMove=intent.Move,resetRevision=motor!=null?motor.PresentationResetRevision:0,
                    stateHash=state.shortNameHash,nextStateHash=next.shortNameHash,normalizedTime=state.normalizedTime,nextNormalizedTime=next.normalizedTime,
                    transition=animator!=null&&animator.IsInTransition(0),clips=clips,
                    stopEnabled=stop!=null&&stop.Enabled,stopActive=stop!=null&&stop.Active,stopStatus=stop!=null?stop.Status:null,stopFailure=stop!=null?stop.Failure:null,
                    stopElapsed=stop!=null?stop.Elapsed:0,stopDuration=stop!=null?stop.Duration:0,pelvisShift=stop!=null?stop.PelvisAdditionalShift:Vector3.zero,
                    shoulderWorld=a,elbowWorld=b,handWorld=c,goalWorld=goal,contactWorld=contact,heldAxisWorld=heldAxis,upperLength=upper,lowerLength=lower,
                    shoulderRoot=root.InverseTransformPoint(a),elbowRoot=root.InverseTransformPoint(b),goalRoot=root.InverseTransformPoint(goal),heldAxisRoot=root.InverseTransformDirection(heldAxis),
                    rootName=root.name,rootPosition=root.position,rootRotation=root.rotation,rootScale=root.lossyScale,
                    motorPosition=motor!=null?motor.transform.position:Vector3.zero,renderPosition=motor!=null?motor.GetRenderPosition():Vector3.zero,
                    toolPosition=anchors.transform.position,toolRotation=anchors.transform.rotation
                };
                Debug.Log("RegripD.PolicyFailure "+JsonUtility.ToJson(row),root);
            } catch(Exception error) {
                // The witness must never replace the original policy failure or its restore path.
                Debug.Log("RegripD.PolicyWitnessUnavailable "+error.GetType().Name,root);
            }
        }
        [Serializable] private sealed class NativeSolveFailure
        {
            public int frame,stateHash,nextStateHash;public bool left,transition;public string clips;
            public float pitch,yaw,deltaTime,normalizedTime,nextNormalizedTime,wristError,upperBefore,upperAfter,lowerBefore,lowerAfter;
            public Vector3 shoulder,sourceElbow,sourceHand,goal,actualHand,upperFrom,upperTo,lowerFrom,lowerTo;
            public Quaternion upperSwing,lowerSwing;
        }
#endif
        private static ArmPoint P(Vector3 v)=>new ArmPoint(v.x,v.y,v.z);
        private static Vector3 V(ArmPoint p)=>new Vector3((float)p.X,(float)p.Y,(float)p.Z);
        private static bool F(float v)=>!float.IsNaN(v)&&!float.IsInfinity(v);
        private static bool F(Vector3 v)=>F(v.x)&&F(v.y)&&F(v.z);
        private static void Need(bool condition,string message){if(!condition)throw new InvalidOperationException("RegripD: "+message);}
        private static void Unit(Vector3 axis,string name)=>Need(F(axis)&&Mathf.Abs(axis.sqrMagnitude-1)<.001f,"Authored unit "+name+" axis required");
        private static void Rotation(Quaternion q,string name)=>Need(F(q.x)&&F(q.y)&&F(q.z)&&F(q.w)&&Mathf.Abs(q.x*q.x+q.y*q.y+q.z*q.z+q.w*q.w-1)<.001f,"Authored unit "+name+" rotation required");
        // Only canonicalize finite floating-point recovery error immediately outside authored endpoints.
        // Real pitch beyond the narrow0.0001-degree band still fails; frozen policies stay strict.
        public static float CanonicalRecoveredPitch(float pitch)
        {
            Need(F(pitch),"Nonfinite rendered pitch");
            if(pitch>80f&&pitch<=80f+.0001f)return 80f;
            if(pitch< -80f&&pitch>=-80f-.0001f)return -80f;
            Need(pitch>=-80f&&pitch<=80f,"Rendered pitch is outside authored +/-80-degree range");
            return pitch;
        }
        public static float ReadRenderedPitch(Quaternion aim)
        {
            Rotation(aim,"rendered aim");
            return CanonicalRecoveredPitch(WorkerHeadLookPresentation.PitchFromRenderedAim(aim));
        }
        public static void ApplyTorso(PlayerAnimationView view,float pitch,WorkerStanceCandidatePolicy.Frame stance=default)
        {
            var d=view.RegripD;var root=view.VisualRoot;
            Need(d!=null&&d.Pelvis!=null&&d.NeckBone!=null&&d.HeadBone!=null&&view.Spine!=null&&view.Chest!=null,"Missing torso/rest bindings");
            Need(F(d.IdlePelvisHeight)&&F(d.IdleSpineAxis)&&F(d.IdleChestAxis)&&d.IdleSpineAxis.sqrMagnitude>.001f&&d.IdleChestAxis.sqrMagnitude>.001f,"Missing sampled Idle reference axes");
            Need((root.lossyScale-Vector3.one).sqrMagnitude<.000001f,"Unit semantic visual root required");
            Vector3 spine=root.InverseTransformPoint(view.Spine.position),chest=root.InverseTransformPoint(view.Chest.position),neck=root.InverseTransformPoint(d.NeckBone.position);
            float drop=d.IdlePelvisHeight-root.InverseTransformPoint(d.Pelvis.position).y;
            Need(NozzleAimMountPolicy.TryTorsoPoint(pitch,drop,P(spine),P(chest),P(chest),P(neck),P(d.IdleSpineAxis),P(d.IdleChestAxis),P(chest),out var nextChest),"Torso policy rejected chest input");
            Need(NozzleAimMountPolicy.TryTorsoPoint(pitch,drop,P(spine),P(chest),P(chest),P(neck),P(d.IdleSpineAxis),P(d.IdleChestAxis),P(neck),out var nextNeck),"Torso policy rejected neck input");
            Quaternion headBefore=d.HeadBone.rotation;
            // Frozen D torso is exactly root-X rotation, not a shortest-arc 3D swing.
            Vector3 nextSpineAxis=V(nextChest)-spine;
            float spineAngle=(Mathf.Atan2(nextSpineAxis.z,nextSpineAxis.y)-Mathf.Atan2(chest.z-spine.z,chest.y-spine.y))*Mathf.Rad2Deg;
            view.Spine.rotation=Quaternion.AngleAxis(spineAngle,root.right)*view.Spine.rotation;
            Vector3 afterChest=root.InverseTransformPoint(view.Chest.position),afterNeck=root.InverseTransformPoint(d.NeckBone.position);
            Vector3 currentChestAxis=afterNeck-afterChest,nextChestAxis=V(nextNeck)-afterChest;
            float chestAngle=(Mathf.Atan2(nextChestAxis.z,nextChestAxis.y)-Mathf.Atan2(currentChestAxis.z,currentChestAxis.y))*Mathf.Rad2Deg;
            view.Spine.rotation=Quaternion.AngleAxis(Mathf.Lerp(spineAngle,stance.SpineReplacementRootXDegrees,stance.Weight)-spineAngle,root.right)*view.Spine.rotation;
            view.Chest.rotation=Quaternion.AngleAxis(Mathf.Lerp(chestAngle,stance.ChestReplacementRootXDegrees,stance.Weight),root.right)*view.Chest.rotation;
            // D stabilization preserves the input head world orientation; HeadLook is added afterwards.
            d.HeadBone.rotation=headBefore;
        }
        private static void PreserveArmReach(PlayerAnimationView.Arm arm,Vector3 goal)
        {
            // A moving pelvis can exhaust the arm reach while the hand remains on
            // its physical grip. Rotate the existing clavicle the smallest amount
            // toward that grip before solving the two unchanged arm segments.
            Vector3 pivot=arm.Clavicle.position,shoulder=arm.Upper.position;
            float upper=Vector3.Distance(shoulder,arm.Forearm.position),lower=Vector3.Distance(arm.Forearm.position,arm.Hand.position);
            float reach=upper+lower-.001f;
            if(Vector3.Distance(shoulder,goal)<=reach)return;
            Vector3 source=shoulder-pivot,toward=goal-pivot;
            double clavicle=source.magnitude,distance=toward.magnitude;
            Need(clavicle>1e-5&&distance>1e-5&&Math.Abs(distance-clavicle)<reach,"Physical grip is outside combined clavicle and arm reach");
            double cosine=(clavicle*clavicle+distance*distance-reach*reach)/(2*clavicle*distance);
            double limit=Math.Acos(Math.Clamp(cosine,-1,1));
            var from=source.normalized;var to=toward.normalized;
            double current=Math.Atan2(Vector3.Cross(from,to).magnitude,Vector3.Dot(from,to));
            var direction=Vector3.RotateTowards(from,to,(float)Math.Max(0,current-limit),0);
            arm.Clavicle.rotation=RegripRotation.FromTo(source,direction*(float)clavicle)*arm.Clavicle.rotation;
        }
        public static float ApplyArm(PlayerAnimationView.Arm arm,Transform marker,VacuumGripAnchors anchors,Transform root,float pitch,bool left,WorkerStanceCandidatePolicy.Frame stance=default)
        {
            Need(arm!=null&&arm.Clavicle!=null&&arm.Upper!=null&&arm.Forearm!=null&&arm.Hand!=null&&arm.Socket!=null&&arm.ElbowSupport!=null&&arm.ShoulderSupport!=null,"Missing actual arm/support bones");
            Need(anchors!=null&&anchors.RegripDReady&&marker!=null,"Final D tool/bar bindings required");
            Unit(arm.ForearmLocalAxis,"forearm local");Unit(arm.HandLocalAxis,"hand local");Rotation(arm.RestHandRootRotation,"rest hand");Rotation(arm.RestForearmRootRotation,"rest forearm");Rotation(arm.HandRelativeTool,"Idle held hand");
            Vector3 barAxis=left?anchors.LeftBarAxis:anchors.RightBarAxis;Unit(barAxis,"physical bar");
            Vector3 barCentre=left?anchors.LeftBarCentre:anchors.RightBarCentre;Need(F(barCentre),"Finite physical bar centre required");
            Quaternion tool=anchors.transform.rotation;
            float regrip=!left&&stance.Weight>0?stance.RightRegripDegrees:(float)NozzleAimMountPolicy.RegripAngleDegrees(pitch,left);
            var delta=Quaternion.AngleAxis(regrip,tool*barAxis);
            Rotation(delta,"regrip");
            Vector3 centre=anchors.transform.TransformPoint(barCentre);
            // Marker is from the FINAL exported tool and already includes the left52.5mm shift.
            // Rotate it about the unchanged physical bar axis; never call TryRegripPoint on that shifted marker.
            Vector3 contact=centre+delta*(marker.position-centre);
            Quaternion hand=delta*tool*arm.HandRelativeTool;
            Vector3 socketOffset=arm.Hand.InverseTransformPoint(arm.Socket.position);
            Vector3 goal=contact-hand*Vector3.Scale(socketOffset,arm.Hand.lossyScale);
            Need(F(goal)&&F(contact),"Finite physical contact/wrist required");
            Quaternion oldClavicle=arm.Clavicle.rotation,oldUpper=arm.Upper.rotation,oldForearm=arm.Forearm.rotation;
            Quaternion oldShoulder=arm.ShoulderSupport.rotation,oldElbow=arm.ElbowSupport.rotation;
            Vector3 clavicle=root.InverseTransformPoint(arm.Clavicle.position),shoulder=root.InverseTransformPoint(arm.Upper.position);
            Need(NozzleAimMountPolicy.TryClavicleEndpoint(pitch,P(clavicle),P(shoulder),out var raised),"Clavicle policy rejected source pose");
            arm.Clavicle.rotation=RegripRotation.FromTo(arm.Upper.position-arm.Clavicle.position,root.TransformPoint(WorkerIdleStancePresentation.ClavicleEndpoint(clavicle,shoulder,V(raised),stance,left))-arm.Clavicle.position)*arm.Clavicle.rotation;
            PreserveArmReach(arm,goal);
            Vector3 a=arm.Upper.position,b=arm.Forearm.position,c=arm.Hand.position;
            float upper=Vector3.Distance(a,b),lower=Vector3.Distance(b,c);
            Vector3 heldAxis=hand*arm.HandLocalAxis;
            bool policyAccepted=RegripArmPolicy.TryElbow(pitch,left,P(root.InverseTransformPoint(a)),P(root.InverseTransformPoint(b)),P(root.InverseTransformPoint(goal)),P(root.InverseTransformDirection(heldAxis)),upper,lower,out var solved);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if(!policyAccepted)RecordPolicyFailure(arm,root,anchors,pitch,left,a,b,c,goal,contact,heldAxis,upper,lower);
#endif
            Need(policyAccepted,"Unreachable/degenerate existing-length arm; no clamp or stretch");
            Vector3 elbow=root.TransformPoint(V(solved));
            Vector3 nativeUpperFrom=b-a,nativeUpperTo=elbow-a;
            Quaternion nativeUpperSwing=RegripRotation.FromTo(nativeUpperFrom,nativeUpperTo);
            arm.Upper.rotation=nativeUpperSwing*arm.Upper.rotation;
            Vector3 nativeLowerFrom=arm.Hand.position-arm.Forearm.position,nativeLowerTo=goal-arm.Forearm.position;
            Quaternion nativeLowerSwing=RegripRotation.FromTo(nativeLowerFrom,nativeLowerTo);
            arm.Forearm.rotation=nativeLowerSwing*arm.Forearm.rotation;
            float weight=(float)RegripArmPolicy.PoleAndForearmRollWeight(pitch);
            if(weight>0){
                Quaternion preferred=hand*Quaternion.Inverse(arm.RestHandRootRotation)*arm.RestForearmRootRotation;
                Vector3 axis=(arm.Hand.position-arm.Forearm.position).normalized;
                preferred=RegripRotation.FromTo(preferred*arm.ForearmLocalAxis,axis)*preferred;
                arm.Forearm.rotation=Quaternion.Slerp(arm.Forearm.rotation,preferred,weight);
            }
            Quaternion upperDelta=arm.Upper.rotation*Quaternion.Inverse(oldUpper),lowerDelta=arm.Forearm.rotation*Quaternion.Inverse(oldForearm),clavicleDelta=arm.Clavicle.rotation*Quaternion.Inverse(oldClavicle);
            arm.ShoulderSupport.position=arm.Upper.position;arm.ShoulderSupport.rotation=Quaternion.Slerp(clavicleDelta,upperDelta,.5f)*oldShoulder;
            arm.ElbowSupport.position=arm.Forearm.position;arm.ElbowSupport.rotation=Quaternion.Slerp(upperDelta,lowerDelta,.5f)*oldElbow;
            arm.Hand.rotation=hand;
            float nativeWristError=Vector3.Distance(arm.Hand.position,goal),nativeUpperAfter=Vector3.Distance(arm.Upper.position,arm.Forearm.position),nativeLowerAfter=Vector3.Distance(arm.Forearm.position,arm.Hand.position);
            if(!(nativeWristError<=.0002f&&Mathf.Abs(nativeUpperAfter-upper)<=.0002f&&Mathf.Abs(nativeLowerAfter-lower)<=.0002f)){
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                var owner=root.GetComponentInParent<PlayerAnimationView>();var animator=owner!=null?owner.Animator:null;
                var state=animator!=null?animator.GetCurrentAnimatorStateInfo(0):default;var next=animator!=null?animator.GetNextAnimatorStateInfo(0):default;string clipNames="";
                if(animator!=null)foreach(var clip in animator.GetCurrentAnimatorClipInfo(0))clipNames+=clip.clip.name+":"+clip.weight.ToString("R")+" ";
                var witness=new NativeSolveFailure{frame=Time.frameCount,left=left,pitch=pitch,yaw=root.eulerAngles.y,deltaTime=Time.deltaTime,stateHash=state.shortNameHash,nextStateHash=next.shortNameHash,normalizedTime=state.normalizedTime,nextNormalizedTime=next.normalizedTime,transition=animator!=null&&animator.IsInTransition(0),clips=clipNames,wristError=nativeWristError,upperBefore=upper,upperAfter=nativeUpperAfter,lowerBefore=lower,lowerAfter=nativeLowerAfter,shoulder=a,sourceElbow=b,sourceHand=c,goal=goal,actualHand=arm.Hand.position,upperFrom=nativeUpperFrom,upperTo=nativeUpperTo,lowerFrom=nativeLowerFrom,lowerTo=nativeLowerTo,upperSwing=nativeUpperSwing,lowerSwing=nativeLowerSwing};
                throw new InvalidOperationException("RegripD.NativeSolveFailure "+JsonUtility.ToJson(witness));
#else
                throw new InvalidOperationException("RegripD: Native quaternion solve changed length or missed wrist by more0.2mm");
#endif
            }
            Need(Vector3.Distance(arm.Socket.position,contact)<=.001f,"Actual final socket misses physical bar by more than1mm");
            return Vector3.Distance(arm.Socket.position,contact);
        }
    }
}
