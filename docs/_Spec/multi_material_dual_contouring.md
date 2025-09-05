# Multi-Material Dual Contouring Implementation

## Overview

Multi-material dual contouring extends the standard algorithm to handle volumes containing multiple materials with clean, artifact-free boundaries. This document presents a comprehensive approach that preserves sharp material transitions while maintaining the benefits of dual contouring's feature preservation.

## Challenges in Multi-Material Meshing

### Core Issues

1. **Material Boundaries**: Need to accurately represent interfaces between different materials
2. **Vertex Placement**: Standard QEF doesn't account for material constraints
3. **Triangle Assignment**: Each triangle must belong to exactly one material
4. **Transition Quality**: Avoid jagged or inconsistent material boundaries
5. **Performance**: Multi-material processing adds computational overhead

### Traditional Approaches and Limitations

- **Marching Cubes**: Creates duplicate vertices at boundaries, leading to cracks
- **Surface Nets**: Blends materials in vertex colors, causing visual artifacts
- **Simple Dual Contouring**: Ignores materials during QEF solving

## Proposed Solution Architecture

### Enhanced Data Structures

```csharp
// Extended Hermite edge data for multi-material support
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
public struct MultiMaterialHermiteEdge
{
    // Standard Hermite data
    public float3 position;          // Intersection point
    public float3 normal;            // Surface normal
    public float t;                  // Parametric position [0,1]
    
    // Material data
    public byte material0;           // Material on negative side
    public byte material1;           // Material on positive side
    public byte transitionType;      // Sharp, smooth, or special
    public byte flags;               // Additional flags
    
    // Extended data for complex boundaries
    public float3 materialGradient;  // Direction of material change
    public float transitionWidth;    // Width of transition zone
}

// Material transition types
public enum MaterialTransitionType : byte
{
    Sharp = 0,          // Distinct boundary
    Smooth = 1,         // Gradual blend
    Layered = 2,        // Stratified materials
    Procedural = 3      // Custom transition function
}
```

### Multi-Material Cell Data

```csharp
public struct MultiMaterialCell
{
    // Primary surface data
    public float3 position;          // Vertex position from QEF
    public float3 normal;            // Surface normal
    
    // Material assignment
    public byte dominantMaterial;    // Primary material for this cell
    public byte materialCount;       // Number of materials in cell
    public fixed byte materials[4];  // Up to 4 materials per cell
    public fixed float weights[4];   // Material weights
    
    // Boundary information
    public float3 boundaryPlane;     // Plane equation for material boundary
    public float boundaryDistance;   // Distance to nearest boundary
    public byte boundaryType;        // Type of material boundary
}
```

## Material-Aware Edge Detection

### Enhanced Edge Processing

