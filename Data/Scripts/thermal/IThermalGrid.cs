using System;
using System.Collections.Generic;
using System.Text;
using VRageMath;

namespace ThermalOverhaul
{
	public interface IThermalGrid
	{

		/// <summary>
		/// gets the cell at a specific location
		/// </summary>
		/// <param name="position"></param>
		/// <returns></returns>
		IThermalCell GetCell(Vector3I position);

		/// <summary>
		/// Gets the average temperature of the grid
		/// </summary>
		/// <returns></returns>
		float GetTemperature();

	}
}
