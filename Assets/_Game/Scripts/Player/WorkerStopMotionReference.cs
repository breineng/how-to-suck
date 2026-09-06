using System;
using UnityEngine;

namespace HowToSuck
{
    // Sampled from the unchanged imported D rig and its original Idle action in the Editor.
    public sealed class WorkerStopMotionReference : ScriptableObject
    {
        public string ModelSha256;
        public Bone[] Bones;
        public Foot Left, Right;
        [Serializable] public sealed class Bone
        {
            public string Name, Parent;
            public Vector3 IdleLocalPosition, IdleLocalScale, IdlePosition, RestPosition, RestAxis;
            public Quaternion IdleLocalRotation, IdleRotation, RestRotation;
        }
        [Serializable] public sealed class Foot
        {
            public int Thigh, Calf, Ankle, Support;
            public Quaternion NeutralRotation;
            public Vector3[] SoleNeutralPoints;
        }
    }
}