```csharp
[BurstCompile]
public struct DetectMultiMaterialEdgesJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<sbyte> sdf;
    [ReadOnly] public NativeArray<byte> materials;
    [ReadOnly] public NativeArray<float> materialBlend; // Optional blend field
    
    [WriteOnly] public NativeArray<MultiMaterialHermiteEdge> edges;
    [WriteOnly] public NativeArray<uint> edgeValid;
    
    public void Execute(int index)
    {
        int3 p0 = LinearTo3D(index);
        int3 p1 = p0 + int3(1, 0, 0); // X-edge example
        
        float v0 = sdf[Index3D(p0)];
        float v1 = sdf[Index3D(p1)];
        
        // Check for surface crossing
        if (v0 * v1 < 0)
        {
            // Standard intersection calculation
            float t = v0 / (v0 - v1);
            float3 pos = lerp((float3)p0, (float3)p1, t);
            
            // Get materials
            byte mat0 = materials[Index3D(p0)];
            byte mat1 = materials[Index3D(p1)];
            
            // Calculate normal with material awareness
            float3 normal = CalculateMaterialAwareNormal(p0, p1, t, mat0, mat1);
            
            // Determine transition type
            var transitionType = DetermineTransitionType(mat0, mat1, p0, p1);
            
            // Calculate material gradient
            float3 matGradient = CalculateMaterialGradient(p0, p1, materials);
            
            // Store enhanced edge data
            edges[index] = new MultiMaterialHermiteEdge
            {
                position = pos * voxelSize,
                normal = normal,
                t = t,
                material0 = v0 < 0 ? mat0 : mat1,  // Material on inside
                material1 = v0 < 0 ? mat1 : mat0,  // Material on outside
                transitionType = transitionType,
                materialGradient = matGradient,
                transitionWidth = EstimateTransitionWidth(p0, p1, materials)
            };
            
            SetEdgeValid(edgeValid, index);
        }
    }
    
    float3 CalculateMaterialAwareNormal(int3 p0, int3 p1, float t, byte mat0, byte mat1)
    {
        // If materials differ, bias normal toward material boundary
        if (mat0 != mat1)
        {
            float3 sdfNormal = CalculateSDFNormal(p0, p1, t);
            float3 materialNormal = CalculateMaterialBoundaryNormal(p0, p1);
            
            // Blend based on how close we are to material boundary
            float materialInfluence = 1.0f - abs(t - 0.5f) * 2.0f;
            return normalize(lerp(sdfNormal, materialNormal, materialInfluence * 0.5f));
        }
        
        return CalculateSDFNormal(p0, p1, t);
    }
}
```

### Material Boundary Detection

```csharp
[BurstCompile]
public struct MaterialBoundaryAnalysis
{
    public float3 boundaryNormal;   // Normal of material boundary plane
    public float3 boundaryPoint;    // Point on boundary
    public float confidence;         // Confidence in boundary detection
    public bool isSharp;            // Sharp vs gradual transition
    
    public static MaterialBoundaryAnalysis Analyze(
        NativeArray<byte> materials,
        int3 cellPos)
    {
        // Sample materials in 3x3x3 neighborhood
        var samples = new NativeArray<byte>(27, Allocator.Temp);
        var positions = new NativeArray<float3>(27, Allocator.Temp);
        
        int sampleCount = 0;
        for (int z = -1; z <= 1; z++)
        {
            for (int y = -1; y <= 1; y++)
            {
                for (int x = -1; x <= 1; x++)
                {
                    int3 p = cellPos + int3(x, y, z);
                    samples[sampleCount] = materials[Index3D(p)];
                    positions[sampleCount] = (float3)p;
                    sampleCount++;
                }
            }
        }
        
        // Find dominant materials
        var materialCounts = new NativeArray<int>(256, Allocator.Temp);
        for (int i = 0; i < 27; i++)
        {
            materialCounts[samples[i]]++;
        }
        
        // Identify two most common materials
        byte mat0 = 0, mat1 = 0;
        int count0 = 0, count1 = 0;
        for (int i = 0; i < 256; i++)
        {
            if (materialCounts[i] > count0)
            {
                mat1 = mat0;
                count1 = count0;
                mat0 = (byte)i;
                count0 = materialCounts[i];
            }
            else if (materialCounts[i] > count1)
            {
                mat1 = (byte)i;
                count1 = materialCounts[i];
            }
        }
        
        // Fit plane to material boundary
        var result = FitBoundaryPlane(samples, positions, mat0, mat1);
        
        samples.Dispose();
        positions.Dispose();
        materialCounts.Dispose();
        
        return result;
    }
    
    static MaterialBoundaryAnalysis FitBoundaryPlane(
        NativeArray<byte> samples,
        NativeArray<float3> positions,
        byte mat0, byte mat1)
    {
        // Use PCA or least squares to fit plane
        float3 center = float3.zero;
        int boundaryCount = 0;
        
        // Find positions near material boundary
        for (int i = 0; i < samples.Length; i++)
        {
            if (samples[i] == mat0 || samples[i] == mat1)
            {
                // Check if neighbors have different material
                bool nearBoundary = false;
                // ... neighbor checking logic ...
                
                if (nearBoundary)
                {
                    center += positions[i];
                    boundaryCount++;
                }
            }
        }
        
        if (boundaryCount == 0)
        {
            return new MaterialBoundaryAnalysis
            {
                confidence = 0
            };
        }
        
        center /= boundaryCount;
        
        // Compute covariance matrix
        float3x3 covariance = float3x3.zero;
        // ... PCA computation ...
        
        // Extract plane normal (smallest eigenvector)
        float3 normal = ComputePlaneNormal(covariance);
        
        return new MaterialBoundaryAnalysis
        {
            boundaryNormal = normal,
            boundaryPoint = center,
            confidence = boundaryCount / 27f,
            isSharp = boundaryCount < 10 // Heuristic for sharpness
        };
    }
}
```

