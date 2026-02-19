// <copyright file="InputBoundsAuthoring.cs" company="BovineLabs">
//     Copyright (c) BovineLabs. All rights reserved.
// </copyright>

#if !BL_DISABLE_RELEVANCY && UNITY_NETCODE
namespace BovineLabs.Core.Authoring.Relevancy
{
    using BovineLabs.Core.Relevancy;
    using Unity.Entities;
    using UnityEngine;

    public class InputBoundsAuthoring : MonoBehaviour
    {
        private class InputCameraFrustumBaker : Baker<InputBoundsAuthoring>
        {
            public override void Bake(InputBoundsAuthoring authoring)
            {
                var entity = this.GetEntity(TransformUsageFlags.None);
                this.AddComponent<InputBounds>(entity);
            }
        }
    }
}
#endif