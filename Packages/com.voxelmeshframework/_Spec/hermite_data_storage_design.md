# Hermite Data Storage Design for Dual Contouring

## Overview

Hermite data is the cornerstone of the Dual Contouring algorithm, storing surface intersection points and normals along voxel grid edges. This document details an efficient storage system optimized for Unity's Burst compiler and Job System, with considerations for memory layout, cache performance, and parallel access patterns.

## Core Concepts

### Grid Edge Topology

In a 3D voxel grid, edges are aligned along three axes:
- **X-edges**: Connect vertices (i,j,k) to (i+1,j,k)
- **Y-edges**: Connect vertices (i,j,k) to (i,j+1,k)
- **Z-edges**: Connect vertices (i,j,k) to (i,j,k+1)

For a chunk of size N³:
- X-edges: N × (N+1) × (N+1) = N(N+1)²
- Y-edges: (N+1) × N × (N+1) = N(N+1)²
- Z-edges: (N+1) × (N+1) × N = N(N+1)²
- Total edges: 3N(N+1)²

For N=32: 3 × 32 × 33² = 104,544 edges per chunk

### Edge Indexing Schemes

#### Separated Arrays (Recommended)

```csharp
public struct HermiteDataStorage
{
    // Separate arrays for each edge direction
    public NativeArray<HermiteEdge> edgesX;  // Size: N × (N+1) × (N+1)
    public NativeArray<HermiteEdge> edgesY;  // Size: (N+1) × N × (N+1)
    public NativeArray<HermiteEdge> edgesZ;  // Size: (N+1) × (N+1) × N
    
    // Validity bit arrays (1 bit per edge, packed)
    public NativeArray<uint> validX;  // Size: ceil(N × (N+1)² / 32)
    public NativeArray<uint> validY;  // Size: ceil(N × (N+1)² / 32)
    public NativeArray<uint> validZ;  // Size: ceil(N × (N+1)² / 32)
}
```

Advantages:
- Better cache locality when processing edges by direction
- Simpler indexing math
- Natural alignment for SIMD operations

#### Interleaved Array (Alternative)

```csharp
public struct HermiteDataStorageInterleaved
{
    // All edges in one array, interleaved by cell
    public NativeArray<HermiteEdge> edges;  // Size: 3N(N+1)²
    public NativeArray<uint> valid;          // Bit array
}
```

### Edge Indexing Functions

```csharp
// For separated arrays
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static int GetEdgeIndexX(int3 pos, int chunkSize)
{
    // X-edge from vertex (x,y,z) to (x+1,y,z)
    // Range: x ∈ [0, N-1], y ∈ [0, N], z ∈ [0, N]
    return pos.x + pos.y * chunkSize + pos.z * chunkSize * (chunkSize + 1);
}

[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static int GetEdgeIndexY(int3 pos, int chunkSize)
{
    // Y-edge from vertex (x,y,z) to (x,y+1,z)
    // Range: x ∈ [0, N], y ∈ [0, N-1], z ∈ [0, N]
    return pos.x + pos.y * (chunkSize + 1) + pos.z * (chunkSize + 1) * chunkSize;
}

[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static int GetEdgeIndexZ(int3 pos, int chunkSize)
{
    // Z-edge from vertex (x,y,z) to (x,y,z+1)
    // Range: x ∈ [0, N], y ∈ [0, N], z ∈ [0, N-1]
    return pos.x + pos.y * (chunkSize + 1) + pos.z * (chunkSize + 1) * (chunkSize + 1);
}

// Bit array helpers
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static bool IsEdgeValid(NativeArray<uint> validArray, int edgeIndex)
{
    int arrayIndex = edgeIndex >> 5;  // Divide by 32
    int bitIndex = edgeIndex & 31;    // Modulo 32
    return (validArray[arrayIndex] & (1u << bitIndex)) != 0;
}

[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static void SetEdgeValid(NativeArray<uint> validArray, int edgeIndex)
{
    int arrayIndex = edgeIndex >> 5;
    int bitIndex = edgeIndex & 31;
    validArray[arrayIndex] |= (1u << bitIndex);
}
```

## Data Structures

### Hermite Edge Data

