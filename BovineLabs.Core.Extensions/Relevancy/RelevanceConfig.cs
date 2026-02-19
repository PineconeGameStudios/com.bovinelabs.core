// <copyright file="RelevanceConfig.cs" company="BovineLabs">
//     Copyright (c) BovineLabs. All rights reserved.
// </copyright>

#if !BL_DISABLE_RELEVANCY && UNITY_NETCODE
namespace BovineLabs.Core.Relevancy
{
    using Unity.Entities;

    public struct RelevanceConfig : IComponentData
    {
        public float ClampExtents; // Half size
        public float ExpandExtents;
    }
}
#endif