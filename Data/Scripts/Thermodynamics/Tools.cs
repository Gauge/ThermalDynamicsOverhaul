using System;
using System.Collections.Generic;
using System.Text;
using VRageMath;

namespace Thermodynamics
{
    public class Tools
    {

        public const float MWtoWatt = 150000;

        /// <summary>
        /// Converts a single axis direction vector into a number
        /// </summary>
        /// <param name="vector"></param>
        /// <returns></returns>
        public static int DirectionToIndex(Vector3I vector)
        {
            return (vector.X > 0) ? 0 : (vector.X < 0) ? 1 : (vector.Y > 0) ? 2 : (vector.Y < 0) ? 3 : (vector.Z > 0) ? 4 : 5;
        }

        /// <summary>
        /// Converts the direction index into a vector
        /// </summary>
        /// <param name="index"></param>
        /// <returns></returns>
        public static Vector3 IndexToDirection(int index) 
        {
            switch (index) 
            {
                case 0:
                    return new Vector3(1, 0, 0);
                case 1:
                    return new Vector3(-1, 0, 0);
                case 2:
                    return new Vector3(0, 1, 0);
                case 3:
                    return new Vector3(0,-1, 0);
                case 4:
                    return new Vector3(0, 0, 1);
                case 5:
                    return new Vector3(0, 0, -1);
                default:
                    return Vector3.Zero;
            }
        }

        /// <summary>
        /// Generates a heat map
        /// </summary>
        /// <param name="temp">current temperature</param>
        /// <param name="max">maximum possible temprature</param>
        /// <param name="low">0 is black this value is blue</param>
        /// <param name="high">this value is red max value is white</param>
        /// <returns>HSV Vector3</returns>
        public static Vector3 GetTemperatureColor(float temp, float max = 2000, float low = 265f, float high = 500f)
        {
            // Clamp the temperature to the range 0-max
            float t = Math.Max(0, Math.Min(max, temp));

            float h = 240f / 360f;
            float s = 1;
            float v = 0.5f;

            if (t < low)
            {
                v = (1.5f * (t / low)) - 1;
            }
            else if (t < high)
            {
                h = (240f - ((t - low) / (high - low) * 240f)) / 360f;
            }
            else
            {
                h = 0;
                s = 1 - (2 * ((t - high) / (max - high)));
            }

            return new Vector3(h, s, v);
        }

        /// <summary>
        /// Calculates the surface area of the touching sides
        /// If you are using IMySlimBlock. add +1 to the max value
        /// </summary>
        public static int FindTouchingSurfaceArea(Vector3I minA, Vector3I maxA, Vector3I minB, Vector3I maxB) 
        {
            bool touched = false;
            int val = 1;
            if (!(minA.X == maxB.X || maxA.X == minB.X))
            {
                val *= Math.Min(maxA.X, maxB.X) - Math.Max(minA.X, minB.X);
                touched = true;
            }

            if (!(minA.Y == maxB.Y || maxA.Y == minB.Y))
            {
                val *= Math.Min(maxA.Y, maxB.Y) - Math.Max(minA.Y, minB.Y);
                touched = true;
            }

            if (!(minA.Y == maxB.Y || maxA.Y == minB.Y))
            {
                val *= Math.Min(maxA.Y, maxB.Y) - Math.Max(minA.Y, minB.Y);
                touched = true;
            }

            if (!touched)
                return 0;

            return val;
        }
    }
}
