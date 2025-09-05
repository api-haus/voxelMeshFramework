# Dual Contouring Implementation Plan for Unity Voxel Framework

## Executive Summary

This document outlines a comprehensive plan to replace the current "botched dual contouring on naive surface nets" implementation with a proper Dual Contouring (DC) algorithm. The new system will preserve sharp features, handle multi-material boundaries cleanly, and provide higher quality meshes while maintaining real-time performance through Unity's Burst compiler and Job System.

## Current State Analysis

### Existing Implementation Issues
1. **Surface Nets Limitations**: Current implementation places vertices at weighted average of edge crossings, losing sharp features
2. **Material Blending Artifacts**: Per-vertex material encoding causes banding and requires complex shader workarounds
3. **No Hermite Data**: Missing surface normals at edge intersections prevents accurate feature preservation
4. **Fixed Resolution**: No adaptive octree structure for varying levels of detail

### What Works Well
1. SIMD-optimized voxel processing
2. Efficient chunk-based architecture
3. Burst-compiled jobs for performance
4. Material system concept (though implementation needs rework)

## Dual Contouring Algorithm Overview

### Core Principles
1. **Hermite Data**: Store both position and normal at each edge crossing
2. **QEF Minimization**: Solve Quadratic Error Function to find optimal vertex position within each cell
3. **Sharp Feature Preservation**: Vertices placed to minimize error from all intersecting planes
4. **Manifold Guarantee**: One vertex per cell, connected by quads

### Algorithm Steps
1. **Edge Detection**: Find sign changes along grid edges
2. **Hermite Data Collection**: Calculate intersection point and normal for each edge crossing
3. **QEF Setup**: Build error function from all edge crossings in a cell
4. **Vertex Placement**: Solve QEF (possibly with constraints) to find optimal position
5. **Quad Generation**: Connect vertices of adjacent cells sharing a face

## Implementation Architecture

### Data Structures

```csharp
// Core Hermite data per edge
[BurstCompile]
public struct HermiteEdge
{
    public float3 position;     // Intersection point on edge
    public float3 normal;       // Surface normal at intersection
    public byte material0;      // Material on negative side
    public byte material1;      // Material on positive side
    public float t;            // Parametric position on edge [0,1]
}

// Per-cell dual contouring data
[BurstCompile]
public struct DualContouringCell
{
    public float3 vertexPosition;   // QEF-solved vertex position
    public float3 vertexNormal;     // Accumulated normal at vertex
    public byte primaryMaterial;    // Dominant material in cell
    public byte secondaryMaterial;  // Second material (for boundaries)
    public float materialBlend;     // Blend factor between materials
    public byte edgeMask;          // Which edges have crossings (12 bits)
    public byte faceMask;          // Which faces need quads (6 bits)
}

// QEF solver data
[BurstCompile]
public struct QEFData
{
    public float3x3 ATA;    // Normal matrix (A^T * A)
    public float3 ATb;      // Right-hand side (A^T * b)
    public float3 massPoint; // Average of intersection points
    public int numPlanes;    // Number of intersecting planes
}
```

### Volume Data Storage

```csharp
// Enhanced voxel chunk with Hermite data
public struct VoxelChunkDC : IDisposable
{
    // Existing SDF data
    public NativeArray<sbyte> sdf;         // Signed distance field
    public NativeArray<byte> materials;    // Material IDs
    
    // Hermite edge data (3 arrays for X, Y, Z aligned edges)
    public NativeArray<HermiteEdge> edgesX;
    public NativeArray<HermiteEdge> edgesY;
    public NativeArray<HermiteEdge> edgesZ;
    
    // Dual contouring cell data
    public NativeArray<DualContouringCell> cells;
    
    // Edge validity flags (packed bits)
    public NativeArray<uint> edgeValidX;
    public NativeArray<uint> edgeValidY;
    public NativeArray<uint> edgeValidZ;
}
```

### Processing Pipeline

#### Phase 1: Hermite Data Extraction Job

