# QEF Solver Implementation for Dual Contouring

## Overview

The Quadratic Error Function (QEF) solver is the mathematical core of the Dual Contouring algorithm. It determines the optimal vertex position within each voxel cell by minimizing the squared distances to all intersecting surface planes. This document provides a detailed implementation guide optimized for Unity's Burst compiler.

## Mathematical Foundation

### Problem Statement

Given a set of planes defined by points **p**ᵢ and normals **n**ᵢ, find vertex position **v** that minimizes:

```
E(v) = Σᵢ (nᵢ · (v - pᵢ))²
```

This is the sum of squared distances from **v** to each plane.

### Least Squares Formulation

Expanding the error function:

```
E(v) = Σᵢ (nᵢ · v - nᵢ · pᵢ)²
```

Let dᵢ = **n**ᵢ · **p**ᵢ (plane distance from origin), then:

```
E(v) = Σᵢ (nᵢ · v - dᵢ)²
```

This is a standard least squares problem: **A**·**v** = **b**, where:
- **A** is the matrix with normals as rows
- **b** is the vector of distances dᵢ

### Normal Equations

The solution minimizes ||**A**·**v** - **b**||². Using normal equations:

```
AᵀA·v = Aᵀb
```

Where:
- **A**ᵀ**A** is a 3×3 symmetric matrix
- **A**ᵀ**b** is a 3×1 vector

## Implementation Details

### Data Structures

```csharp
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
public struct PlaneData
{
    public float3 point;    // Point on the plane
    public float3 normal;   // Unit normal vector
}

[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
public struct QEFData
{
    // Upper triangular part of symmetric matrix AᵀA
    public float a11, a12, a13;
    public float      a22, a23;
    public float           a33;
    
    // Right-hand side vector Aᵀb
    public float3 b;
    
    // Mass point (average of intersection points)
    public float3 massPoint;
    
    // Number of planes
    public int numPlanes;
}
```

### Building the QEF System

```csharp
[BurstCompile]
public static QEFData BuildQEFData(NativeArray<PlaneData> planes, int count)
{
    QEFData qef = new QEFData();
    
    // Initialize mass point accumulator
    float3 massPointSum = float3.zero;
    
    // Build AᵀA and Aᵀb incrementally
    for (int i = 0; i < count; i++)
    {
        float3 n = planes[i].normal;
        float3 p = planes[i].point;
        
        // Add to mass point
        massPointSum += p;
        
        // Calculate plane distance
        float d = dot(n, p);
        
        // Update upper triangular part of AᵀA
        qef.a11 += n.x * n.x;
        qef.a12 += n.x * n.y;
        qef.a13 += n.x * n.z;
        qef.a22 += n.y * n.y;
        qef.a23 += n.y * n.z;
        qef.a33 += n.z * n.z;
        
        // Update Aᵀb
        qef.b += n * d;
    }
    
    // Calculate mass point
    qef.massPoint = massPointSum / max(count, 1);
    qef.numPlanes = count;
    
    return qef;
}
```

### Solving the System

#### Method 1: Direct Inversion (Fast but less stable)

```csharp
[BurstCompile]
public static float3 SolveQEF_Direct(QEFData qef)
{
    // Reconstruct full symmetric matrix
    float3x3 ATA = new float3x3(
        qef.a11, qef.a12, qef.a13,
        qef.a12, qef.a22, qef.a23,
        qef.a13, qef.a23, qef.a33
    );
    
    // Add regularization for numerical stability
    const float REGULARIZATION = 1e-6f;
    ATA.c0.x += REGULARIZATION;
    ATA.c1.y += REGULARIZATION;
    ATA.c2.z += REGULARIZATION;
    
    // Calculate determinant
    float det = determinant(ATA);
    
    // Check if matrix is singular
    if (abs(det) < 1e-10f)
    {
        // Fall back to mass point
        return qef.massPoint;
    }
    
    // Solve using matrix inverse
    float3x3 inv = inverse(ATA);
    float3 solution = mul(inv, qef.b);
    
    return solution;
}
```

#### Method 2: Cholesky Decomposition (More stable)

