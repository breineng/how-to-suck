using System;
using UnityEngine;

namespace HowToSuck
{
    // Menu-only presentation: the gameplay cargo, inputs and physics remain independently owned.
    public sealed class MenuBSharedSuctionMotion : MonoBehaviour
    {
        [Serializable]
        public sealed class Stream
        {
            public ParticleSystem Particles;
            public Transform Nozzle;
            public Vector3 CargoLocalPoint;
            public float Phase;
            [NonSerialized] public ParticleSystem.Particle[] Buffer;
        }

        public Animator PhaseAnimator;
        public Transform Piano;
        public Vector3 RestPosition;
        public Vector3 RestEuler;
        public Stream[] Streams = Array.Empty<Stream>();

        private void LateUpdate()
        {
            if (PhaseAnimator == null || Piano == null || !PhaseAnimator.isActiveAndEnabled) return;
            float phase = Mathf.Repeat(PhaseAnimator.GetCurrentAnimatorStateInfo(0).normalizedTime, 1f);
            float angle = phase * Mathf.PI * 2f;
            Piano.localPosition = RestPosition + new Vector3(.035f * Mathf.Sin(angle), .085f * Mathf.Sin(angle * 2f), .025f * Mathf.Sin(angle));
            Piano.localRotation = Quaternion.Euler(RestEuler + new Vector3(1.8f * Mathf.Sin(angle), 2.5f * Mathf.Sin(angle), 3.3f * Mathf.Sin(angle * 2f)));
            foreach (var stream in Streams)
            {
                if (stream == null || stream.Particles == null || stream.Nozzle == null) continue;
                const int count = 24;
                if (stream.Buffer == null || stream.Buffer.Length != count) stream.Buffer = new ParticleSystem.Particle[count];
                Vector3 start = Piano.TransformPoint(stream.CargoLocalPoint), end = stream.Nozzle.position;
                Vector3 direction = end - start;
                Vector3 side = Vector3.Cross(direction.normalized, Vector3.up).normalized;
                if (side.sqrMagnitude < .1f) side = Vector3.right;
                Vector3 up = Vector3.Cross(side, direction.normalized);
                for (int i = 0; i < count; i++)
                {
                    float t = Mathf.Repeat(phase * 6f + i / (float)count + stream.Phase, 1f);
                    float spin = i * 2.399963f + t * Mathf.PI * 4f;
                    float radius = .11f * Mathf.Sin(t * Mathf.PI) * (1f - .65f * t);
                    float visibility = Mathf.Sin(t * Mathf.PI);
                    stream.Buffer[i] = new ParticleSystem.Particle
                    {
                        position = Vector3.Lerp(start, end, t) + (side * Mathf.Cos(spin) + up * Mathf.Sin(spin)) * radius,
                        startColor = new Color(.76f, .94f, .91f, .72f * visibility),
                        startSize = (.014f + .008f * (i % 3)) * visibility,
                        rotation = i * 47f + t * 180f,
                        startLifetime = 2f, remainingLifetime = 1f, randomSeed = (uint)(i + 1)
                    };
                }
                stream.Particles.SetParticles(stream.Buffer, count);
            }
        }
    }
}
