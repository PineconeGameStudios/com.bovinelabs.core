// <copyright file="SingletonInitializeSystemGroup.cs" company="BovineLabs">
//     Copyright (c) BovineLabs. All rights reserved.
// </copyright>

namespace BovineLabs.Core.Settings
{
    using BovineLabs.Core.Groups;
    using BovineLabs.Core.Pause;
    using BovineLabs.Core.Utility;
    using Unity.Entities;

#if !BL_DISABLE_LIFECYCLE
    [WorldSystemFilter(Worlds.SimulationEditor)]
    [UpdateBefore(typeof(LifeCycle.InitializeSystemGroup))]
#endif
    [UpdateInGroup(typeof(BeginSimulationSystemGroup), OrderFirst = true)]
    public partial class SingletonInitializeSystemGroup : ComponentSystemGroup, IUpdateWhilePaused
    {
        /// <summary>
        /// Updates only when there is at least one enabled <see cref="SingletonInitialize" /> component, allowing dependent systems
        /// to run only when singleton buffers have been merged/changed.
        /// </summary>
        /// <inheritdoc />
        protected override void OnUpdate()
        {
            var query = SystemAPI.QueryBuilder().WithAll<SingletonInitialize>().Build();
            if (BurstUtil.IsEmpty(ref query))
            {
                return;
            }

            base.OnUpdate();
        }
    }
}
