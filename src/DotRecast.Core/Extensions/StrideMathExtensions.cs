using Stride.Core.Mathematics;

namespace DotRecast.Core.Extensions;
public static class StrideMathExtensions
{
    public static void CopyTo(this Vector3 vector3, float[] array, int n)
    {
        array[n + 0] = vector3.X;
        array[n + 1] = vector3.Y;
        array[n + 2] = vector3.Z;
    }
}
