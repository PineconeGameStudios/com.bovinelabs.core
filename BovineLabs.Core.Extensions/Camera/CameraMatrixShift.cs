// <copyright file="CameraMatrixShift.cs" company="BovineLabs">
//     Copyright (c) BovineLabs. All rights reserved.
// </copyright>

#if !BL_DISABLE_CAMERA
namespace BovineLabs.Core.Camera
{
    using UnityEngine;
    using UnityEngine.Rendering;

    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    [ExecuteAlways]
    public class CameraMatrixShift : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Offset the projection center (principal point) as a fraction of the half-frustum size at the near plane. (1, 0) shifts by one half-width.")]
        private Vector2 projectionCenterOffset;

        [SerializeField]
        [Tooltip("Offset applied to the camera view in camera-space units (meters). Positive X is right, Y is up, Z is forward.")]
        private Vector3 cameraSpaceOffset;

        [SerializeField]
        [Tooltip("If true, rebuilds the camera projection using an off-center frustum from Projection Center Offset.")]
        private bool overrideProjectionMatrix = true;

        [SerializeField]
        [Tooltip("If true, offsets the camera worldToCameraMatrix using Camera Space Offset.")]
        private bool overrideViewMatrix;

        [SerializeField]
        [Tooltip("If true, also overrides the culling matrix to match the custom view/projection matrices.")]
        private bool overrideCullingMatrix = true;

        private Camera cameraComponent;

        private bool isApplied;
        private Matrix4x4 originalWorldToCameraMatrix;
        private Matrix4x4 originalProjectionMatrix;
        private Matrix4x4 originalCullingMatrix;

        private void OnEnable()
        {
            this.cameraComponent = this.GetComponent<Camera>();
            RenderPipelineManager.beginCameraRendering += this.BeginCameraRendering;
            RenderPipelineManager.endCameraRendering += this.EndCameraRendering;
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= this.BeginCameraRendering;
            RenderPipelineManager.endCameraRendering -= this.EndCameraRendering;

            if (this.cameraComponent != null)
            {
                this.Restore(this.cameraComponent);
            }
        }

        private void OnPreCull()
        {
            if (GraphicsSettings.currentRenderPipeline != null)
            {
                return;
            }

            this.Apply(this.cameraComponent);
        }

        private void OnPostRender()
        {
            if (GraphicsSettings.currentRenderPipeline != null)
            {
                return;
            }

            this.Restore(this.cameraComponent);
        }

        private void BeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (camera != this.cameraComponent)
            {
                return;
            }

            this.Apply(camera);
        }

        private void EndCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (camera != this.cameraComponent)
            {
                return;
            }

            this.Restore(camera);
        }

        private void Apply(Camera camera)
        {
            if (camera == null)
            {
                camera = this.cameraComponent != null ? this.cameraComponent : this.GetComponent<Camera>();
            }

            if (camera == null || this.isApplied)
            {
                return;
            }

            this.originalWorldToCameraMatrix = camera.worldToCameraMatrix;
            this.originalProjectionMatrix = camera.projectionMatrix;
            this.originalCullingMatrix = camera.cullingMatrix;

            var worldToCamera = this.originalWorldToCameraMatrix;
            if (this.overrideViewMatrix)
            {
                worldToCamera = Matrix4x4.Translate(-this.cameraSpaceOffset) * worldToCamera;
            }

            var projection = this.originalProjectionMatrix;
            if (this.overrideProjectionMatrix)
            {
                projection = CalculateOffCenterProjection(camera, this.projectionCenterOffset);
            }

            camera.worldToCameraMatrix = worldToCamera;
            camera.projectionMatrix = projection;

            if (this.overrideCullingMatrix)
            {
                camera.cullingMatrix = projection * worldToCamera;
            }

            this.isApplied = true;
        }

        private void Restore(Camera camera)
        {
            if (camera == null || !this.isApplied)
            {
                return;
            }

            camera.worldToCameraMatrix = this.originalWorldToCameraMatrix;
            camera.projectionMatrix = this.originalProjectionMatrix;
            camera.cullingMatrix = this.originalCullingMatrix;

            this.isApplied = false;
        }

        private static Matrix4x4 CalculateOffCenterProjection(Camera camera, Vector2 centerOffset)
        {
            var near = camera.nearClipPlane;
            var far = camera.farClipPlane;
            var aspect = camera.aspect <= 0f ? 1f : camera.aspect;

            if (camera.orthographic)
            {
                var halfHeight = camera.orthographicSize;
                var halfWidth = halfHeight * aspect;
                var x = centerOffset.x * halfWidth;
                var y = centerOffset.y * halfHeight;

                var left = -halfWidth + x;
                var right = halfWidth + x;
                var bottom = -halfHeight + y;
                var top = halfHeight + y;

                return Matrix4x4.Ortho(left, right, bottom, top, near, far);
            }
            else
            {
                if (near <= 0f)
                {
                    return camera.projectionMatrix;
                }

                var halfHeight = near * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
                var halfWidth = halfHeight * aspect;
                var x = centerOffset.x * halfWidth;
                var y = centerOffset.y * halfHeight;

                var left = -halfWidth + x;
                var right = halfWidth + x;
                var bottom = -halfHeight + y;
                var top = halfHeight + y;

                return Matrix4x4.Frustum(left, right, bottom, top, near, far);
            }
        }
    }
}
#endif