```csharp
[BurstCompile]
public static float3 SolveQEF_Cholesky(QEFData qef)
{
    // Add regularization
    const float REG = 1e-6f;
    float a11 = qef.a11 + REG;
    float a22 = qef.a22 + REG;
    float a33 = qef.a33 + REG;
    
    // Cholesky decomposition: A = L·Lᵀ
    float l11 = sqrt(a11);
    if (l11 < 1e-10f) return qef.massPoint;
    
    float l21 = qef.a12 / l11;
    float l31 = qef.a13 / l11;
    
    float l22 = sqrt(a22 - l21 * l21);
    if (l22 < 1e-10f) return qef.massPoint;
    
    float l32 = (qef.a23 - l31 * l21) / l22;
    
    float l33 = sqrt(a33 - l31 * l31 - l32 * l32);
    if (l33 < 1e-10f) return qef.massPoint;
    
    // Forward substitution: L·y = b
    float y1 = qef.b.x / l11;
    float y2 = (qef.b.y - l21 * y1) / l22;
    float y3 = (qef.b.z - l31 * y1 - l32 * y2) / l33;
    
    // Back substitution: Lᵀ·x = y
    float x3 = y3 / l33;
    float x2 = (y2 - l32 * x3) / l22;
    float x1 = (y1 - l21 * x2 - l31 * x3) / l11;
    
    return float3(x1, x2, x3);
}
```

#### Method 3: SVD-based (Most robust but slower)

```csharp
[BurstCompile]
public static float3 SolveQEF_SVD(QEFData qef)
{
    // For small systems, we can use an optimized 3x3 SVD
    float3x3 ATA = new float3x3(
        qef.a11, qef.a12, qef.a13,
        qef.a12, qef.a22, qef.a23,
        qef.a13, qef.a23, qef.a33
    );
    
    // Compute eigenvalues using characteristic polynomial
    float3 eigenvalues = ComputeEigenvalues3x3(ATA);
    
    // Check condition number
    float conditionNumber = eigenvalues.x / max(eigenvalues.z, 1e-10f);
    if (conditionNumber > 1e6f)
    {
        // Ill-conditioned system
        return qef.massPoint;
    }
    
    // Compute eigenvectors
    float3x3 V = ComputeEigenvectors3x3(ATA, eigenvalues);
    
    // Compute pseudoinverse: A⁺ = V·Σ⁺·Vᵀ
    float3 sigma_inv = 1.0f / max(eigenvalues, 1e-10f);
    float3x3 A_pseudo = mul(mul(V, float3x3(
        sigma_inv.x, 0, 0,
        0, sigma_inv.y, 0,
        0, 0, sigma_inv.z
    )), transpose(V));
    
    return mul(A_pseudo, qef.b);
}
```

### Constrained QEF Solver

Often we need to constrain the solution to lie within the voxel cell bounds:

```csharp
[BurstCompile]
public static float3 SolveQEFConstrained(
    QEFData qef, 
    float3 minBounds, 
    float3 maxBounds)
{
    // First try unconstrained solution
    float3 unconstrained = SolveQEF_Cholesky(qef);
    
    // Check if solution is within bounds
    if (all(unconstrained >= minBounds) && all(unconstrained <= maxBounds))
    {
        return unconstrained;
    }
    
    // Project to bounds
    float3 clamped = clamp(unconstrained, minBounds, maxBounds);
    
    // Optional: Gradient projection for better results
    float3 gradient = ComputeQEFGradient(qef, clamped);
    float3 projected = clamped - gradient * 0.1f;
    projected = clamp(projected, minBounds, maxBounds);
    
    // Choose solution with lower error
    float error1 = EvaluateQEF(qef, clamped);
    float error2 = EvaluateQEF(qef, projected);
    
    return error1 < error2 ? clamped : projected;
}

[BurstCompile]
static float EvaluateQEF(QEFData qef, float3 v)
{
    // E(v) = vᵀ·AᵀA·v - 2·vᵀ·Aᵀb + const
    float3 Av = float3(
        qef.a11 * v.x + qef.a12 * v.y + qef.a13 * v.z,
        qef.a12 * v.x + qef.a22 * v.y + qef.a23 * v.z,
        qef.a13 * v.x + qef.a23 * v.y + qef.a33 * v.z
    );
    
    return dot(v, Av) - 2f * dot(v, qef.b);
}
```

### Numerical Stability Improvements

#### 1. Preconditioning

```csharp
[BurstCompile]
public static QEFData PreconditionQEF(QEFData qef)
{
    // Scale normals to improve conditioning
    float maxDiag = max(max(qef.a11, qef.a22), qef.a33);
    if (maxDiag > 1e-10f)
    {
        float scale = 1f / sqrt(maxDiag);
        
        qef.a11 *= scale * scale;
        qef.a12 *= scale * scale;
        qef.a13 *= scale * scale;
        qef.a22 *= scale * scale;
        qef.a23 *= scale * scale;
        qef.a33 *= scale * scale;
        qef.b *= scale;
    }
    
    return qef;
}
```

#### 2. Iterative Refinement

