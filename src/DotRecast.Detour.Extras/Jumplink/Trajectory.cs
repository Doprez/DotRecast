using System;
using DotRecast.Core.Numerics;
using Stride.Core.Mathematics;

namespace DotRecast.Detour.Extras.Jumplink
{
    public class Trajectory
    {
        public float Lerp(float f, float g, float u)
        {
            return u * g + (1f - u) * f;
        }

        public virtual Vector3 Apply(Vector3 start, Vector3 end, float u)
        {
            throw new NotImplementedException();
        }
    }
}