```csharp
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
public struct HermiteEdge
{
    public float3 position;      // Intersection point in local chunk space
    public float3 normal;        // Surface normal at intersection
    public float t;              // Parametric position along edge [0,1]
    public byte material0;       // Material on negative side
    public byte material1;       // Material on positive side
    public byte flags;           // Edge flags (sharp feature, boundary, etc.)
    public ushort _padding;      // Align to 32 bytes for cache lines
}

[Flags]
public enum EdgeFlags : byte
{
    None = 0,
    SharpFeature = 1 << 0,      // Edge is part of a sharp feature
    MaterialBoundary = 1 << 1,   // Different materials on each side
    ChunkBoundary = 1 << 2,      // Edge is on chunk boundary
    Constrained = 1 << 3,        // Position was constrained during solve
}
```

### Optimized Storage for High-Density Meshes

For cases where most edges have intersections:

```csharp
public struct CompactHermiteStorage
{
    // Store only active edges
    public NativeList<HermiteEdge> activeEdges;
    public NativeHashMap<int, int> edgeIndexMap;  // Grid edge index → active edge index
    
    // Spatial acceleration structure
    public NativeMultiHashMap<int3, int> cellToEdges;  // Cell position → edge indices
}
```

## Memory Layout Optimization

### Cache-Friendly Access Patterns

```csharp
// Process edges in cache-friendly order
[BurstCompile]
public struct ProcessEdgesCacheFriendly : IJobParallelFor
{
    [ReadOnly] public NativeArray<HermiteEdge> edges;
    [ReadOnly] public NativeArray<uint> valid;
    
    public void Execute(int sliceIndex)
    {
        // Process XY slices for better cache locality
        int z = sliceIndex;
        int edgesPerSlice = CHUNK_SIZE * (CHUNK_SIZE + 1);
        int baseIndex = z * edgesPerSlice;
        
        // Process all X-edges in this Z-slice
        for (int i = 0; i < edgesPerSlice; i++)
        {
            int edgeIndex = baseIndex + i;
            if (IsEdgeValid(valid, edgeIndex))
            {
                ProcessEdge(edges[edgeIndex]);
            }
        }
    }
}
```

### SIMD-Friendly Layout

```csharp
// Structure-of-Arrays for SIMD processing
public struct HermiteDataSOA
{
    // Positions
    public NativeArray<float> posX, posY, posZ;
    
    // Normals
    public NativeArray<float> normX, normY, normZ;
    
    // Materials and metadata
    public NativeArray<byte> mat0, mat1;
    public NativeArray<float> t;
    public NativeArray<byte> flags;
}

// Batch processing with SIMD
[BurstCompile]
public static void ProcessEdgesSIMD(HermiteDataSOA data, int start, int count)
{
    // Process 4 edges at once
    for (int i = start; i < start + count; i += 4)
    {
        // Load 4 positions
        var px = load_ps(data.posX.GetUnsafeReadOnlyPtr() + i);
        var py = load_ps(data.posY.GetUnsafeReadOnlyPtr() + i);
        var pz = load_ps(data.posZ.GetUnsafeReadOnlyPtr() + i);
        
        // ... SIMD operations ...
    }
}
```

## Edge Detection and Population

### Parallel Edge Detection Job

```csharp
[BurstCompile]
public struct DetectEdgesJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<sbyte> sdf;
    [ReadOnly] public NativeArray<byte> materials;
    
    [NativeDisableParallelForRestriction]
    [WriteOnly] public NativeArray<HermiteEdge> edgesX;
    [NativeDisableParallelForRestriction]
    [WriteOnly] public NativeArray<uint> validX;
    
    public int chunkSize;
    public float voxelSize;
    
    public void Execute(int index)
    {
        // Convert linear index to 3D position
        int3 pos = LinearTo3D(index, chunkSize + 1);
        
        // Check if we can create an X-edge from this position
        if (pos.x < chunkSize)
        {
            CheckAndCreateEdgeX(pos);
        }
    }
    
    void CheckAndCreateEdgeX(int3 pos)
    {
        int idx0 = Index3D(pos);
        int idx1 = Index3D(pos + int3(1, 0, 0));
        
        float v0 = sdf[idx0];
        float v1 = sdf[idx1];
        
        // Check for sign change
        if (v0 * v1 < 0)
        {
            // Calculate intersection using linear interpolation
            float t = v0 / (v0 - v1);
            float3 localPos = (float3)pos + float3(t, 0, 0);
            
            // Calculate normal using gradient
            float3 grad0 = CalculateGradient(sdf, pos);
            float3 grad1 = CalculateGradient(sdf, pos + int3(1, 0, 0));
            float3 normal = normalize(lerp(grad0, grad1, t));
            
            // Store edge data
            int edgeIndex = GetEdgeIndexX(pos, chunkSize);
            edgesX[edgeIndex] = new HermiteEdge
            {
                position = localPos * voxelSize,
                normal = normal,
                t = t,
                material0 = materials[idx0],
                material1 = materials[idx1],
                flags = DetermineEdgeFlags(pos, materials[idx0], materials[idx1])
            };
            
            // Mark as valid
            SetEdgeValid(validX, edgeIndex);
        }
    }
}
```

