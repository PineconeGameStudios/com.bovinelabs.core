// <copyright file="RelevancySystem.cs" company="BovineLabs">
//     Copyright (c) BovineLabs. All rights reserved.
// </copyright>

#if !BL_DISABLE_RELEVANCY && UNITY_NETCODE
namespace BovineLabs.Core.Relevancy
{
    using System;
    using BovineLabs.Core.Extensions;
    using BovineLabs.Core.Groups;
    using BovineLabs.Core.Spatial;
    using BovineLabs.Core.Utility;
    using Unity.Burst;
    using Unity.Collections;
    using Unity.Collections.LowLevel.Unsafe;
    using Unity.Entities;
    using Unity.Jobs;
    using Unity.Mathematics;
    using Unity.NetCode;
    using Unity.Transforms;

    [UpdateInGroup(typeof(RelevancySystemGroup), OrderFirst = true)] // This system is responsible for clearing so must run first
    public unsafe partial struct RelevancySystem : ISystem
    {
        private EntityQuery spatialGhostQuery;
        private EntityQuery alwaysRelevantQuery;
        private EntityQuery relevanceProviderQuery;

        private PositionBuilder positionBuilder;
        private SpatialMap<SpatialPosition> spatialMapUtil;

        private NativeList<Ptr<UnsafeList<RelevantGhostForConnection>>> relevantGhostForConnections;

        public void OnCreate(ref SystemState state)
        {
            ReadOnlySpan<ComponentType> comps = stackalloc ComponentType[] { ComponentType.ReadWrite<GhostDistanceData>(), ComponentType.ReadWrite<GhostImportance>() };

            var entity = state.EntityManager.CreateEntity(comps);

            state.EntityManager.SetComponentData(entity, new GhostImportance
            {
                BatchScaleImportanceFunction = GhostDistanceImportance.BatchScaleWithRelevancyFunctionPointer,
                GhostConnectionComponentType = ComponentType.ReadOnly<GhostConnectionPosition>(),
                GhostImportanceDataType = ComponentType.ReadOnly<GhostDistanceData>(),
                GhostImportancePerChunkDataType = ComponentType.ReadOnly<GhostDistancePartitionShared>(),
            });

            state.EntityManager.SetComponentData(entity, new GhostDistanceData
            {
                TileSize = new int3(64, 10240, 64), // 256
                TileCenter = new int3(0, 0, 0),
                TileBorderWidth = new float3(1f),
            });

            // TODO why is this safe?
            ref var ghostRelevancy = ref SystemAPI.GetSingletonRW<GhostRelevancy>().ValueRW;
            ghostRelevancy.GhostRelevancyMode = GhostRelevancyMode.SetIsIrrelevant;
            ghostRelevancy.DefaultRelevancyQuery = new EntityQueryBuilder(Allocator.Temp).WithAll<RelevanceAlways>().Build(state.EntityManager);
        }