## Material-Constrained QEF Solving

### Enhanced QEF with Material Constraints

```csharp
[BurstCompile]
public struct MaterialConstrainedQEF
{
    // Standard QEF data
    public float3x3 ATA;
    public float3 ATb;
    public float3 massPoint;
    public int planeCount;
    
    // Material constraint data
    public float3 materialBoundaryNormal;
    public float3 materialBoundaryPoint;
    public float materialConstraintWeight;
    public bool hasMaterialConstraint;
    
    public float3 Solve(float3 minBounds, float3 maxBounds)
    {
        if (hasMaterialConstraint)
        {
            // Add material boundary as soft constraint
            float weight = materialConstraintWeight;
            float3 n = materialBoundaryNormal;
            float d = dot(n, materialBoundaryPoint);
            
            // Update normal equations
            ATA += weight * float3x3(
                n.x * n.x, n.x * n.y, n.x * n.z,
                n.y * n.x, n.y * n.y, n.y * n.z,
                n.z * n.x, n.z * n.y, n.z * n.z
            );
            
            ATb += weight * n * d;
        }
        
        // Solve enhanced system
        return SolveConstrainedQEF(ATA, ATb, massPoint, minBounds, maxBounds);
    }
}

[BurstCompile]
public struct SolveMultiMaterialCellJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<MultiMaterialHermiteEdge> edges;
    [ReadOnly] public NativeArray<uint> edgeValid;
    [ReadOnly] public NativeArray<byte> materials;
    
    [WriteOnly] public NativeArray<MultiMaterialCell> cells;
    
    public void Execute(int cellIndex)
    {
        int3 cellPos = LinearTo3D(cellIndex);
        
        // Gather edge data for this cell
        var cellEdges = GatherCellEdges(cellPos);
        if (cellEdges.activeCount == 0)
        {
            cells[cellIndex] = default;
            return;
        }
        
        // Analyze materials in cell
        var materialAnalysis = AnalyzeCellMaterials(cellPos, cellEdges);
        
        // Build QEF with material constraints
        var qef = BuildMaterialConstrainedQEF(cellEdges, materialAnalysis);
        
        // Solve for vertex position
        float3 minBounds = (float3)cellPos;
        float3 maxBounds = minBounds + 1f;
        float3 position = qef.Solve(minBounds, maxBounds);
        
        // Create cell data
        cells[cellIndex] = new MultiMaterialCell
        {
            position = position,
            normal = ComputeCellNormal(cellEdges),
            dominantMaterial = materialAnalysis.dominantMaterial,
            materialCount = materialAnalysis.materialCount,
            materials = materialAnalysis.materials,
            weights = materialAnalysis.weights,
            boundaryPlane = materialAnalysis.boundaryPlane,
            boundaryDistance = materialAnalysis.boundaryDistance,
            boundaryType = materialAnalysis.boundaryType
        };
    }
}
```

## Triangle Generation with Material Assignment

### Material-Aware Quad Generation

