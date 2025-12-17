// <copyright file="CameraMainSystem.cs" company="BovineLabs">
//     Copyright (c) BovineLabs. All rights reserved.
// </copyright>

#if !BL_DISABLE_CAMERA
namespace BovineLabs.Core.Camera
{
    using Unity.Entities;
    using Unity.Transforms;
    using UnityEngine;

    [UpdateBefore(typeof(CameraFrustumSystem))]
    [UpdateInGroup(typeof(CameraSystemGroup))]
    public partial class CameraMainSystem : SystemBase
    {
        /// <inheritdoc />
        protected override void OnUpdate()
        {
            var cameraQuery = SystemAPI.QueryBuilder().WithAllRW<LocalTransform, CameraComponent>().WithAll<CameraMain>().Build();

            if (cameraQuery.IsEmptyIgnoreFilter)
            {
                // User hasn't setup an entity, create our own
                this.EntityManager.CreateEntity(typeof(CameraMain), typeof(LocalTransform), typeof(CameraFrustumPlanes), typeof(CameraFrustumCorners),
                    typeof(CameraComponent));
            }

            cameraQuery.CompleteDependency();

            ref var cameraComponent = ref cameraQuery.GetSingletonRW<CameraComponent>().ValueRW;

            if (!cameraComponent.Value.IsValid())
            {
                var cam = Camera.main;
                if (cam == null)
                {
                    SystemAPI.GetSingleton<BLLogger>().LogError("No main camera found");
                    return;
                }

                cameraComponent.Value = cam;
            }

            var tr = cameraComponent.Value.Value.transform;
            cameraQuery.GetSingletonRW<LocalTransform>().ValueRW = LocalTransform.FromPositionRotation(tr.position, tr.rotation);
        }
    }
}
#endif
