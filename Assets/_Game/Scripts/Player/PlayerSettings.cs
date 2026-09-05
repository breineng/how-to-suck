using System;
using UnityEngine;

namespace HowToSuck
{
    [Serializable]
    public sealed class PlayerSettings
    {
        [Min(0f)] public float WalkSpeed = 4.5f;
        [Min(0f)] public float SprintSpeed = 7f;
        [Min(0f)] public float JumpHeight = 1f;
        public float Gravity = -22f;
        [Min(0.1f)] public float CapsuleHeight = 1.8f;
        [Min(0.01f)] public float CapsuleRadius = 0.3f;
        [Min(0f)] public float StepOffset = 0.3f;
        [Range(0f, 89f)] public float SlopeLimit = 45f;
        [Min(0f)] public float EyeHeight = 1.62f;
        [Range(-.1f,.2f)] public float CameraForwardOffset = 0f;
        [Range(40f, 110f)] public float FieldOfView = 75f;
    }
}
