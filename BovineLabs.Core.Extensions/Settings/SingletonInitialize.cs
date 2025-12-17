// <copyright file="SingletonInitialize.cs" company="BovineLabs">
//     Copyright (c) BovineLabs. All rights reserved.
// </copyright>

namespace BovineLabs.Core.Settings
{
    using Unity.Entities;

    /// <summary>
    /// Enableable marker component used as a one-frame "singleton buffer changed/initialized" signal for systems in
    /// <see cref="SingletonInitializeSystemGroup" />.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="SingletonSystem" /> enables this component when it finishes merging a singleton buffer.
    /// <see cref="SingletonInitializedSystem" /> disables it at the end of the group.
    /// </para>
    /// <para>
    /// This provides a cheap conditional update mechanism for initialization/caching systems that depend on singleton buffers.
    /// </para>
    /// </remarks>
    public struct SingletonInitialize : IComponentData, IEnableableComponent
    {
    }
}
