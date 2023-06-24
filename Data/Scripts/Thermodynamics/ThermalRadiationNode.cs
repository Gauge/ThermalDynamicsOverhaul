using Sandbox.Game.Entities;
using System;
using System.Collections.Generic;
using System.Text;
using VRageMath;

namespace Thermodynamics
{
    public class ThermalRadiationNode
    {
        public bool IsSolarNode => Source == null;

        public ThermalCell Source;

        public float[] Sides;
        public float[] SideAverages;
        public int[] SideSurfaces;

        public ThermalRadiationNode() 
        {
            Sides = new float[6];
            SideAverages = new float[6];
            SideSurfaces = new int[6];
        }
        public ThermalRadiationNode(ThermalCell cell)
        {
            Source = cell;
            Sides = new float[6];
            SideAverages = new float[6];
            SideSurfaces = new int[6];
        }

        /// <summary>
        /// Run at the end of the heat tick to setup for the next
        /// </summary>
        public void Update() 
        {
            for (int i = 0; i < 6; i++)
            {
                SideAverages[i] = (SideSurfaces[i] > 0) ? Sides[i] / (float)SideSurfaces[i] : 0;
            }

            Sides = new float[6];
            SideSurfaces = new int[6];
        }
    }
}
