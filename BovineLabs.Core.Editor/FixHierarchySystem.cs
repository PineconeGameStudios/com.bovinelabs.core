// <copyright file="FixHierarchySystem.cs" company="BovineLabs">
//     Copyright (c) BovineLabs. All rights reserved.
// </copyright>

#if UNITY_6000_5_OR_NEWER
namespace BovineLabs.Core.Editor
{
    using Unity.Entities;
    using Unity.Entities.Editor;

    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation | WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.LocalSimulation | Worlds.Menu)]
    public partial class FixHierarchySystem : SystemBase
    {
        /// <inheritdoc/>
        protected override void OnCreate()
        {
            if (this.World.GetExistingSystemManaged<UpdateHierarchySystem>() == null)
            {
                var hierarchySystem = this.World.CreateSystem<UpdateHierarchySystem>();
                var systemGroup = this.World.GetOrCreateSystemManaged<SimulationSystemGroup>();
                systemGroup.AddSystemToUpdateList(hierarchySystem);
                hierarchySystem.Update(this.World.Unmanaged);
            }

            this.Enabled = false;
        }

        protected override void OnUpdate()
        {
        }
    }
}
#endif