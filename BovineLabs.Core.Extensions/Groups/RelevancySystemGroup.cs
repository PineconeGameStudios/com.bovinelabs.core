// <copyright file="RelevancySystemGroup.cs" company="BovineLabs">
//     Copyright (c) BovineLabs. All rights reserved.
// </copyright>

#if UNITY_NETCODE
namespace BovineLabs.Core.Groups
{
    using Unity.Entities;

    /// <summary>
    /// Group for server-side systems that calculate and apply NetCode ghost relevancy.
    /// </summary>
    [UpdateInGroup(typeof(AfterTransformSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation, WorldSystemFilterFlags.ServerSimulation)]
    public partial class RelevancySystemGroup : ComponentSystemGroup
    {
    }
}
#endif