### Gradient Calculation

```csharp
[MethodImpl(MethodImplOptions.AggressiveInlining)]
static float3 CalculateGradient(NativeArray<sbyte> sdf, int3 pos)
{
    // Central differences with boundary handling
    float dx = GetSDFSafe(sdf, pos + int3(1,0,0)) - GetSDFSafe(sdf, pos - int3(1,0,0));
    float dy = GetSDFSafe(sdf, pos + int3(0,1,0)) - GetSDFSafe(sdf, pos - int3(0,1,0));
    float dz = GetSDFSafe(sdf, pos + int3(0,0,1)) - GetSDFSafe(sdf, pos - int3(0,0,1));
    
    return float3(dx, dy, dz) * 0.5f;
}

[MethodImpl(MethodImplOptions.AggressiveInlining)]
static float GetSDFSafe(NativeArray<sbyte> sdf, int3 pos)
{
    // Clamp to valid range
    pos = clamp(pos, 0, CHUNK_SIZE);
    return sdf[Index3D(pos)];
}
```

## Cell-to-Edge Mapping

For dual contouring, we need to efficiently find all edges belonging to a cell:

```csharp
public struct CellEdges
{
    // 12 edges per cell in standard order
    public fixed int edgeIndices[12];
    public byte edgeDirections[12]; // 0=X, 1=Y, 2=Z
    public byte validMask;           // Bit mask of valid edges
}

[BurstCompile]
public static CellEdges GetCellEdges(int3 cellPos, int chunkSize)
{
    CellEdges result = new CellEdges();
    int edgeCount = 0;
    
    // Bottom face edges (Z = cellPos.z)
    // X-aligned edges
    AddEdge(ref result, ref edgeCount, cellPos, 0);                    // Edge 0
    AddEdge(ref result, ref edgeCount, cellPos + int3(0,1,0), 0);      // Edge 1
    
    // Y-aligned edges  
    AddEdge(ref result, ref edgeCount, cellPos, 1);                    // Edge 2
    AddEdge(ref result, ref edgeCount, cellPos + int3(1,0,0), 1);      // Edge 3
    
    // Top face edges (Z = cellPos.z + 1)
    // X-aligned edges
    AddEdge(ref result, ref edgeCount, cellPos + int3(0,0,1), 0);      // Edge 4
    AddEdge(ref result, ref edgeCount, cellPos + int3(0,1,1), 0);      // Edge 5
    
    // Y-aligned edges
    AddEdge(ref result, ref edgeCount, cellPos + int3(0,0,1), 1);      // Edge 6  
    AddEdge(ref result, ref edgeCount, cellPos + int3(1,0,1), 1);      // Edge 7
    
    // Vertical edges (Z-aligned)
    AddEdge(ref result, ref edgeCount, cellPos, 2);                    // Edge 8
    AddEdge(ref result, ref edgeCount, cellPos + int3(1,0,0), 2);      // Edge 9
    AddEdge(ref result, ref edgeCount, cellPos + int3(0,1,0), 2);      // Edge 10
    AddEdge(ref result, ref edgeCount, cellPos + int3(1,1,0), 2);      // Edge 11
    
    return result;
}

[MethodImpl(MethodImplOptions.AggressiveInlining)]
static void AddEdge(ref CellEdges edges, ref int count, int3 pos, byte direction)
{
    int index = 0;
    switch (direction)
    {
        case 0: index = GetEdgeIndexX(pos, CHUNK_SIZE); break;
        case 1: index = GetEdgeIndexY(pos, CHUNK_SIZE); break;
        case 2: index = GetEdgeIndexZ(pos, CHUNK_SIZE); break;
    }
    
    edges.edgeIndices[count] = index;
    edges.edgeDirections[count] = direction;
    edges.validMask |= (byte)(1 << count);
    count++;
}
```

## Chunk Boundary Handling

### Shared Edge Storage

