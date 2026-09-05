using System;

namespace HowToSuck
{
    // Pure root-space field. Every sleeve samples the same function of Z and time.
    public sealed class TruckTubeProfile
    {
        public float AxisX { get; }
        public float AxisY { get; }
        public float FrontZ { get; }
        public float RearZ { get; }
        public float MouthFollowEndZ { get; }
        public float BottomAnchorRadius { get; }
        public float BoundaryFadeFraction { get; }
        public float PulseWidth { get; }
        public const float PulseStartPhase = .64f;
        public const float PulseEndPhase = .88f;

        public TruckTubeProfile(float axisX, float axisY, float frontZ, float rearZ,
            float mouthFollowEndZ, float bottomAnchorRadius = 1.532f,
            float boundaryFadeFraction = .075f, float pulseWidth = .11f)
        {
            if (!Finite(axisX) || !Finite(axisY) || !Finite(frontZ) || !Finite(rearZ) ||
                !Finite(mouthFollowEndZ) || !(frontZ < mouthFollowEndZ && mouthFollowEndZ < rearZ))
                throw new ArgumentException("Use finite root-space front < follow-end < rear Z.");
            if (!Finite(bottomAnchorRadius) || bottomAnchorRadius < 0 ||
                !Finite(boundaryFadeFraction) || boundaryFadeFraction <= 0 || boundaryFadeFraction > .5f ||
                !Finite(pulseWidth) || pulseWidth <= 0)
                throw new ArgumentOutOfRangeException(nameof(bottomAnchorRadius), "Invalid tube field shape.");
            AxisX = axisX; AxisY = axisY; FrontZ = frontZ; RearZ = rearZ;
            MouthFollowEndZ = mouthFollowEndZ; BottomAnchorRadius = bottomAnchorRadius;
            BoundaryFadeFraction = boundaryFadeFraction; PulseWidth = pulseWidth;
        }

        // Baseline manifest, not measured runtime-import proof. Rebuild from actual bounds after FBX changes.
        public static TruckTubeProfile AuthoredTruck() =>
            new TruckTubeProfile(0, 1.65f, -3.035f, -1.329f, -2.245f);

        public float ScaleAt(float rootZ, float phase, float mouthScale, float pulseSize)
        {
            if (!Finite(rootZ) || !Finite(phase) || !Finite(mouthScale) || mouthScale < 1 ||
                !Finite(pulseSize) || pulseSize < 0)
                throw new ArgumentOutOfRangeException(nameof(phase), "Deformation inputs must be finite and nonnegative; mouth scale >= 1.");
            // Exact attachment constraints, also outside the deformed span.
            if (rootZ <= FrontZ) return mouthScale;
            if (rootZ >= RearZ) return 1;
            double u = (rootZ - (double)FrontZ) / (RearZ - (double)FrontZ);
            double followU = (rootZ - (double)FrontZ) / (MouthFollowEndZ - (double)FrontZ);
            double follow = 1 + (mouthScale - 1) * (1 - Smooth01(followU));
            double envelope = Smooth01(u / BoundaryFadeFraction) *
                Smooth01((1 - u) / BoundaryFadeFraction);
            double t = Clamp01(phase);
            double centre = PulseStartPhase + (PulseEndPhase - PulseStartPhase) * u;
            double q = (t - centre) / PulseWidth;
            // Finish at the exact baseline instead of popping a remaining Gaussian tail on release.
            double temporalTail = 1 - Smooth01((t - PulseEndPhase) / (1 - PulseEndPhase));
            double pulse = 1 + Math.Exp(-q * q) * envelope * temporalTail * Math.Min(1.1, pulseSize);
            return (float)Math.Max(follow, pulse);
        }

        public void DeformRootXY(float x, float y, float z, float phase, float mouthScale,
            float pulseSize, out float deformedX, out float deformedY)
        {
            if (!Finite(x) || !Finite(y)) throw new ArgumentException("Root-space vertex is not finite.");
            float scale = ScaleAt(z, phase, mouthScale, pulseSize);
            if (scale == 1) { deformedX = x; deformedY = y; return; }
            deformedX = AxisX + (x - AxisX) * scale;
            deformedY = AxisY + (y - AxisY) * scale + (scale - 1) * BottomAnchorRadius;
            if (!Finite(deformedX) || !Finite(deformedY)) throw new OverflowException("Deformed vertex exceeds finite float range.");
            // Z is intentionally not an output: the caller retains its original value.
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static double Clamp01(double value) => Math.Max(0, Math.Min(1, value));
        private static double Smooth01(double value) { double u = Clamp01(value); return u * u * (3 - 2 * u); }
    }
}