```csharp
[BurstCompile]
public struct GenerateMultiMaterialMeshJob : IJob
{
    [ReadOnly] public NativeArray<MultiMaterialCell> cells;
    [ReadOnly] public NativeArray<byte> materials;
    
    // Per-material output buffers
    public NativeList<float3> vertices;
    public NativeList<float3> normals;
    public NativeList<int> indices;
    public NativeList<byte> triangleMaterials;
    
    // Material boundary vertices
    public NativeList<float3> boundaryVertices;
    public NativeList<float3> boundaryNormals;
    public NativeList<int> boundaryIndices;
    
    public void Execute()
    {
        var vertexMap = new NativeHashMap<int, int>(cells.Length, Allocator.Temp);
        var materialVertexMaps = new NativeHashMap<ulong, int>(cells.Length * 4, Allocator.Temp);
        
        // Process each potential quad
        for (int z = 0; z < CHUNK_SIZE - 1; z++)
        {
            for (int y = 0; y < CHUNK_SIZE - 1; y++)
            {
                for (int x = 0; x < CHUNK_SIZE - 1; x++)
                {
                    ProcessQuad(int3(x, y, z), vertexMap, materialVertexMaps);
                }
            }
        }
        
        vertexMap.Dispose();
        materialVertexMaps.Dispose();
    }
    
    void ProcessQuad(int3 basePos, 
        NativeHashMap<int, int> vertexMap,
        NativeHashMap<ulong, int> materialVertexMaps)
    {
        // Get the 4 cells that share this face
        var cellIndices = stackalloc int[4];
        var cellPositions = stackalloc float3[4];
        var cellMaterials = stackalloc byte[4];
        int activeCells = 0;
        
        // Gather active cells for this face
        for (int i = 0; i < 4; i++)
        {
            int3 offset = GetQuadOffset(i);
            int cellIndex = Index3D(basePos + offset);
            
            if (cells[cellIndex].materialCount > 0)
            {
                cellIndices[activeCells] = cellIndex;
                cellPositions[activeCells] = cells[cellIndex].position;
                cellMaterials[activeCells] = cells[cellIndex].dominantMaterial;
                activeCells++;
            }
        }
        
        if (activeCells < 3) return; // Need at least 3 vertices for triangles
        
        // Determine quad material
        byte quadMaterial = DetermineQuadMaterial(cellMaterials, activeCells);
        
        // Check if this is a material boundary quad
        bool isBoundary = IsMaterialBoundaryQuad(cellMaterials, activeCells);
        
        if (isBoundary)
        {
            // Generate special boundary geometry
            GenerateBoundaryQuad(
                cellIndices, cellPositions, cellMaterials, 
                activeCells, materialVertexMaps
            );
        }
        else
        {
            // Generate standard single-material quad
            GenerateStandardQuad(
                cellIndices, cellPositions, 
                activeCells, quadMaterial, vertexMap
            );
        }
    }
    
    void GenerateBoundaryQuad(
        int* cellIndices, float3* positions, byte* materials,
        int count, NativeHashMap<ulong, int> materialVertexMaps)
    {
        // Create vertices for each material at this boundary
        for (int i = 0; i < count; i++)
        {
            byte mat = materials[i];
            ulong key = ((ulong)cellIndices[i] << 8) | mat;
            
            if (!materialVertexMaps.ContainsKey(key))
            {
                int vertexIndex = boundaryVertices.Length;
                materialVertexMaps[key] = vertexIndex;
                
                var cell = cells[cellIndices[i]];
                
                // Adjust vertex position based on material
                float3 adjustedPos = AdjustPositionForMaterial(
                    cell.position, cell.boundaryPlane, mat
                );
                
                boundaryVertices.Add(adjustedPos);
                boundaryNormals.Add(cell.normal);
            }
        }
        
        // Generate triangles ensuring each uses vertices of same material
        // This prevents material bleeding across boundaries
    }
}
```

### Material Transition Mesh Generation

