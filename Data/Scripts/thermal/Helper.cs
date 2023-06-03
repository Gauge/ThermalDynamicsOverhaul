using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using VRage.Noise.Combiners;
using VRageMath;

namespace ThermalOverhaul
{
    internal static class Helper
    {
        private const long size = 100000;

        /// <summary>
        /// flattens the 3D array into a linear representation
        /// </summary>
        /// <param name="vector"></param>
        /// <returns></returns>
        public static long Flatten(this Vector3I vector) 
        {
            return (size * size * vector.Z) + (size * vector.Y) + vector.X;
        }

        /// <summary>
        /// replaces the contents of the current vector with the extractect x,y,z values
        /// </summary>
        /// <param name="flatVector"></param>
        /// <returns></returns>
        public static Vector3I Unflatten(this Vector3I vector, long flatVector)
        {
            vector.Z = (int)(flatVector / (size * size));
            flatVector -= (vector.Z * size * size);
            
            vector.Y = (int)(flatVector / size);
            vector.Z = (int)(flatVector % size);

            return vector;
        }

    }
}
