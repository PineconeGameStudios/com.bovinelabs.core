// <copyright file="SingletonInitializedSystem.cs" company="BovineLabs">
//     Copyright (c) BovineLabs. All rights reserved.
// </copyright>

namespace BovineLabs.Core.Settings
{
    using Unity.Burst;
    using Unity.Burst.Intrinsics;
    using Unity.Entities;

    /// <summary>
    /// Clears the one-frame <see cref="SingletonInitialize" /> signal after all systems in <see cref="SingletonInitializeSystemGroup" />
    /// have updated.
    /// </summary>
    [UpdateInGroup(typeof(SingletonInitializeSystemGroup), OrderLast = true)]
    public partial struct SingletonInitializedSystem : ISystem
    {
        /// <inheritdoc />
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var query = SystemAPI.QueryBuilder().WithAll<SingletonInitialize>().Build();

            state.Dependency = new MarkInitializedJob
            {
                SingletonHandle = SystemAPI.GetComponentTypeHandle<SingletonInitialize>(),
            }.ScheduleParallel(query, state.Dependency);
        }

        [BurstCompile]
        private struct MarkInitializedJob : IJobChunk
        {
            public ComponentTypeHandle<SingletonInitialize> SingletonHandle;

            /// <inheritdoc />
            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                chunk.SetComponentEnabledForAll(ref this.SingletonHandle, false);
            }
        }
    }
}
