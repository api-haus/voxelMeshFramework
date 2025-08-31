using System.Runtime.CompilerServices;
using Unity.Burst;

[assembly: BurstCompile(CompileSynchronously = true)]
[assembly: InternalsVisibleTo("Voxels.Runtime")]
[assembly: InternalsVisibleTo("Voxels.Editor.Tests")]