```csharp
public struct ChunkBoundaryData
{
    // Edges shared with neighboring chunks
    public NativeArray<HermiteEdge> boundaryEdgesXMin, boundaryEdgesXMax;
    public NativeArray<HermiteEdge> boundaryEdgesYMin, boundaryEdgesYMax;
    public NativeArray<HermiteEdge> boundaryEdgesZMin, boundaryEdgesZMax;
    
    // Synchronization flags
    public NativeArray<byte> neighborDataReady; // 6 neighbors
}

// Copy boundary edges to neighbors
[BurstCompile]
public struct ShareBoundaryEdgesJob : IJob
{
    public HermiteDataStorage sourceData;
    [NativeDisableParallelForRestriction]
    public NativeArray<ChunkBoundaryData> allChunkBoundaries;
    public int3 chunkCoord;
    
    public void Execute()
    {
        // Copy X-max edges to X+1 neighbor's X-min
        CopyEdgesToNeighbor(
            sourceData.edgesX, 
            allChunkBoundaries[GetNeighborIndex(chunkCoord + int3(1,0,0))].boundaryEdgesXMin,
            GetXMaxEdgeIndices()
        );
        
        // ... similar for other boundaries
    }
}
```

### Edge Consistency

```csharp
// Ensure consistent Hermite data across chunk boundaries
[BurstCompile]
public struct ResolveSharedEdgesJob : IJob
{
    public NativeArray<HermiteEdge> edges;
    [ReadOnly] public NativeArray<HermiteEdge> neighborEdges;
    
    public void Execute()
    {
        for (int i = 0; i < edges.Length; i++)
        {
            if (IsSharedEdge(i))
            {
                // Average positions and normals for consistency
                var e1 = edges[i];
                var e2 = neighborEdges[GetCorrespondingIndex(i)];
                
                edges[i] = new HermiteEdge
                {
                    position = (e1.position + e2.position) * 0.5f,
                    normal = normalize(e1.normal + e2.normal),
                    t = (e1.t + e2.t) * 0.5f,
                    material0 = e1.material0, // Keep original materials
                    material1 = e1.material1,
                    flags = (EdgeFlags)(e1.flags | e2.flags)
                };
            }
        }
    }
}
```

## Memory Management

### Pooling System

```csharp
public class HermiteDataPool
{
    Stack<HermiteDataStorage> available;
    int chunkSize;
    Allocator allocator;
    
    public HermiteDataStorage Rent()
    {
        if (available.Count > 0)
            return available.Pop();
        
        return AllocateNew();
    }
    
    public void Return(HermiteDataStorage data)
    {
        // Clear validity bits
        ClearBitArray(data.validX);
        ClearBitArray(data.validY);
        ClearBitArray(data.validZ);
        
        available.Push(data);
    }
    
    HermiteDataStorage AllocateNew()
    {
        int edgeCountX = chunkSize * (chunkSize + 1) * (chunkSize + 1);
        int edgeCountY = (chunkSize + 1) * chunkSize * (chunkSize + 1);
        int edgeCountZ = (chunkSize + 1) * (chunkSize + 1) * chunkSize;
        
        return new HermiteDataStorage
        {
            edgesX = new NativeArray<HermiteEdge>(edgeCountX, allocator),
            edgesY = new NativeArray<HermiteEdge>(edgeCountY, allocator),
            edgesZ = new NativeArray<HermiteEdge>(edgeCountZ, allocator),
            validX = new NativeArray<uint>((edgeCountX + 31) / 32, allocator),
            validY = new NativeArray<uint>((edgeCountY + 31) / 32, allocator),
            validZ = new NativeArray<uint>((edgeCountZ + 31) / 32, allocator)
        };
    }
}
```

### Streaming and LOD

```csharp
public struct LODHermiteData
{
    // Reduced resolution Hermite data for distant chunks
    public NativeArray<HermiteEdge> coarseEdges;
    public int reductionFactor; // 2, 4, or 8
    
    // Importance metrics for edge simplification
    public NativeArray<float> edgeImportance;
}

[BurstCompile]
public struct SimplifyHermiteDataJob : IJob
{
    [ReadOnly] public HermiteDataStorage fullRes;
    [WriteOnly] public LODHermiteData lod;
    
    public void Execute()
    {
        // Downsample edges based on importance
        // Merge nearby edges with similar normals
        // Preserve sharp features and material boundaries
    }
}
```

## Performance Considerations

### Access Pattern Analysis