        //         /// <inheritdoc />
//         [BurstCompile]
//         public void OnCreate(ref SystemState state)
//         {
//             SystemAPI.GetSingletonRW<GhostRelevancy>().ValueRW.GhostRelevancyMode = GhostRelevancyMode.SetIsRelevant;
//
//             this.spatialGhostQuery = SystemAPI.QueryBuilder().WithAll<GhostInstance, LocalTransform, GhostCleanup>().WithNone<RelevanceManual>().Build();
//             this.alwaysRelevantQuery = SystemAPI
//                 .QueryBuilder()
//                 .WithAll<GhostInstance, GhostCleanup>()
//                 .WithNone<LocalTransform, RelevanceManual>()
//                 .AddAdditionalQuery()
//                 .WithAll<GhostInstance, GhostCleanup, RelevanceAlways>()
//                 .WithNone<RelevanceManual>()
//                 .Build();
//
//             this.relevanceProviderQuery = SystemAPI.QueryBuilder().WithAll<GhostOwner, InputBounds, RelevanceProvider>().Build();
//             state.RequireForUpdate(this.relevanceProviderQuery);
//
//             this.positionBuilder = new PositionBuilder(ref state, this.spatialGhostQuery);
//             this.spatialMapUtil = new SpatialMap<SpatialPosition>(2, 16 * 1024);
//
//             this.relevantGhostForConnections = new NativeList<Ptr<UnsafeList<RelevantGhostForConnection>>>(8, Allocator.Persistent);
//
//             state.AddDependency(ComponentType.ReadWrite<GhostRelevancy>());
//         }
//
//         /// <inheritdoc />
//         [BurstCompile]
//         public void OnDestroy(ref SystemState state)
//         {
//             this.spatialMapUtil.Dispose();
//
//             foreach (var r in this.relevantGhostForConnections)
//             {
//                 UnsafeList<RelevantGhostForConnection>.Destroy(r.Value);
//             }
//
//             this.relevantGhostForConnections.Dispose();
//         }
//
//         /// <inheritdoc />
//         [BurstCompile]
//         public void OnUpdate(ref SystemState state)
//         {
//             var connectionCount = this.relevanceProviderQuery.CalculateEntityCount();
//             this.EnsureListCapacity(connectionCount);
//
//             // TODO clean this up...
//             var relevanceEntities = this.relevanceProviderQuery.ToEntityListAsync(state.WorldUpdateAllocator, state.Dependency, out var dependency);
//             state.Dependency = dependency;
//
//             state.Dependency = this.positionBuilder.Gather(ref state, state.Dependency, out var positions);
//             var spatialDependency = this.spatialMapUtil.Build(positions, state.Dependency);
//
//             var ghostComponents = this.spatialGhostQuery.ToComponentDataListAsync<GhostInstance>(
//                 state.WorldUpdateAllocator, state.Dependency, out var ghostDependency1);
//
//             var alwaysRelevantGhostComponents = this.alwaysRelevantQuery.ToComponentDataListAsync<GhostInstance>(
//                 state.WorldUpdateAllocator, state.Dependency, out var ghostDependency2);
//
//             state.Dependency = JobHandle.CombineDependencies(spatialDependency, ghostDependency1, ghostDependency2);
//
//             state.Dependency = new RelevancyJob
//             {
//                 RelevanceEntities = relevanceEntities,
//                 GhostOwners = SystemAPI.GetComponentLookup<GhostOwner>(true),
//                 InputBounds = SystemAPI.GetComponentLookup<InputBounds>(true),
//                 GhostInstances = ghostComponents.AsDeferredJobArray(),
//                 AlwaysRelevantGhostInstances = alwaysRelevantGhostComponents.AsDeferredJobArray(),
//                 RelevantGhostForConnections = this.relevantGhostForConnections,
//                 SpatialMap = this.spatialMapUtil.AsReadOnly(),
//                 Config = SystemAPI.GetSingleton<RelevanceConfig>(),
//             }.ScheduleParallel(this.relevanceProviderQuery.CalculateEntityCount(), 1, state.Dependency);
//
//             state.Dependency = new MergeToHashMapJob
//             {
//                 RelevantSet = SystemAPI.GetSingleton<GhostRelevancy>().GhostRelevancySet,
//                 RelevantGhostForConnections = this.relevantGhostForConnections,
//                 ConnectionCount = connectionCount,
//             }.Schedule(state.Dependency);
//         }
//
//         /// <summary> Ensure enough lists 1 per connection. This both allocates on joining and disposes on connections disconnecting. </summary>
//         /// <param name="connectionCount"> The number of connections. </param>
//         private void EnsureListCapacity(int connectionCount)
//         {
//             var startCount = this.relevantGhostForConnections.Length;
//
//             // Clear any old lists from connection count dropping
//             for (var i = connectionCount; i < startCount; i++)
//             {
//                 UnsafeList<RelevantGhostForConnection>.Destroy(this.relevantGhostForConnections[i].Value);
//             }
//
//             // Add any new lists from connections joined
//             this.relevantGhostForConnections.ResizeUninitialized(connectionCount);
//             for (var i = startCount; i < connectionCount; i++)
//             {
//                 this.relevantGhostForConnections[i] = UnsafeList<RelevantGhostForConnection>.Create(0, Allocator.Persistent);
//             }
//         }
//
//         [BurstCompile]
//         private struct RelevancyJob : IJobFor
//         {
//             [ReadOnly]
//             public NativeList<Entity> RelevanceEntities;
//
//             [ReadOnly]
//             public ComponentLookup<GhostOwner> GhostOwners;
//
//             [ReadOnly]
//             public ComponentLookup<InputBounds> InputBounds;
//
//             [ReadOnly]
//             public NativeArray<GhostInstance> GhostInstances;
//
//             [ReadOnly]
//             public NativeArray<GhostInstance> AlwaysRelevantGhostInstances;
//
//             [ReadOnly]
//             public NativeList<Ptr<UnsafeList<RelevantGhostForConnection>>> RelevantGhostForConnections;
//
//             public SpatialMap.ReadOnly SpatialMap;
//
//             public RelevanceConfig Config;
//
//             public void Execute(int index)
//             {
//                 var list = this.RelevantGhostForConnections[index].Value;
//                 list->Clear();
//
//                 var entity = this.RelevanceEntities[index];
//                 ref readonly var networkID = ref this.GhostOwners.GetRefRO(entity).ValueRO.NetworkId;
//
//                 // TODO we can use some replicates to speed this up
//                 list->SetCapacity(list->Length + this.AlwaysRelevantGhostInstances.Length);
//
//                 for (var i = 0; i < this.AlwaysRelevantGhostInstances.Length; i++)
//                 {
//                     list->AddNoResize(new RelevantGhostForConnection(networkID, this.AlwaysRelevantGhostInstances[i].ghostId));
//                 }
//
//                 ref readonly var bounds = ref this.InputBounds.GetRefRO(entity).ValueRO;
//
//                 // No data yet
//                 if (!math.all(bounds.Max.xz - bounds.Min.xz))
//                 {
//                     return;
//                 }
//
//                 AABB aabb = bounds.AABB;
//                 aabb.Extents = math.min(aabb.Extents, this.Config.ClampExtents);
//                 aabb.Extents += this.Config.ExpandExtents;
//
//                 // Add all the relevant ghosts in the boxes around us
//                 var min = this.SpatialMap.Quantized(aabb.Min.xz);
//                 var max = this.SpatialMap.Quantized(aabb.Max.xz);
//
//                 var map = this.SpatialMap.Map;
//                 for (var j = min.y; j <= max.y; j++)
//                 {
//                     for (var i = min.x; i <= max.x; i++)
//                     {
//                         var spatialIndex = this.SpatialMap.Hash(new int2(i, j));
//
//                         if (!map.TryGetFirstValue(spatialIndex, out var item, out var it))
//                         {
//                             continue;
//                         }
//
//                         do
//                         {
//                             list->Add(new RelevantGhostForConnection(networkID, this.GhostInstances[item].ghostId));
//                         }
//                         while (map.TryGetNextValue(out item, ref it));
//                     }
//                 }
//
//                 // Finally add all our always relevant ghosts
// //                 var startIndex = list->Length;
// //                 list->Resize(list->Length + this.AlwaysRelevantGhostInstances.Length);
// //
// //                 var array = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<RelevantGhostForConnection>(
// //                     list->Ptr + startIndex, this.AlwaysRelevantGhostInstances.Length, Allocator.None);
// //
// // #if ENABLE_UNITY_COLLECTIONS_CHECKS
// //                 NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref array, AtomicSafetyHandle.GetTempMemoryHandle());
// // #endif
// //
// //                 // array.Slice().SliceWithStride<int>().CopyFrom();
// //
// //                 array/e
//
//                 // var ghostIds = this.AlwaysRelevantGhostInstances.Slice().SliceWithStride<int>();
//                 //
//                 // UnsafeUtility.MemCpyStride(array.GetUnsafePtr<T>(), num, this.GetUnsafeReadOnlyPtr<T>(), this.Stride, num, this.m_Length);
//                 //
//                 // ghostIds.CopyTo();
//             }
//         }
//
//         [BurstCompile]
//         private struct MergeToHashMapJob : IJob
//         {
//             public NativeParallelHashMap<RelevantGhostForConnection, int> RelevantSet;
//
//             [ReadOnly]
//             public NativeList<Ptr<UnsafeList<RelevantGhostForConnection>>> RelevantGhostForConnections;
//
//             public int ConnectionCount;
//
//             public void Execute()
//             {
//                 this.RelevantSet.Clear();
//                 for (var index = 0; index < this.ConnectionCount; index++)
//                 {
//                     var relevant = this.RelevantGhostForConnections[index];
//                     this.RelevantSet.AddBatchUnsafe(relevant.Value->Ptr, relevant.Value->Length);
//                 }
//             }
//         }
    }
}
#endif