```csharp
[BurstCompile]
public struct ExtractHermiteDataJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<sbyte> sdf;
    [ReadOnly] public NativeArray<byte> materials;
    
    [WriteOnly] public NativeArray<HermiteEdge> edgesX;
    [WriteOnly] public NativeArray<HermiteEdge> edgesY;
    [WriteOnly] public NativeArray<HermiteEdge> edgesZ;
    [WriteOnly] public NativeArray<uint> edgeValidX;
    [WriteOnly] public NativeArray<uint> edgeValidY;
    [WriteOnly] public NativeArray<uint> edgeValidZ;
    
    public void Execute(int index)
    {
        // Convert linear index to 3D position
        int3 pos = LinearTo3D(index);
        
        // Check X-aligned edge
        if (pos.x < CHUNK_SIZE - 1)
        {
            CheckEdge(pos, int3(1,0,0), edgesX, edgeValidX);
        }
        
        // Check Y-aligned edge
        if (pos.y < CHUNK_SIZE - 1)
        {
            CheckEdge(pos, int3(0,1,0), edgesY, edgeValidY);
        }
        
        // Check Z-aligned edge
        if (pos.z < CHUNK_SIZE - 1)
        {
            CheckEdge(pos, int3(0,0,1), edgesZ, edgeValidZ);
        }
    }
    
    void CheckEdge(int3 p0, int3 dir, NativeArray<HermiteEdge> edges, NativeArray<uint> valid)
    {
        int3 p1 = p0 + dir;
        float v0 = sdf[Index3D(p0)];
        float v1 = sdf[Index3D(p1)];
        
        // Check for sign change
        if (v0 * v1 < 0)
        {
            // Calculate intersection point
            float t = v0 / (v0 - v1);
            float3 position = (float3)p0 + t * (float3)dir;
            
            // Calculate normal using central differences
            float3 normal = CalculateGradient(position);
            
            // Store Hermite data
            int edgeIndex = CalculateEdgeIndex(p0, dir);
            edges[edgeIndex] = new HermiteEdge
            {
                position = position * voxelSize,
                normal = normalize(normal),
                material0 = materials[Index3D(p0)],
                material1 = materials[Index3D(p1)],
                t = t
            };
            
            // Mark edge as valid
            SetEdgeValid(valid, edgeIndex);
        }
    }
}
```

#### Phase 2: QEF Solving Job

```csharp
[BurstCompile]
public struct SolveDualContouringJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<HermiteEdge> edgesX;
    [ReadOnly] public NativeArray<HermiteEdge> edgesY;
    [ReadOnly] public NativeArray<HermiteEdge> edgesZ;
    [ReadOnly] public NativeArray<uint> edgeValidX;
    [ReadOnly] public NativeArray<uint> edgeValidY;
    [ReadOnly] public NativeArray<uint> edgeValidZ;
    
    [WriteOnly] public NativeArray<DualContouringCell> cells;
    
    public void Execute(int index)
    {
        int3 cellPos = LinearTo3D(index);
        
        // Gather all Hermite data for this cell
        QEFData qef = GatherCellHermiteData(cellPos);
        
        if (qef.numPlanes == 0)
        {
            // No intersections in this cell
            cells[index] = default;
            return;
        }
        
        // Solve QEF for vertex position
        float3 vertexPos = SolveQEF(qef, cellPos);
        
        // Determine materials
        var (mat0, mat1, blend) = DetermineCellMaterials(cellPos);
        
        cells[index] = new DualContouringCell
        {
            vertexPosition = vertexPos,
            vertexNormal = normalize(qef.ATb), // Approximate normal
            primaryMaterial = mat0,
            secondaryMaterial = mat1,
            materialBlend = blend,
            edgeMask = CalculateEdgeMask(cellPos),
            faceMask = CalculateFaceMask(cellPos)
        };
    }
}
```

#### Phase 3: Mesh Generation Job

```csharp
[BurstCompile]
public struct GenerateDualContouringMeshJob : IJob
{
    [ReadOnly] public NativeArray<DualContouringCell> cells;
    
    [WriteOnly] public NativeList<float3> vertices;
    [WriteOnly] public NativeList<float3> normals;
    [WriteOnly] public NativeList<int> indices;
    [WriteOnly] public NativeList<Color32> vertexColors;
    
    public void Execute()
    {
        // Build vertex buffer and index map
        var vertexMap = new NativeHashMap<int, int>(cells.Length, Allocator.Temp);
        
        // First pass: collect vertices
        for (int i = 0; i < cells.Length; i++)
        {
            var cell = cells[i];
            if (cell.edgeMask != 0)
            {
                int vertexIndex = vertices.Length;
                vertexMap[i] = vertexIndex;
                
                vertices.Add(cell.vertexPosition);
                normals.Add(cell.vertexNormal);
                
                // Enhanced material encoding for shader
                vertexColors.Add(new Color32(
                    cell.primaryMaterial,
                    cell.secondaryMaterial,
                    (byte)(cell.materialBlend * 255),
                    255 // Reserved for future use
                ));
            }
        }
        
        // Second pass: generate quads
        for (int z = 0; z < CHUNK_SIZE - 1; z++)
        {
            for (int y = 0; y < CHUNK_SIZE - 1; y++)
            {
                for (int x = 0; x < CHUNK_SIZE - 1; x++)
                {
                    GenerateQuadsForCell(int3(x,y,z), vertexMap);
                }
            }
        }
    }
}
```

### QEF Solver Implementation

