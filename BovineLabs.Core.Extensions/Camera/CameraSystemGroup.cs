// <copyright file="CameraSystemGroup.cs" company="BovineLabs">
//     Copyright (c) BovineLabs. All rights reserved.
// </copyright>

#if !BL_DISABLE_CAMERA
namespace BovineLabs.Core.Camera
{
    using BovineLabs.Core.Groups;
    using Unity.Entities;

    [WorldSystemFilter(WorldSystemFilterFlags.Presentation | WorldSystemFilterFlags.Editor | Worlds.Menu, WorldSystemFilterFlags.Presentation | Worlds.Menu)]
    [UpdateInGroup(typeof(BeginSimulationSystemGroup))]
    public partial class CameraSystemGroup : ComponentSystemGroup
    {
    }
}
#endif