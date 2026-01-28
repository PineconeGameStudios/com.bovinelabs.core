// <copyright file="DestroyOnDestroySystem.cs" company="BovineLabs">
//     Copyright (c) BovineLabs. All rights reserved.
// </copyright>

#if !BL_DISABLE_LIFECYCLE
namespace BovineLabs.Core.LifeCycle
{
    using Unity.Burst;
    using Unity.Collections;
    using Unity.Collections.LowLevel.Unsafe;
    using Unity.Entities;
    using Unity.Jobs;

    /// <summary>
    /// Propagates destruction through LinkedEntityGroup hierarchies. When an entity with DestroyEntity enabled has a LinkedEntityGroup,
    /// this system recursively marks all child entities for destruction.
    /// </summary>
    [WorldSystemFilter(Worlds.SimulationMenu)]
    [UpdateInGroup(typeof(DestroySystemGroup), OrderFirst = true)]
    public partial struct DestroyOnDestroySystem : ISystem
    {
        /// <inheritdoc/>
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var toDestroy = new NativeQueue<Entity>(state.WorldUpdateAllocator);

            state.Dependency = new DestroyJob
            {
                ToDestroy = toDestroy.AsParallelWriter(),
                LinkedEntityGroups = SystemAPI.GetBufferLookup<LinkedEntityGroup>(),
                DestroyEntitys = SystemAPI.GetComponentLookup<DestroyEntity>(true),
            }.ScheduleParallel(state.Dependency);

            state.Dependency = new DestroyLinkedEntitiesJob
            {
                ToDestroy = toDestroy,
                DestroyEntitys = SystemAPI.GetComponentLookup<DestroyEntity>(),
            }.Schedule(state.Dependency);
        }

        [BurstCompile]
        [WithChangeFilter(typeof(DestroyEntity))]
        [WithAll(typeof(DestroyEntity))]
        private partial struct DestroyJob : IJobEntity
        {
            public NativeQueue<Entity>.ParallelWriter ToDestroy;

            [NativeDisableContainerSafetyRestriction]
            public BufferLookup<LinkedEntityGroup> LinkedEntityGroups;

            [ReadOnly]
            public ComponentLookup<DestroyEntity> DestroyEntitys;

            private void Execute(DynamicBuffer<LinkedEntityGroup> linkedEntityGroup)
            {
                this.DestroyIterative(ref linkedEntityGroup);
            }

            /// <summary>
            /// Recursively propagates destruction through a LinkedEntityGroup hierarchy.
            /// </summary>
            private void DestroyIterative(
                ref DynamicBuffer<LinkedEntityGroup> linkedEntityGroup)
            {
                var leg = linkedEntityGroup.AsNativeArray();

                // i >= 1 so we ignore ourselves
                for (var i = leg.Length - 1; i >= 1; i--)
                {
                    var entity = leg[i].Value;

                    if (entity.Index < 0 || !this.DestroyEntitys.EntityExists(entity))
                    {
                        // Entity has already been destroyed, just safely handle it so we don't have to care about ownership here
                        linkedEntityGroup.RemoveAtSwapBack(i);
                        continue;
                    }

                    // Check child has destroy component, if not we just let regular destroy handle it
                    var enabled = this.DestroyEntitys.GetEnabledRefROOptional<DestroyEntity>(entity);
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

                    this.ToDestroy.Enqueue(entity);

                    // Propagate down
                    if (this.LinkedEntityGroups.TryGetBuffer(entity, out var newLinkedEntityGroup))
                    {
                        this.DestroyIterative(ref newLinkedEntityGroup);
                    }
                }
            }
        }

        [BurstCompile]
        private struct DestroyLinkedEntitiesJob : IJob
        {
            public NativeQueue<Entity> ToDestroy;

            public ComponentLookup<DestroyEntity> DestroyEntitys;

            public void Execute()
            {
                while (this.ToDestroy.TryDequeue(out var entity))
                {
                    this.DestroyEntitys.SetComponentEnabled(entity, true);
                }
            }
        }
    }
}
#endif
