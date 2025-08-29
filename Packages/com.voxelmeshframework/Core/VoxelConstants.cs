namespace Voxels.Core
{
	/// <summary>
	///   Chunk: Fundamental Voxel Data Structure for Seamless Surface Meshing
	///   This class represents the core data structure that enables seamless voxel terrain
	///   generation. It contains the mathematical constants and memory layout that form
	///   the foundation of the entire surface meshing pipeline.
	///   CORE DESIGN PRINCIPLES:
	///   ======================
	///   The chunk design is built around several critical mathematical relationships
	///   that enable seamless meshing across arbitrary numbers of chunks:
	///   1. POWER-OF-TWO SIZING:
	///   - CHUNK_SIZE = 32 = 2^5
	///   - Enables efficient bit shifting for memory indexing
	///   - Optimizes SIMD processing alignment
	///   - Allows for efficient subdivision and spatial queries
	///   2. OVERLAP REGION DESIGN:
	///   - CHUNK_OVERLAP = 2 voxels
	///   - EFFECTIVE_CHUNK_SIZE = 30 (non-overlapping portion)
	///   - Creates shared boundary regions between adjacent chunks
	///   - Ensures surface continuity without gaps or T-junctions
	///   3. MEMORY LAYOUT OPTIMIZATION:
	///   - Linear array storage for 32³ = 32,768 voxels
	///   - Cache-friendly memory access patterns
	///   - SIMD-optimized data alignment
	///   - Efficient parallel processing support
	///   SEAMLESS MESHING MATHEMATICS:
	///   ============================
	///   The seamless meshing system relies on precise mathematical relationships:
	///   SPATIAL ARRANGEMENT:
	///   ===================
	///   Adjacent chunks overlap by exactly 2 voxels on each face:
	///   Chunk Layout (1D visualization):
	///   Chunk 0:  [0  1  2  3  4  ...  29  30  31]
	///   Chunk 1:           [30 31 32 33 ...  59  60  61]
	///   ↑-overlap-↑
	///   World positions:
	///   - Chunk 0 at world position 0
	///   - Chunk 1 at world position 30 (EFFECTIVE_CHUNK_SIZE)
	///   - Voxels 30-31 are shared between both chunks
	///   COORDINATE TRANSFORMATIONS:
	///   ===========================
	///   The bit shift constants enable ultra-fast coordinate transformations:
	///   3D to Linear Index: index = (x &lt;&lt; X_SHIFT) + ( 'y &lt;&lt; Y_SHIFT) + z
	///   Where:
	///   - X_SHIFT= 10 ( x * 1024, spans Y* Z plane)
	///   - Y_SHIFT= 5  ( y * 32, spans Z row)
	///   - Z_SHIFT= 0  ( z * 1, individual voxel)
	///   This creates the memory layout:
	///   [ x=0, y=0, z=0] [ x=0, y=0, z=1] ... [ x=0, y=0, z=31] [ x=0, y=1,
	///   z=0] ...
	///   VOXEL VALUE REPRESENTATION:===========================
	///   Each voxel stores a signed distance field ( SDF) value as an
	///   sbyte:
	///   Value Range: -127 to +127
	///   - Negative values: Interior ( solid material)
	///   - Positive values: Exterior ( air/ void)
	///   - Zero crossing: Surface boundary
	///   This 8- bit representation provides:
	///   - Memory efficiency (32 KB per chunk vs 128 KB for float)
	///   - SIMD processing optimization (16 voxels per 128- bit register)
	///   - Sufficient precision for visual quality
	///   - Fast comparison operations for surface detection
	///   MEMORY ALLOCATION STRATEGY:===========================
	///   Persistent allocation ensures:
	///   - Chunks persist across frame boundaries
	///   - No garbage collection pressure during gameplay
	///   - Stable memory layout for job system processing
	///   - Thread-safe access from parallel jobs
	///   PERFORMANCE CHARACTERISTICS:============================
	///   Memory Usage:
	///   - 32³ × 1 byte= 32,768 bytes= 32 KB per chunk
	///   - Compact storage enables large chunk counts
	///   - Cache-friendly access patterns for processing
	///   Processing Efficiency:
	///   - Power-of-two dimensions optimize loop unrolling
	///   - Bit shift indexing is faster than multiplication
	///   - SIMD alignment enables vectorized operations
	///   - Linear layout minimizes cache misses
	///   The result is a highly optimized data structure that enables
	///   seamless
	///   voxel terrain generation at scale while maintaining excellent
	///   performance
	///   characteristics for real-time applications.
	/// </summary>
	public static class VoxelConstants
	{
		/// <summary>
		///   FUNDAMENTAL CHUNK DIMENSION: The Foundation of Seamless Meshing
		///   CHUNK_SIZE = 32 is not arbitrary - it's carefully chosen for multiple reasons:
		///   1. SIMD OPTIMIZATION:
		///   - 32 = 2^5, enables efficient bit shifting
		///   - Aligns with 128-bit SIMD registers (16 bytes = 16 voxels)
		///   - Optimizes vectorized processing in Surface Nets algorithm
		///   2. MEMORY EFFICIENCY:
		///   - 32³ = 32,768 voxels = 32KB per chunk
		///   - Fits comfortably in CPU cache levels
		///   - Balances memory usage with processing granularity
		///   3. PROCESSING GRANULARITY:
		///   - Large enough for meaningful terrain features
		///   - Small enough for responsive updates
		///   - Optimal for parallel job processing
		///   WARNING: Changing this value requires careful consideration of:
		///   - SIMD optimization code in MesherJob
		///   - Memory allocation patterns
		///   - Performance characteristics
		///   - Bit shift constants below
		/// </summary>
		public const int CHUNK_SIZE = 32;

		public const int VOLUME_LENGTH = CHUNK_SIZE * CHUNK_SIZE * CHUNK_SIZE;

		/// <summary>
		///   BOUNDARY CALCULATION CONSTANTS: Essential for Surface Nets Processing
		///   These constants are used throughout the Surface Nets algorithm for:
		///   - Loop boundary conditions
		///   - Edge case handling
		///   - Gradient calculation limits
		///   - Safe memory access bounds
		/// </summary>
		public const int CHUNK_SIZE_MINUS_ONE = CHUNK_SIZE - 1; // 31: Last valid index

		public const int CHUNK_SIZE_MINUS_TWO = CHUNK_SIZE - 2; // 30: Safe gradient sampling bound

		/// <summary>
		///   SEAMLESS MESHING OVERLAP: The Secret to Crack-Free Surfaces
		///   CHUNK_OVERLAP = 2 creates a 2-voxel shared region between adjacent chunks.
		///   This overlap is essential for seamless surface generation:
		///   OVERLAP NECESSITY:
		///   - Surface Nets processes 2x2x2 voxel cubes
		///   - Each cube needs neighboring voxels for surface determination
		///   - Without overlap, boundary cubes would lack neighbor data
		///   - 2-voxel overlap ensures all boundary cubes have complete neighborhoods
		///   SURFACE CONTINUITY GUARANTEE:
		///   - Adjacent chunks generate identical voxel values in overlap regions
		///   - Surface Nets creates identical vertices at shared positions
		///   - Results in perfectly seamless mesh connectivity
		///   - No post-processing required to fix boundary issues
		/// </summary>
		public const int CHUNK_OVERLAP = 2;

		/// <summary>
		///   EFFECTIVE CHUNK SIZE: The True Chunk Spacing for World Positioning
		///   EFFECTIVE_CHUNK_SIZE = 30 determines how chunks are spaced in world coordinates:
		///   CHUNK POSITIONING LOGIC:
		///   - Chunk [0,0,0] at world position (0, 0, 0)
		///   - Chunk [1,0,0] at world position (30, 0, 0)
		///   - Chunk [0,1,0] at world position (0, 30, 0)
		///   This 30-unit spacing creates the precise 2-voxel overlap needed for seamless meshing.
		///   Using CHUNK_SIZE (32) for spacing would create gaps, while using smaller values
		///   would create excessive overlap and waste processing power.
		/// </summary>
		public const int EFFECTIVE_CHUNK_SIZE = CHUNK_SIZE - CHUNK_OVERLAP; // 30

		/// <summary>
		///   MEMORY INDEXING BIT SHIFTS: Ultra-Fast 3D to Linear Coordinate Conversion
		///   These constants enable blazing-fast coordinate transformations using bit shifts
		///   instead of expensive multiplication operations:
		///   INDEXING FORMULA:
		///   linearIndex = (x &lt;&lt; X_SHIFT) + ( 'y &lt;&lt; Y_SHIFT) + ( 'z &lt;&lt; Z_SHIFT)
		///   SHIFT CALCULATIONS:
		///   - X_SHIFT= 10 : x * 1024= x * (32 * 32) spans entire YZ
		///   planes
		///   - Y_SHIFT= 5 : y * 32 spans entire Z rows
		///   - Z_SHIFT= 0 : z * 1 individual voxel offset
		///   MEMORY LAYOUT VISUALIZATION:
		///   The resulting memory layout is:
		///   [ x=0, y=0 : z=0,1,2...31] [ x=0, y=1 : z=0,1,2...31] ... [
		///   x=0, y=31 : z=0,1,2...31]
		///   [ x=1, y=0 : z=0,1,2...31] [ x=1, y=1 : z=0,1,2...31] ... [
		///   x=1, y=31 : z=0,1,2...31]
		///   ...
		///   PERFORMANCE BENEFITS:
		///   - Bit shifts are 10-100 x faster than multiplication on most
		///   CPUs
		///   - Enables tight loops in Surface Nets algorithm
		///   - Critical for real-time voxel processing performance
		/// </summary>
		public const int X_SHIFT = 10; // x * 1024 (32²)

		public const int Y_SHIFT = 5; // y * 32
		public const int Z_SHIFT = 0; // z * 1
	}
}
