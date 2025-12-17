// <copyright file="SingletonAttribute.cs" company="BovineLabs">
//     Copyright (c) BovineLabs. All rights reserved.
// </copyright>

namespace BovineLabs.Core.Settings
{
    using System;
    using Unity.Entities;

    /// <summary>
    /// Marks an <see cref="IBufferElementData" /> type as a "singleton buffer" that can be merged from many sources into one
    /// runtime singleton buffer entity by <see cref="SingletonSystem" />.
    /// </summary>
    /// <remarks>
    /// This attribute is intentionally different from <c>BovineLabs.Core.SingletonAttribute</c> (used by Facets on fields).
    /// Use this attribute on buffer element structs, not on facet fields.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Struct)]
    public class SingletonAttribute : Attribute
    {
    }
}