```csharp
[BurstCompile]
public static float3 SolveQEFIterative(QEFData qef, int maxIterations = 5)
{
    float3 x = qef.massPoint; // Initial guess
    
    for (int iter = 0; iter < maxIterations; iter++)
    {
        // Compute residual: r = b - A·x
        float3 Ax = float3(
            qef.a11 * x.x + qef.a12 * x.y + qef.a13 * x.z,
            qef.a12 * x.x + qef.a22 * x.y + qef.a23 * x.z,
            qef.a13 * x.x + qef.a23 * x.y + qef.a33 * x.z
        );
        float3 residual = qef.b - Ax;
        
        // Check convergence
        if (length(residual) < 1e-6f)
            break;
        
        // Solve for correction
        QEFData correctionQEF = qef;
        correctionQEF.b = residual;
        float3 delta = SolveQEF_Direct(correctionQEF);
        
        // Update solution
        x += delta;
    }
    
    return x;
}
```

## Optimization Strategies

### SIMD Optimization

```csharp
[BurstCompile]
public static void BatchSolveQEF(
    [NoAlias] NativeArray<QEFData> qefData,
    [NoAlias] NativeArray<float3> solutions,
    int count)
{
    // Process 4 QEF systems at once using SIMD
    int simdCount = count & ~3; // Round down to multiple of 4
    
    for (int i = 0; i < simdCount; i += 4)
    {
        // Load 4 QEF systems
        float4 a11 = new float4(
            qefData[i].a11, qefData[i+1].a11, 
            qefData[i+2].a11, qefData[i+3].a11);
        // ... load other components
        
        // Solve 4 systems simultaneously
        // ... SIMD operations
        
        // Store results
        solutions[i] = /* result 0 */;
        solutions[i+1] = /* result 1 */;
        solutions[i+2] = /* result 2 */;
        solutions[i+3] = /* result 3 */;
    }
    
    // Handle remaining systems
    for (int i = simdCount; i < count; i++)
    {
        solutions[i] = SolveQEF_Direct(qefData[i]);
    }
}
```

### Caching and Reuse

```csharp
public struct QEFCache
{
    NativeHashMap<uint, float3> solvedVertices;
    NativeHashMap<uint, QEFData> qefSystems;
    
    public float3 GetOrSolve(uint cellKey, QEFData qef)
    {
        if (solvedVertices.TryGetValue(cellKey, out float3 cached))
        {
            return cached;
        }
        
        float3 solution = SolveQEFConstrained(
            qef, 
            float3.zero, 
            float3(1, 1, 1)
        );
        
        solvedVertices[cellKey] = solution;
        qefSystems[cellKey] = qef;
        
        return solution;
    }
}
```

## Error Analysis

### Condition Number Estimation

```csharp
[BurstCompile]
public static float EstimateConditionNumber(QEFData qef)
{
    // Quick estimate using matrix trace and determinant
    float trace = qef.a11 + qef.a22 + qef.a33;
    float det = qef.a11 * (qef.a22 * qef.a33 - qef.a23 * qef.a23)
              - qef.a12 * (qef.a12 * qef.a33 - qef.a13 * qef.a23)
              + qef.a13 * (qef.a12 * qef.a23 - qef.a13 * qef.a22);
    
    if (abs(det) < 1e-10f)
        return float.PositiveInfinity;
    
    // Rough approximation of condition number
    float normA = sqrt(trace);
    float normAinv = 1f / cbrt(abs(det));
    
    return normA * normAinv;
}
```

### Error Metrics

```csharp
public struct QEFSolution
{
    public float3 position;
    public float error;           // Residual error
    public float conditionNumber; // Matrix condition
    public bool isStable;         // Numerical stability flag
}

[BurstCompile]
public static QEFSolution SolveQEFWithMetrics(QEFData qef)
{
    QEFSolution result;
    
    // Estimate condition number
    result.conditionNumber = EstimateConditionNumber(qef);
    result.isStable = result.conditionNumber < 1e4f;
    
    // Solve system
    if (result.isStable)
    {
        result.position = SolveQEF_Cholesky(qef);
    }
    else
    {
        // Use more robust method for ill-conditioned systems
        result.position = SolveQEF_SVD(qef);
    }
    
    // Calculate residual error
    result.error = EvaluateQEF(qef, result.position);
    
    return result;
}
```

## Testing Framework

### Unit Tests