```csharp
public struct MaterialTransitionMesh
{
    // Special mesh data for smooth material transitions
    public NativeList<float3> positions;
    public NativeList<float3> normals;
    public NativeList<float4> tangents;     // xyz = transition direction, w = width
    public NativeList<float4> materialData; // xy = materials, z = blend, w = sharpness
    public NativeList<int> indices;
    
    public void GenerateTransition(
        MultiMaterialCell cell0, MultiMaterialCell cell1,
        float3 edgeStart, float3 edgeEnd)
    {
        // Generate vertices along transition
        int segments = DetermineSegmentCount(cell0, cell1);
        
        for (int i = 0; i <= segments; i++)
        {
            float t = i / (float)segments;
            float3 pos = lerp(edgeStart, edgeEnd, t);
            
            // Calculate blend weight based on position
            float blend = CalculateBlendWeight(pos, cell0, cell1);
            
            // Store vertex with transition data
            positions.Add(pos);
            normals.Add(lerp(cell0.normal, cell1.normal, blend));
            tangents.Add(float4(
                cell0.boundaryPlane, // Transition plane normal
                CalculateTransitionWidth(cell0, cell1)
            ));
            materialData.Add(float4(
                cell0.dominantMaterial / 255f,
                cell1.dominantMaterial / 255f,
                blend,
                cell0.boundaryType == (byte)MaterialTransitionType.Sharp ? 1f : 0f
            ));
        }
    }
}
```

## Optimization Strategies

### Hierarchical Material Clustering

```csharp
public struct MaterialCluster
{
    public AABB bounds;
    public byte dominantMaterial;
    public byte materialCount;
    public fixed byte materials[8];
    public float purity; // 1.0 = single material, 0.0 = maximum mixture
}

[BurstCompile]
public struct BuildMaterialClustersJob : IJob
{
    [ReadOnly] public NativeArray<byte> materials;
    [WriteOnly] public NativeArray<MaterialCluster> clusters;
    
    public void Execute()
    {
        // Build octree of material clusters
        // Merge regions with same material
        // Identify material boundary regions
    }
}
```

### Material Boundary Cache

```csharp
public struct MaterialBoundaryCache
{
    // Cache computed boundary planes between material pairs
    public NativeHashMap<uint, float4> boundaryPlanes; // Key: (mat0 << 8) | mat1
    public NativeHashMap<uint, float> transitionWidths;
    public NativeHashMap<uint, byte> transitionTypes;
    
    public void CacheBoundary(byte mat0, byte mat1, float4 plane, float width, byte type)
    {
        uint key = ((uint)math.min(mat0, mat1) << 8) | math.max(mat0, mat1);
        boundaryPlanes[key] = plane;
        transitionWidths[key] = width;
        transitionTypes[key] = type;
    }
    
    public bool TryGetBoundary(byte mat0, byte mat1, out float4 plane, out float width, out byte type)
    {
        uint key = ((uint)math.min(mat0, mat1) << 8) | math.max(mat0, mat1);
        if (boundaryPlanes.TryGetValue(key, out plane))
        {
            width = transitionWidths[key];
            type = transitionTypes[key];
            return true;
        }
        
        plane = default;
        width = 0;
        type = 0;
        return false;
    }
}
```

## Shader Integration

### Vertex Data Layout

```hlsl
struct MultiMaterialVertexInput
{
    float3 position : POSITION;
    float3 normal : NORMAL;
    float4 tangent : TANGENT;      // xyz = boundary normal, w = boundary distance
    float4 color : COLOR;           // r,g = materials, b = blend, a = sharpness
    float4 texcoord : TEXCOORD0;   // uv, material weights
};

struct MultiMaterialVertexOutput
{
    float4 position : SV_POSITION;
    float3 worldPos : TEXCOORD0;
    float3 worldNormal : TEXCOORD1;
    float4 materialData : TEXCOORD2;    // materials and blend
    float4 boundaryData : TEXCOORD3;    // plane equation
    float2 materialWeights : TEXCOORD4; // Additional blend weights
};
```

### Fragment Shader with Analytic Material Blending

