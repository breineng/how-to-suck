using System;
using UnityEngine;

namespace HowToSuck
{
    /// <summary>Shortest-arc rotation without discarding small nonzero angles.</summary>
    public static class RegripRotation
    {
        public static Quaternion FromTo(Vector3 from, Vector3 to)
        {
            double ax=from.x, ay=from.y, az=from.z, bx=to.x, by=to.y, bz=to.z;
            double aa=ax*ax+ay*ay+az*az, bb=bx*bx+by*by+bz*bz;
            if (!(aa>0) || !(bb>0) || double.IsInfinity(aa) || double.IsInfinity(bb))
                throw new InvalidOperationException("RegripD: finite nonzero rotation directions required");
            double al=Math.Sqrt(aa), bl=Math.Sqrt(bb);
            ax/=al; ay/=al; az/=al; bx/=bl; by/=bl; bz/=bl;
            double cx=ay*bz-az*by, cy=az*bx-ax*bz, cz=ax*by-ay*bx;
            double crossLength=Math.Sqrt(cx*cx+cy*cy+cz*cz), dot=ax*bx+ay*by+az*bz;
            if (crossLength==0)
            {
                if (dot>=0) return Quaternion.identity;
                // Exact antiparallel inputs have no unique shortest-arc axis.
                // Cross with the least aligned basis axis for a deterministic, nonzero choice.
                if (Math.Abs(ax)<=Math.Abs(ay) && Math.Abs(ax)<=Math.Abs(az)) { cx=0; cy=az; cz=-ay; }
                else if (Math.Abs(ay)<=Math.Abs(az)) { cx=-az; cy=0; cz=ax; }
                else { cx=ay; cy=-ax; cz=0; }
                double length=Math.Sqrt(cx*cx+cy*cy+cz*cz);
                return new Quaternion((float)(cx/length),(float)(cy/length),(float)(cz/length),0);
            }
            double halfAngle=.5*Math.Atan2(crossLength,dot), scale=Math.Sin(halfAngle)/crossLength;
            return new Quaternion((float)(cx*scale),(float)(cy*scale),(float)(cz*scale),(float)Math.Cos(halfAngle));
        }
    }
}
