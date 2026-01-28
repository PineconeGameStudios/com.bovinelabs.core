// <copyright file="DestroyOnDestroySystem.cs" company="BovineLabs">
//     Copyright (c) BovineLabs. All rights reserved.
// </copyright>

#if !BL_DISABLE_LIFECYCLE
namespace BovineLabs.Core.LifeCycle
{
    using Unity.Burst;
    using Unity.Collections;
    using Unity.Entities;
    using Unity.Jobs;

    /// <summary> 
    /// Propagates destruction through LinkedEntityGroup hierarchies. When an entity with DestroyEntity enabled has a LinkedEntityGroup, 
    /// this system recursively marks all child entities for destruction.
    /// </summary>
    [UpdateInGroup(typeof(DestroySystemGroup), OrderFirst = true)]
    public partial struct DestroyOnDestroySystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var disableChildEntities = new NativeQueue<Entity>(state.WorldUpdateAllocator);

            state.Dependency = new DestroyJob
            {
                ToDisable = disableChildEntities.AsParallelWriter(),
                LinkedEntityGroups = SystemAPI.GetBufferLookup<LinkedEntityGroup>(),
                DestroyEntitys = SystemAPI.GetComponentLookup<DestroyEntity>(true),
                EntityStorageInfoLookup = SystemAPI.GetEntityStorageInfoLookup(),
            }.ScheduleParallel(state.Dependency);

            state.Dependency = new DisableChildEntitiesJob
            {
                ToDisable = disableChildEntities,
                DestroyEntitys = SystemAPI.GetComponentLookup<DestroyEntity>(),
            }.Schedule(state.Dependency);
        }

        [BurstCompile]
        [WithChangeFilter(typeof(DestroyEntity))]
        [WithAll(typeof(DestroyEntity))]
        private partial struct DestroyJob : IJobEntity
        {
            public NativeQueue<Entity>.ParallelWriter ToDisable;

            [NativeDisableParallelForRestriction]
            public BufferLookup<LinkedEntityGroup> LinkedEntityGroups;

            [ReadOnly]
            public ComponentLookup<DestroyEntity> DestroyEntitys;

            [ReadOnly]
            public EntityStorageInfoLookup EntityStorageInfoLookup;

            private void Execute(DynamicBuffer<LinkedEntityGroup> linkedEntityGroup)
            {
                DestroyIterative(ref linkedEntityGroup, ref this.DestroyEntitys, ref this.LinkedEntityGroups, ref this.EntityStorageInfoLookup,
                    ref this.ToDisable);
            }

            /// <summary>
            /// Recursively propagates destruction through a LinkedEntityGroup hierarchy.
            /// </summary>
            private static void DestroyIterative(
                ref DynamicBuffer<LinkedEntityGroup> linkedEntityGroup, ref ComponentLookup<DestroyEntity> destroyEntities,
                ref BufferLookup<LinkedEntityGroup> linkedEntityGroups, ref EntityStorageInfoLookup entityStorageInfoLookup,
                ref NativeQueue<Entity>.ParallelWriter toDisable)
            {
                var leg = linkedEntityGroup.AsNativeArray();

                // i >= 1 so we ignore ourselves
                for (var i = leg.Length - 1; i >= 1; i--)
                {
                    var entity = leg[i].Value;

                    if (entity.Index < 0 || !entityStorageInfoLookup.Exists(entity))
                    {
                        // Entity has already been destroyed, just safely handle it so we don't have to care about ownership here
                        linkedEntityGroup.RemoveAtSwapBack(i);
                        continue;
                    }

                    // Check child has destroy component, if not we just let regular destroy handle it
                    var enabled = destroyEntities.GetEnabledRefROOptional<DestroyEntity>(entity);
                    if (!enabled.IsValid)
                    {
                        continue;
                    }

                    // Need to be removed from LEG so it can be handled by destroy system instead
                    linkedEntityGroup.RemoveAtSwapBack(i);

                    // Destroy already being handled, so we don't touch it as it will be iterated over at the top level
                    if (enabled.ValueRO)
                    {
                        continue;
                    }

                    // enabled.ValueRW = true;
                    toDisable.Enqueue(entity);

                    // Propagate down
                    if (linkedEntityGroups.TryGetBuffer(entity, out var newLinkedEntityGroup))
                    {
                        DestroyIterative(ref newLinkedEntityGroup, ref destroyEntities, ref linkedEntityGroups, ref entityStorageInfoLookup, ref toDisable);
                    }
                }
            }
        }

        [BurstCompile]
        private struct DisableChildEntitiesJob : IJob
        {
            public NativeQueue<Entity> ToDisable;

            public ComponentLookup<DestroyEntity> DestroyEntitys;

            public void Execute()
            {
                while (this.ToDisable.TryDequeue(out var entity))
                {
                    this.DestroyEntitys.SetComponentEnabled(entity, true);
                }
            }
        }
    }
}
#endif