```csharp
// Metrics for optimization
public struct EdgeAccessMetrics
{
    public int totalAccesses;
    public int cacheHits;
    public int cacheMisses;
    public float averageAccessTime;
    
    public void RecordAccess(int edgeIndex, bool wasInCache)
    {
        totalAccesses++;
        if (wasInCache) cacheHits++;
        else cacheMisses++;
    }
}
```

### Parallel Access Safety

```csharp
// Thread-safe edge updates using atomic operations
[BurstCompile]
public struct AtomicEdgeUpdate : IJobParallelFor
{
    [NativeDisableParallelForRestriction]
    public NativeArray<HermiteEdge> edges;
    
    public void Execute(int index)
    {
        // Use Interlocked operations for concurrent updates
        var edge = edges[index];
        
        // Atomic update of position (example)
        float3 newPos = CalculateRefinedPosition(edge);
        AtomicAddFloat3(ref edges[index].position, newPos - edge.position);
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static void AtomicAddFloat3(ref float3 target, float3 delta)
    {
        // Note: Unity.Mathematics doesn't have native atomic float3
        // This is a conceptual example - real implementation would need
        // custom atomic operations or different synchronization strategy
    }
}
```

## Testing and Validation

### Unit Tests

```csharp
[Test]
public void TestEdgeIndexing()
{
    const int size = 32;
    
    // Test X-edge indexing
    for (int z = 0; z <= size; z++)
    {
        for (int y = 0; y <= size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int index = GetEdgeIndexX(int3(x, y, z), size);
                int3 recovered = RecoverPositionFromEdgeIndexX(index, size);
                Assert.That(recovered, Is.EqualTo(int3(x, y, z)));
            }
        }
    }
}

[Test]
public void TestBitArrayOperations()
{
    var valid = new NativeArray<uint>(100, Allocator.Temp);
    
    // Set some edges as valid
    SetEdgeValid(valid, 5);
    SetEdgeValid(valid, 31);
    SetEdgeValid(valid, 32);
    SetEdgeValid(valid, 127);
    
    // Verify
    Assert.That(IsEdgeValid(valid, 5), Is.True);
    Assert.That(IsEdgeValid(valid, 31), Is.True);
    Assert.That(IsEdgeValid(valid, 32), Is.True);
    Assert.That(IsEdgeValid(valid, 127), Is.True);
    Assert.That(IsEdgeValid(valid, 6), Is.False);
    
    valid.Dispose();
}
```

## Integration Example

```csharp
// Complete example of Hermite data in dual contouring pipeline
[BurstCompile]
public struct DualContouringPipeline : IJob
{
    [ReadOnly] public NativeArray<sbyte> sdf;
    [ReadOnly] public NativeArray<byte> materials;
    
    public HermiteDataStorage hermiteData;
    public NativeArray<DualContouringCell> cells;
    
    public void Execute()
    {
        // Phase 1: Detect edges and populate Hermite data
        var detectJob = new DetectEdgesJob
        {
            sdf = sdf,
            materials = materials,
            edgesX = hermiteData.edgesX,
            validX = hermiteData.validX,
            chunkSize = CHUNK_SIZE,
            voxelSize = VOXEL_SIZE
        };
        
        // Phase 2: Process cells using Hermite data
        for (int i = 0; i < cells.Length; i++)
        {
            ProcessCell(i);
        }
    }
    
    void ProcessCell(int cellIndex)
    {
        int3 cellPos = LinearTo3D(cellIndex, CHUNK_SIZE);
        var cellEdges = GetCellEdges(cellPos, CHUNK_SIZE);
        
        // Gather Hermite data for QEF
        var planes = new NativeArray<PlaneData>(12, Allocator.Temp);
        int planeCount = 0;
        
        for (int e = 0; e < 12; e++)
        {
            if ((cellEdges.validMask & (1 << e)) != 0)
            {
                var edge = GetEdgeData(cellEdges, e);
                if (IsEdgeValid(edge))
                {
                    planes[planeCount++] = ConvertToPlaneData(edge);
                }
            }
        }
        
        // Solve QEF and create cell
        if (planeCount > 0)
        {
            cells[cellIndex] = SolveCell(planes, planeCount, cellPos);
        }
        
        planes.Dispose();
    }
}
```

## Conclusion

This Hermite data storage system provides an efficient foundation for dual contouring implementation. The separated array approach offers the best balance of performance and simplicity, while the bit array validity tracking minimizes memory overhead. The design supports parallel processing, SIMD optimization, and seamless integration with Unity's Job System.