```hlsl
float4 MultiMaterialFragment(MultiMaterialVertexOutput input) : SV_Target
{
    // Decode materials
    int mat0 = (int)(input.materialData.x * 255.0);
    int mat1 = (int)(input.materialData.y * 255.0);
    float baseBlend = input.materialData.z;
    float sharpness = input.materialData.w;
    
    // Calculate distance to boundary plane
    float3 boundaryNormal = input.boundaryData.xyz;
    float boundaryDist = dot(input.worldPos, boundaryNormal) - input.boundaryData.w;
    
    // Analytic antialiasing for material boundaries
    float pixelSize = length(fwidth(input.worldPos));
    float transitionWidth = lerp(pixelSize * 4.0, pixelSize * 0.5, sharpness);
    
    // Smooth transition
    float blend = smoothstep(-transitionWidth, transitionWidth, boundaryDist);
    
    // Sample both materials with triplanar mapping
    MaterialSample sample0 = SampleMaterial(mat0, input.worldPos, input.worldNormal);
    MaterialSample sample1 = SampleMaterial(mat1, input.worldPos, input.worldNormal);
    
    // Height-based blend for more natural transitions
    float heightBlend = HeightBlend(sample0.height, sample1.height, blend, 0.1);
    
    // Final material blend
    float3 albedo = lerp(sample0.albedo, sample1.albedo, heightBlend);
    float3 normal = normalize(lerp(sample0.normal, sample1.normal, heightBlend));
    float roughness = lerp(sample0.roughness, sample1.roughness, heightBlend);
    float metallic = lerp(sample0.metallic, sample1.metallic, heightBlend);
    
    // Apply lighting
    return CalculatePBR(albedo, normal, roughness, metallic, input.worldPos);
}
```

## Advanced Features

### Procedural Material Transitions

```csharp
public interface IMaterialTransition
{
    float4 EvaluateTransition(float3 position, byte mat0, byte mat1);
    float GetTransitionWidth(byte mat0, byte mat1);
    bool RequiresSpecialHandling(byte mat0, byte mat1);
}

public struct LayeredTransition : IMaterialTransition
{
    public float4 EvaluateTransition(float3 position, byte mat0, byte mat1)
    {
        // Stratified transition based on height
        float y = position.y;
        float transitionHeight = GetTransitionHeight(mat0, mat1);
        float blend = smoothstep(transitionHeight - 0.1f, transitionHeight + 0.1f, y);
        
        return float4(0, 1, 0, transitionHeight); // Y-up plane
    }
}

public struct ErosionTransition : IMaterialTransition
{
    public float4 EvaluateTransition(float3 position, byte mat0, byte mat1)
    {
        // Noise-based erosion pattern
        float noise = SimplexNoise3D(position * 0.5f);
        float erosion = saturate(noise * 0.5f + 0.5f);
        
        // Modify transition based on material properties
        if (IsHardMaterial(mat0) && IsSoftMaterial(mat1))
        {
            erosion = pow(erosion, 2f); // Harder material resists more
        }
        
        return float4(normalize(GradientNoise3D(position * 0.5f)), erosion);
    }
}
```

### Material Property Integration

```csharp
[System.Serializable]
public struct MaterialProperties
{
    public byte id;
    public float density;
    public float hardness;
    public float roughness;
    public Color albedo;
    public bool isMetallic;
    public bool isTransparent;
    
    // Transition behavior
    public MaterialTransitionBehavior transitionBehavior;
    public float transitionSharpness;
    public AnimationCurve transitionCurve;
}

public enum MaterialTransitionBehavior
{
    Standard,       // Simple boundary
    Erosion,        // Natural erosion pattern
    Layered,        // Stratified layers
    Blend,          // Smooth blend
    Crystalline,    // Sharp crystalline boundaries
    Organic         // Organic growth patterns
}

public class MaterialDatabase : ScriptableObject
{
    public MaterialProperties[] materials;
    
    public MaterialTransitionType GetTransitionType(byte mat0, byte mat1)
    {
        var prop0 = materials[mat0];
        var prop1 = materials[mat1];
        
        // Determine transition based on material properties
        if (prop0.isMetallic && prop1.isMetallic)
            return MaterialTransitionType.Sharp;
        
        if (prop0.transitionBehavior == MaterialTransitionBehavior.Erosion ||
            prop1.transitionBehavior == MaterialTransitionBehavior.Erosion)
            return MaterialTransitionType.Procedural;
        
        return MaterialTransitionType.Smooth;
    }
}
```

