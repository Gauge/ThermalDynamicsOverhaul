using System;
using System.Collections.Generic;
using System.Text;

namespace ThermalOverhaul
{
	public interface IThermalCell
	{
		/// <summary>
		/// the temerature of the cell
		/// </summary>
		/// <returns></returns>
		float GetTemperature();
	}
}
