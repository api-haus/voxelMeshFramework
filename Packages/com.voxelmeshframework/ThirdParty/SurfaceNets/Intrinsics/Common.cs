namespace Voxels.ThirdParty.SurfaceNets.Intrinsics
{
	using Unity.Burst.Intrinsics;

	/// <summary>
	///   Static methods and properties for X86 instruction intrinsics.
	/// </summary>
	public static unsafe partial class X86F
	{
		static v128 GenericCSharpLoad(void* ptr)
		{
			return *(v128*)ptr;
		}

		static void GenericCSharpStore(void* ptr, v128 val)
		{
			*(v128*)ptr = val;
		}

		static sbyte Saturate_To_Int8(int val)
		{
			if (val > sbyte.MaxValue)
				return sbyte.MaxValue;
			if (val < sbyte.MinValue)
				return sbyte.MinValue;
			return (sbyte)val;
		}

		static byte Saturate_To_UnsignedInt8(int val)
		{
			if (val > byte.MaxValue)
				return byte.MaxValue;
			if (val < byte.MinValue)
				return byte.MinValue;
			return (byte)val;
		}

		static short Saturate_To_Int16(int val)
		{
			if (val > short.MaxValue)
				return short.MaxValue;
			if (val < short.MinValue)
				return short.MinValue;
			return (short)val;
		}

		static ushort Saturate_To_UnsignedInt16(int val)
		{
			if (val > ushort.MaxValue)
				return ushort.MaxValue;
			if (val < ushort.MinValue)
				return ushort.MinValue;
			return (ushort)val;
		}

		static bool IsNaN(uint v)
		{
			return (v & 0x7fffffffu) > 0x7f800000;
		}

		static bool IsNaN(ulong v)
		{
			return (v & 0x7ffffffffffffffful) > 0x7ff0000000000000ul;
		}
	}
}