```csharp
// Optimized QEF solver using Burst-friendly operations
[BurstCompile]
static float3 SolveQEF(QEFData qef, int3 cellPos)
{
    // Use pseudoinverse to solve normal equations
    // A^T * A * x = A^T * b
    
    // Add regularization for stability
    const float REGULARIZATION = 0.001f;
    qef.ATA.c0.x += REGULARIZATION;
    qef.ATA.c1.y += REGULARIZATION;
    qef.ATA.c2.z += REGULARIZATION;
    
    // Try to invert the matrix
    float det = determinant(qef.ATA);
    
    if (abs(det) < 0.0001f)
    {
        // Singular matrix - fall back to mass point
        return qef.massPoint;
    }
    
    // Solve using inverse
    float3x3 inv = inverse(qef.ATA);
    float3 solution = mul(inv, qef.ATb) + qef.massPoint;
    
    // Constrain to cell bounds
    float3 cellMin = (float3)cellPos;
    float3 cellMax = cellMin + 1.0f;
    solution = clamp(solution, cellMin, cellMax);
    
    return solution;
}
```

## Multi-Material Handling

### Material Transition Detection

```csharp
struct MaterialTransition
{
    public byte material0;
    public byte material1;
    public float3 transitionPlane; // Plane equation (nx, ny, nz, d)
    public float sharpness;         // 0 = smooth, 1 = sharp
}

// Detect material transitions in a cell
MaterialTransition DetectMaterialTransition(int3 cellPos)
{
    // Sample materials at corners and edges
    // Fit plane to material boundary
    // Determine transition sharpness from gradient
}
```

### Enhanced Vertex Attributes

```csharp
public struct DualContouringVertex
{
    public float3 position;
    public float3 normal;
    public float4 tangent;      // xyz = material transition plane normal, w = plane distance
    public Color32 materials;   // R,G = material IDs, B = blend, A = transition sharpness
}
```

## Shader System Redesign

### Vertex Shader Enhancements

```hlsl
struct VertexInput
{
    float3 position : POSITION;
    float3 normal : NORMAL;
    float4 tangent : TANGENT;     // Material plane
    float4 color : COLOR;          // Material data
};

struct VertexOutput
{
    float4 position : SV_POSITION;
    float3 worldPos : TEXCOORD0;
    float3 worldNormal : TEXCOORD1;
    float4 materialPlane : TEXCOORD2;  // xyz = normal, w = distance
    float4 materialData : TEXCOORD3;   // xy = materials, z = blend, w = sharpness
};

VertexOutput VertexMain(VertexInput input)
{
    VertexOutput output;
    
    // Standard transformations
    output.position = TransformObjectToHClip(input.position);
    output.worldPos = TransformObjectToWorld(input.position);
    output.worldNormal = TransformObjectToWorldNormal(input.normal);
    
    // Material plane in world space
    float3 planeNormal = TransformObjectToWorldDir(input.tangent.xyz);
    float planeDistance = input.tangent.w + dot(planeNormal, output.worldPos);
    output.materialPlane = float4(planeNormal, planeDistance);
    
    // Unpack material data
    output.materialData = input.color;
    
    return output;
}
```

### Fragment Shader with Analytic AA

```hlsl
float4 FragmentMain(VertexOutput input) : SV_Target
{
    // Decode materials
    int mat0 = (int)(input.materialData.x * 255.0);
    int mat1 = (int)(input.materialData.y * 255.0);
    float baseBlend = input.materialData.z;
    float sharpness = input.materialData.w;
    
    // Calculate distance to material plane
    float planeDist = dot(input.materialPlane.xyz, input.worldPos) - input.materialPlane.w;
    
    // Analytic antialiasing
    float pixelSize = length(fwidth(input.worldPos));
    float aa = 1.0 / max(pixelSize, 0.0001);
    
    // Sharp or smooth transition based on sharpness parameter
    float transitionWidth = lerp(2.0, 0.1, sharpness) * pixelSize;
    float blend = smoothstep(-transitionWidth, transitionWidth, planeDist);
    
    // Sample both materials
    float3 albedo0 = SampleMaterial(mat0, input.worldPos, input.worldNormal);
    float3 albedo1 = SampleMaterial(mat1, input.worldPos, input.worldNormal);
    
    // Final blend
    float3 albedo = lerp(albedo0, albedo1, blend);
    
    return float4(albedo, 1.0);
}
```

## Optimization Strategies

### 1. Octree Acceleration Structure

```csharp
public struct OctreeNode
{
    public int3 minCorner;
    public int level;
    public byte childMask;
    public int firstChild;
    public DualContouringCell cell;
}

// Adaptive resolution based on surface complexity
public struct AdaptiveDualContouring
{
    public NativeArray<OctreeNode> nodes;
    public NativeMultiHashMap<int3, int> nodeMap;
    
    // Build octree from SDF
    void BuildOctree()
    {
        // Bottom-up construction
        // Merge cells with similar normals/materials
    }
}
```

### 2. SIMD Optimization