```csharp
[Test]
public void TestQEF_SinglePlane()
{
    // Single plane should place vertex on the plane
    var planes = new NativeArray<PlaneData>(1, Allocator.Temp);
    planes[0] = new PlaneData
    {
        point = float3(0.5f, 0.5f, 0.5f),
        normal = float3(0, 1, 0)
    };
    
    var qef = BuildQEFData(planes, 1);
    var solution = SolveQEF_Direct(qef);
    
    // Verify vertex is on the plane
    float distance = dot(solution - planes[0].point, planes[0].normal);
    Assert.That(distance, Is.EqualTo(0).Within(1e-6f));
    
    planes.Dispose();
}

[Test]
public void TestQEF_ThreeOrthogonalPlanes()
{
    // Three orthogonal planes should intersect at a point
    var planes = new NativeArray<PlaneData>(3, Allocator.Temp);
    
    float3 intersection = float3(0.3f, 0.6f, 0.8f);
    planes[0] = new PlaneData
    {
        point = intersection,
        normal = float3(1, 0, 0)
    };
    planes[1] = new PlaneData
    {
        point = intersection,
        normal = float3(0, 1, 0)
    };
    planes[2] = new PlaneData
    {
        point = intersection,
        normal = float3(0, 0, 1)
    };
    
    var qef = BuildQEFData(planes, 3);
    var solution = SolveQEF_Direct(qef);
    
    Assert.That(solution, Is.EqualTo(intersection).Within(1e-6f));
    
    planes.Dispose();
}

[Test]
public void TestQEF_ParallelPlanes()
{
    // Parallel planes should result in mass point solution
    var planes = new NativeArray<PlaneData>(2, Allocator.Temp);
    
    planes[0] = new PlaneData
    {
        point = float3(0, 0.3f, 0),
        normal = float3(0, 1, 0)
    };
    planes[1] = new PlaneData
    {
        point = float3(0, 0.7f, 0),
        normal = float3(0, 1, 0)
    };
    
    var qef = BuildQEFData(planes, 2);
    var solution = SolveQEF_Direct(qef);
    
    // Should return mass point
    float3 expectedMassPoint = (planes[0].point + planes[1].point) / 2f;
    Assert.That(solution.y, Is.EqualTo(expectedMassPoint.y).Within(0.01f));
    
    planes.Dispose();
}
```

### Performance Benchmarks

```csharp
[BurstCompile]
public struct QEFBenchmarkJob : IJob
{
    [ReadOnly] public NativeArray<QEFData> systems;
    [WriteOnly] public NativeArray<float3> solutions;
    
    public void Execute()
    {
        for (int i = 0; i < systems.Length; i++)
        {
            solutions[i] = SolveQEF_Direct(systems[i]);
        }
    }
}

[Test]
[Performance]
public void BenchmarkQEFSolver()
{
    const int COUNT = 10000;
    
    var systems = new NativeArray<QEFData>(COUNT, Allocator.TempJob);
    var solutions = new NativeArray<float3>(COUNT, Allocator.TempJob);
    
    // Initialize with random QEF systems
    InitializeRandomQEFSystems(systems);
    
    var job = new QEFBenchmarkJob
    {
        systems = systems,
        solutions = solutions
    };
    
    // Measure performance
    Measure.Method(() =>
    {
        job.Schedule().Complete();
    })
    .WarmupCount(10)
    .MeasurementCount(100)
    .Run();
    
    systems.Dispose();
    solutions.Dispose();
}
```

## Integration Example

```csharp
// Example integration with dual contouring pipeline
[BurstCompile]
public struct DualContouringCellProcessor : IJobParallelFor
{
    [ReadOnly] public NativeArray<HermiteEdge> edges;
    [WriteOnly] public NativeArray<float3> vertices;
    
    public void Execute(int cellIndex)
    {
        // Gather Hermite data for this cell
        var planes = new NativeArray<PlaneData>(12, Allocator.Temp);
        int planeCount = 0;
        
        // Check all 12 edges of the cell
        for (int e = 0; e < 12; e++)
        {
            int edgeIndex = GetEdgeIndex(cellIndex, e);
            if (IsEdgeActive(edgeIndex))
            {
                var edge = edges[edgeIndex];
                planes[planeCount++] = new PlaneData
                {
                    point = edge.position,
                    normal = edge.normal
                };
            }
        }
        
        if (planeCount > 0)
        {
            // Build and solve QEF
            var qef = BuildQEFData(planes, planeCount);
            float3 cellMin = GetCellMin(cellIndex);
            float3 cellMax = cellMin + 1f;
            
            vertices[cellIndex] = SolveQEFConstrained(
                qef, cellMin, cellMax
            );
        }
        else
        {
            // No active edges - use cell center
            vertices[cellIndex] = GetCellCenter(cellIndex);
        }
        
        planes.Dispose();
    }
}
```

## Conclusion

The QEF solver is a critical component that directly impacts the quality of dual contouring meshes. This implementation provides multiple solution methods with different trade-offs between speed and robustness. For production use, the Cholesky method offers a good balance, with SVD as a fallback for difficult cases. Proper preconditioning and constraint handling ensure stable results across a wide range of input data.
