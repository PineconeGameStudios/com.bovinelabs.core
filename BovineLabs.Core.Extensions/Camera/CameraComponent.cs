// <copyright file="CameraComponent.cs" company="BovineLabs">
//     Copyright (c) BovineLabs. All rights reserved.
// </copyright>

#if !BL_DISABLE_CAMERA
namespace BovineLabs.Core.Camera
{
    using Unity.Entities;
    using UnityEngine;

    public struct CameraComponent : IComponentData
    {
        public UnityObjectRef<Camera> Value;
    }
}
#endif