## Performance Profiling

```csharp
public struct MultiMaterialProfilingData
{
    public float edgeDetectionMs;
    public float materialAnalysisMs;
    public float qefSolvingMs;
    public float meshGenerationMs;
    public float boundaryProcessingMs;
    
    public int totalEdges;
    public int materialBoundaryEdges;
    public int generatedVertices;
    public int generatedTriangles;
    public int uniqueMaterials;
    
    public void LogReport()
    {
        Debug.Log($"Multi-Material DC Profile:\n" +
                  $"  Edge Detection: {edgeDetectionMs:F2}ms\n" +
                  $"  Material Analysis: {materialAnalysisMs:F2}ms\n" +
                  $"  QEF Solving: {qefSolvingMs:F2}ms\n" +
                  $"  Mesh Generation: {meshGenerationMs:F2}ms\n" +
                  $"  Boundary Processing: {boundaryProcessingMs:F2}ms\n" +
                  $"  Total: {TotalMs:F2}ms\n" +
                  $"  Edges: {totalEdges} ({materialBoundaryEdges} boundaries)\n" +
                  $"  Output: {generatedVertices} vertices, {generatedTriangles} triangles\n" +
                  $"  Materials: {uniqueMaterials} unique");
    }
    
    public float TotalMs => edgeDetectionMs + materialAnalysisMs + 
                            qefSolvingMs + meshGenerationMs + boundaryProcessingMs;
}
```

## Testing Framework

```csharp
[TestFixture]
public class MultiMaterialDualContouringTests
{
    [Test]
    public void TestSimpleMaterialBoundary()
    {
        // Create volume with two materials separated by plane
        var sdf = new NativeArray<sbyte>(32*32*32, Allocator.Temp);
        var materials = new NativeArray<byte>(32*32*32, Allocator.Temp);
        
        // Fill with plane at y=16
        for (int i = 0; i < sdf.Length; i++)
        {
            int3 pos = LinearTo3D(i, 32);
            sdf[i] = (sbyte)((pos.y - 16) * 10); // SDF for horizontal plane
            materials[i] = pos.y < 16 ? (byte)1 : (byte)2;
        }
        
        // Run dual contouring
        var result = RunMultiMaterialDualContouring(sdf, materials);
        
        // Verify clean material boundary
        Assert.That(result.vertices.Length, Is.GreaterThan(0));
        Assert.That(result.materialBoundaryEdges, Is.GreaterThan(0));
        
        // Check that vertices near boundary have correct materials
        // ...
        
        sdf.Dispose();
        materials.Dispose();
    }
    
    [Test]
    public void TestTripleMaterialJunction()
    {
        // Test case with three materials meeting at a point
        // Verify no cracks or overlaps
    }
    
    [Test]
    public void TestMaterialTransitionSharpness()
    {
        // Verify sharp vs smooth transitions work correctly
    }
}
```

## Conclusion

This multi-material dual contouring system provides:

1. **Clean Boundaries**: No bleeding or aliasing at material interfaces
2. **Flexible Transitions**: Support for sharp, smooth, and procedural transitions
3. **Performance**: Optimized for parallel processing with Burst
4. **Quality**: Analytic antialiasing and height-based blending
5. **Extensibility**: Easy to add new material types and transition behaviors

The implementation maintains the core benefits of dual contouring while properly handling the complexities of multi-material volumes, resulting in high-quality meshes suitable for games and visualization.