```csharp
// Vectorized edge processing
[BurstCompile]
static void ProcessEdgesSIMD(
    [NoAlias] float* sdf,
    [NoAlias] HermiteEdge* edges,
    int startIdx)
{
    // Load 4 edges at once
    var v0 = load_ps(sdf + startIdx);
    var v1 = load_ps(sdf + startIdx + 1);
    
    // Check sign changes
    var signs = _mm_mul_ps(v0, v1);
    var mask = _mm_cmplt_ps(signs, _mm_setzero_ps());
    
    // Process edges with sign changes
    // ...
}
```

### 3. GPU Acceleration

```csharp
// Compute shader for parallel QEF solving
[numthreads(8, 8, 8)]
void SolveQEFCompute(uint3 id : SV_DispatchThreadID)
{
    // Each thread handles one cell
    QEFData qef = GatherHermiteData(id);
    float3 vertex = SolveQEF(qef);
    cells[Index3D(id)] = CreateCell(vertex, qef);
}
```

## Migration Plan

### Phase 1: Core Algorithm (2 weeks)
1. Implement Hermite data extraction
2. Basic QEF solver
3. Simple quad generation
4. Unit tests for each component

### Phase 2: Multi-Material Support (1 week)
1. Material transition detection
2. Enhanced vertex attributes
3. Update shader system
4. Material boundary tests

### Phase 3: Optimization (2 weeks)
1. SIMD optimization for edge processing
2. Parallel QEF solving
3. Memory layout optimization
4. Performance benchmarks

### Phase 4: Advanced Features (2 weeks)
1. Octree adaptive resolution
2. Sharp feature detection
3. T-junction handling
4. LOD system

### Phase 5: Integration (1 week)
1. Replace existing Surface Nets
2. Update editor tools
3. Migration guides
4. Performance validation

## Testing Strategy

### Unit Tests
```csharp
[Test]
public void TestQEFSolver_PlanarSurface_ReturnsCorrectVertex()
{
    // Setup plane intersection data
    var qef = new QEFData();
    // ... setup ...
    
    var result = SolveQEF(qef, int3.zero);
    
    Assert.That(result, Is.EqualTo(expectedVertex).Within(0.001f));
}

[Test]
public void TestHermiteExtraction_CubeCorner_PreservesSharpFeature()
{
    // Setup SDF with cube corner
    // Verify sharp feature is preserved
}
```

### Integration Tests
- Sphere test: Smooth surface reproduction
- Cube test: Sharp edge preservation  
- Material boundary test: Clean transitions
- Performance test: < 5ms for 32³ chunk

### Visual Tests
- Wireframe overlay showing dual contouring grid
- Material ID visualization
- Normal direction debug view
- QEF error heat map

## Performance Targets

- **Hermite Extraction**: < 1ms per 32³ chunk
- **QEF Solving**: < 2ms per 32³ chunk  
- **Mesh Generation**: < 1ms per 32³ chunk
- **Total**: < 5ms per chunk (200 chunks/second)
- **Memory**: ~2MB per chunk (including all acceleration structures)

## Risks and Mitigations

### Risk 1: QEF Solver Numerical Instability
- **Mitigation**: Regularization, fallback to mass point, SVD for difficult cases

### Risk 2: Non-Manifold Geometry
- **Mitigation**: Constrain vertices to cell bounds, enforce one vertex per cell

### Risk 3: Performance Regression
- **Mitigation**: Incremental migration, performance benchmarks, SIMD optimization

### Risk 4: Material Boundary Artifacts
- **Mitigation**: Analytic antialiasing, transition plane fitting, artist controls

## Success Criteria

1. **Quality**: Sharp features preserved, no visible aliasing
2. **Performance**: Maintains 60 FPS with 100+ visible chunks
3. **Compatibility**: Drop-in replacement for Surface Nets
4. **Robustness**: Handles all edge cases without crashes
5. **Usability**: Clear documentation and migration path

## References

1. Ju, T., Losasso, F., Schaefer, S., & Warren, J. (2002). "Dual contouring of hermite data"
2. Schaefer, S., & Warren, J. (2004). "Dual marching cubes: Primal contouring of dual grids"
3. Zhang, N., et al. (2004). "Dual contouring with topology-preserving simplification"
4. Chen, Z., et al. (2022). "Neural Dual Contouring"
5. Unity Burst Compiler documentation
6. Unity Job System best practices

## Appendix: Implementation Checklist

- [ ] Core data structures defined
- [ ] Hermite extraction job implemented
- [ ] QEF solver working with unit tests
- [ ] Basic mesh generation functional
- [ ] Multi-material support added
- [ ] Shader system updated
- [ ] SIMD optimizations applied
- [ ] Performance targets met
- [ ] Editor integration complete
- [ ] Documentation written
- [ ] Migration guide published
