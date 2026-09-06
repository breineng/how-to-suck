using System;
using System.Collections.Generic;
using UnityEngine;
namespace HowToSuck
{
    [DisallowMultipleComponent]
    public sealed class VacuumView : MonoBehaviour
    {
        public IntakeReceiver Receiver;
        public Transform NozzleVisual;
        [Min(.01f)] public float DeformationSizeReference=1f;
        [Min(0)] public int MouthFollowSegments;
        [Min(0)] public float VisualBottomAnchorRadius;
        public bool ContinuousTruckTube;
        public Transform[] HoseSegments=Array.Empty<Transform>();
        private Vector3 nozzleScale,nozzlePosition;
        private Vector3[] segmentScales,segmentPositions;
        private TruckTubeDeformer tube;
        private void Awake()
        {
            nozzleScale=NozzleVisual!=null?NozzleVisual.localScale:Vector3.one;
            nozzlePosition=NozzleVisual!=null?NozzleVisual.localPosition:Vector3.zero;
            segmentScales=new Vector3[HoseSegments.Length];segmentPositions=new Vector3[HoseSegments.Length];
            for(int i=0;i<HoseSegments.Length;i++)if(HoseSegments[i]!=null){segmentScales[i]=HoseSegments[i].localScale;segmentPositions[i]=HoseSegments[i].localPosition;}
            if(ContinuousTruckTube)
            {
                try
                {
                    var meshes=new List<MeshFilter>();
                    foreach(var segment in HoseSegments)if(segment!=null)meshes.AddRange(segment.GetComponentsInChildren<MeshFilter>(true));
                    tube=new TruckTubeDeformer(transform,meshes,TruckTubeProfile.AuthoredTruck());
                }
                catch(Exception error){FailTube(error);}
            }
        }
        private void LateUpdate()
        {
            var snapshot=Receiver!=null?Receiver.Current:null;
            if(snapshot==null){Restore();return;}
            float t=snapshot.Progress(snapshot.PresentationNow);
            float size=snapshot.RequiredSize/Mathf.Max(.01f,DeformationSizeReference);
            float peak=Mathf.Clamp(1+size*1.5f,1.2f,3.5f);
            float expand=t<.2f?Mathf.SmoothStep(1,peak,t/.2f):Mathf.Lerp(peak,1,Mathf.SmoothStep(0,1,Mathf.InverseLerp(.65f,1,t)));
            if(NozzleVisual!=null)Deform(NozzleVisual,nozzleScale,nozzlePosition,expand);
            if(ContinuousTruckTube)
            {
                try{if(tube==null)throw new InvalidOperationException("Truck tube was not initialized.");tube.Apply(t,expand,size);}
                catch(Exception error){FailTube(error);}
                return;
            }
            for(int i=0;i<HoseSegments.Length;i++)if(HoseSegments[i]!=null)
            {
                float centre=Mathf.Lerp(.64f,.88f,i/(float)Mathf.Max(1,HoseSegments.Length-1));
                float wave=Mathf.Exp(-Mathf.Pow((t-centre)/.11f,2));
                float pulse=1+wave*Mathf.Min(1.1f,size);
                float weight=i<MouthFollowSegments?1-i/(float)MouthFollowSegments:0;
                float follow=1+(expand-1)*weight;
                Deform(HoseSegments[i],segmentScales[i],segmentPositions[i],Mathf.Max(follow,pulse));
            }
        }
        private void FailTube(Exception error)
        {
            // Any validation/deformation failure restores the complete owned view once.
            tube?.Dispose();tube=null;Restore();enabled=false;
            Debug.LogException(new InvalidOperationException("Truck tube presentation disabled: "+error.Message,error),this);
        }
        private void Deform(Transform part,Vector3 scale,Vector3 position,float radial)
        {
            part.localScale=Vector3.Scale(scale,new Vector3(radial,radial,1));
            part.localPosition=position+Vector3.up*((radial-1)*VisualBottomAnchorRadius);
        }
        private void Restore()
        {
            tube?.Restore();
            if(NozzleVisual!=null){NozzleVisual.localScale=nozzleScale;NozzleVisual.localPosition=nozzlePosition;}
            if(segmentScales!=null)for(int i=0;i<HoseSegments.Length;i++)if(HoseSegments[i]!=null){HoseSegments[i].localScale=segmentScales[i];HoseSegments[i].localPosition=segmentPositions[i];}
        }
        private void OnDisable()=>Restore();
        private void OnDestroy(){tube?.Dispose();tube=null;}
    }
}