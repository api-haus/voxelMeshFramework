using System.Text;
using Unity.Entities;
using UnityEngine;
using Voxels.Core.Concurrency;

namespace Voxels.Diagnostics
{
	[AddComponentMenu("Voxels/System Diagnostics")]
	public class VoxelSystemDiagnostics : MonoBehaviour
	{
		[Header("Debug")]
		[SerializeField]
		bool logOnAwake = true;

		[SerializeField]
		bool logOnUpdate = false;

		void Awake()
		{
			if (logOnAwake)
				LogSystemDiagnostics();
		}

		void Update()
		{
			if (logOnUpdate)
				LogSystemDiagnostics();
		}

		[ContextMenu("Log System Diagnostics")]
		public void LogSystemDiagnostics()
		{
			var world = World.DefaultGameObjectInjectionWorld;
			if (world == null || !world.IsCreated)
			{
				Debug.LogError(
					"[VoxelSystemDiagnostics] DefaultGameObjectInjectionWorld is null or not created!"
				);
				return;
			}

			var sb = new StringBuilder();
			sb.AppendLine("=== VOXEL SYSTEM DIAGNOSTICS ===");
			sb.AppendLine($"World: {world.Name}");
			sb.AppendLine($"System Count: {world.Systems.Count}");

			// Check for critical voxel systems
			CheckForSystem<Voxels.Core.Config.VoxelProjectBootstrapSystem>(world, sb);
			CheckForSystem<Voxels.Core.Concurrency.VoxelJobFenceRegistrySystem>(world, sb);
			CheckForManagedSystem<Voxels.Core.Meshing.Systems.ManagedVoxelMeshingSystem>(world, sb);
			CheckForManagedSystem<Voxels.Core.Hybrid.EntityGameObjectTransformSystem>(world, sb);
			CheckForSystem<Voxels.Core.Grids.GridChunkAllocationSystem>(world, sb);

			// Check VoxelJobFenceRegistry state
			sb.AppendLine("\n=== FENCE REGISTRY STATUS ===");
			try
			{
				// Test if VoxelJobFenceRegistry is initialized
				var testEntity = world.EntityManager.CreateEntity();
				var testFence = VoxelJobFenceRegistry.GetFence(testEntity);
				sb.AppendLine("✓ VoxelJobFenceRegistry appears to be initialized");
				world.EntityManager.DestroyEntity(testEntity);
			}
			catch (System.Exception ex)
			{
				sb.AppendLine($"✗ VoxelJobFenceRegistry error: {ex.Message}");
			}

			// List all systems for reference
			sb.AppendLine("\n=== ALL SYSTEMS IN WORLD ===");
			foreach (var systemHandle in world.Systems)
			{
				var system = world.GetExistingSystem(systemHandle);
				sb.AppendLine($"  {system.GetType().Name}");
			}

			Debug.Log(sb.ToString());
		}

		void CheckForSystem<T>(World world, StringBuilder sb)
			where T : unmanaged, ISystem
		{
			var systemHandle = world.GetExistingSystem<T>();
			if (systemHandle != SystemHandle.Null)
			{
				sb.AppendLine($"✓ {typeof(T).Name} - FOUND");
			}
			else
			{
				sb.AppendLine($"✗ {typeof(T).Name} - MISSING");
			}
		}

		void CheckForManagedSystem<T>(World world, StringBuilder sb)
			where T : ComponentSystemBase
		{
			var system = world.GetExistingSystemManaged<T>();
			if (system != null)
			{
				sb.AppendLine($"✓ {typeof(T).Name} - FOUND");
			}
			else
			{
				sb.AppendLine($"✗ {typeof(T).Name} - MISSING");
			}
		}
	}
}
