// <copyright file="RelevanceAlways.cs" company="BovineLabs">
//     Copyright (c) BovineLabs. All rights reserved.
// </copyright>

#if !BL_DISABLE_RELEVANCY && UNITY_NETCODE
namespace BovineLabs.Core.Relevancy
{
    using Unity.Entities;

    public struct RelevanceAlways : IComponentData
    {
    }
}
#endif