// <copyright file="Singleton.cs" company="BovineLabs">
//     Copyright (c) BovineLabs. All rights reserved.
// </copyright>

namespace BovineLabs.Core.Settings
{
    using Unity.Entities;

    /// <summary>
    /// Tag component placed on the internal singleton-buffer entity created by <see cref="SingletonSystem" />.
    /// </summary>
    public struct Singleton : IComponentData
    {
    }
}
