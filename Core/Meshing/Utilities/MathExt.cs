namespace Voxels.Core.Meshing.Utilities
{
	using static Unity.Mathematics.math;

	public class mathex
	{
		/// <summary>
		///   <para>Compares two floating point values and returns true if they are similar.</para>
		/// </summary>
		/// <param name="a"></param>
		/// <param name="b"></param>
		public static bool approx(float a, float b)
		{
			return abs(b - a) < (double)max(1E-06f * max(abs(a), abs(b)), EPSILON * 8f);
		}
	}
}
