using UnityEngine;
namespace HowToSuck
{
    [DisallowMultipleComponent]
    public sealed class VacuumView : MonoBehaviour
    {
        public IntakeReceiver Receiver;
        public Transform NozzleVisual;
        public Transform[] HoseSegments=System.Array.Empty<Transform>();
        private Vector3 nozzleScale;
        private Vector3[] segmentScales;
        private void Awake()
        {
            nozzleScale=NozzleVisual!=null?NozzleVisual.localScale:Vector3.one;
            segmentScales=new Vector3[HoseSegments.Length];for(int i=0;i<HoseSegments.Length;i++)if(HoseSegments[i]!=null)segmentScales[i]=HoseSegments[i].localScale;
        }
        private void LateUpdate()
        {
            var snapshot=Receiver!=null?Receiver.Current:null;
            if(snapshot==null){Restore();return;}
            float t=snapshot.Progress(Time.realtimeSinceStartupAsDouble);
            float peak=Mathf.Clamp(1+snapshot.RequiredSize*1.5f,1.2f,3.5f);
            float expand=t<.2f?Mathf.SmoothStep(1,peak,t/.2f):Mathf.Lerp(peak,1,Mathf.SmoothStep(0,1,Mathf.InverseLerp(.65f,1,t)));
            if(NozzleVisual!=null)NozzleVisual.localScale=Vector3.Scale(nozzleScale,new Vector3(expand,expand,1));
            for(int i=0;i<HoseSegments.Length;i++)if(HoseSegments[i]!=null)
            {
                float centre=Mathf.Lerp(.64f,.88f,i/(float)Mathf.Max(1,HoseSegments.Length-1));
                float wave=Mathf.Exp(-Mathf.Pow((t-centre)/.11f,2));
                float pulse=1+wave*Mathf.Min(1.1f,snapshot.RequiredSize);
                HoseSegments[i].localScale=Vector3.Scale(segmentScales[i],new Vector3(pulse,pulse,1));
            }
        }
        private void Restore()
        {
            if(NozzleVisual!=null)NozzleVisual.localScale=nozzleScale;
            if(segmentScales!=null)for(int i=0;i<HoseSegments.Length;i++)if(HoseSegments[i]!=null)HoseSegments[i].localScale=segmentScales[i];
        }
        private void OnDisable()=>Restore();
    }
}