// <copyright file="InputBounds.cs" company="BovineLabs">
//     Copyright (c) BovineLabs. All rights reserved.
// </copyright>

#if !BL_DISABLE_RELEVANCY && UNITY_NETCODE
namespace BovineLabs.Core.Relevancy
{
    using Unity.Mathematics;
    using Unity.NetCode;

    public struct InputBounds : IInputComponentData
    {
        [GhostField(Quantization = 100)]
        public float3 Min;

        [GhostField(Quantization = 100)]
        public float3 Max;

        public float3 Center => (this.Max + this.Min) / 2f;

        public MinMaxAABB AABB => new()
        {
            Min = this.Min,
            Max = this.Max,
        };
    }
}
#endif