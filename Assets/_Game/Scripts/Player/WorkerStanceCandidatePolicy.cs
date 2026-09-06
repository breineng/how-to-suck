using UnityEngine;

namespace HowToSuck
{
    // Presentation-only candidate. Caller owns the shared authoritative/visual mount blend.
    // The original D mesh, skeleton, nine clips and left bar contact are unchanged.
    public static class WorkerStanceCandidatePolicy
    {
        public readonly struct Frame
        {
            public readonly float Weight;
            public readonly Vector3 PelvisRootOffset, AdditionalMountAimOffset;
            public readonly float SpineReplacementRootXDegrees, ChestReplacementRootXDegrees;
            public readonly float ClavicleTargetElevationDegrees, ClavicleOverrideWeight;
            public readonly float ClavicleProtractionDegrees, RightRegripDegrees;
            public Frame(float weight, Vector3 pelvis, Vector3 mount, float spine, float chest,
                float elevation, float clavicleWeight, float protraction, float rightGrip)
            {
                Weight=weight; PelvisRootOffset=pelvis; AdditionalMountAimOffset=mount;
                SpineReplacementRootXDegrees=spine; ChestReplacementRootXDegrees=chest;
                ClavicleTargetElevationDegrees=elevation; ClavicleOverrideWeight=clavicleWeight;
                ClavicleProtractionDegrees=protraction; RightRegripDegrees=rightGrip;
            }
        }
        static float Smooth(float t) { t=Mathf.Clamp01(t); return t*t*(3f-2f*t); }
        public static Frame Evaluate(float fullPitchDegrees, float stanceWeight)
        {
            float w=Mathf.Clamp01(stanceWeight);
            float up=Smooth(-fullPitchDegrees/80f);
            float down=Smooth(fullPitchDegrees/80f);
            float aim=up+down;
            float bodyWeight=aim*w;
            Vector3 pelvis=Vector3.Lerp(new Vector3(0,.018f,-.10f),new Vector3(0,.028f,-.025f),up)*bodyWeight;
            Vector3 mount=(new Vector3(0,-.03f,.16f)*down+new Vector3(0,-.04f,.14f)*up)*w;
            return new Frame(bodyWeight,pelvis,mount,Mathf.Lerp(15,-2,up),Mathf.Lerp(-5,2,up),
                fullPitchDegrees<0?26:-25,aim*w,30*aim*w,
                Mathf.Lerp(30,-15,w)*Smooth((fullPitchDegrees-10)/50f));
        }
    }
}

