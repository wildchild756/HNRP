using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace HN.HNRP
{
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    [ExecuteAlways]
    public class HNAdditionalCameraData : MonoBehaviour, IAdditionalData
    {
        void OnEnable()
        {
            builtinCamera = GetComponent<Camera>();
            viewConstants = new ViewConstants();
            frustum = new Frustum { planes = new Plane[6], corners = new Vector3[8] };
            frustumPlaneEquations = new Vector4[6];
        }

        void Update()
        {
            UpdateViewConstants();
            UpdateFrustum();
        }

        void OnDestroy()
        {
            // renderer 由本附加数据组件持有，其生命周期 = 相机组件生命周期。
            // 组件销毁时释放该相机的 pass 资源（如阴影 atlas、驻留分配表）。
            cameraRenderer?.Dispose();
            cameraRenderer = null;
        }

        /// <summary>
        /// 获取本相机持有的运行时 <see cref="CameraRenderer"/>；不存在时创建。
        /// renderer 跨帧存活，使 pass 实例及其拥有的持久资源（如阴影 atlas）
        /// 随相机保留。<see cref="IsEnabled"/> 等动态开关也因实例持久而跨帧生效。
        /// </summary>
        /// <returns>本相机的运行时渲染器。</returns>
        internal CameraRenderer GetOrCreateRenderer()
        {
            return cameraRenderer ??= new CameraRenderer();
        }

        unsafe public void UpdateCameraGlobalConstantBuffer(ref GlobalConstantBuffer globalConstantBuffer)
        {
            globalConstantBuffer._ScreenSize = new Vector4(BuiltinCamera.scaledPixelWidth, BuiltinCamera.scaledPixelHeight, 1.0f / BuiltinCamera.scaledPixelWidth, 1.0f / BuiltinCamera.scaledPixelHeight);

            globalConstantBuffer._WorldSpaceCameraPos = viewConstants.worldSpaceCameraPos;

            globalConstantBuffer._ProjectionParams = viewConstants.projectionParams;

            globalConstantBuffer._ScreenParams = viewConstants.screenParams;

            globalConstantBuffer._ZBufferParams = viewConstants.zBufferParams;

            globalConstantBuffer.unity_OrthoParams = viewConstants.unity_OrthoParams;

            globalConstantBuffer.unity_MatrixV = viewConstants.viewMatrix;
            globalConstantBuffer.unity_MatrixInvV = viewConstants.invViewMatrix;
            globalConstantBuffer.glstate_matrix_projection = viewConstants.projMatrix;
            globalConstantBuffer.unity_MatrixInvP = viewConstants.invProjMatrix;
            globalConstantBuffer.unity_MatrixVP = viewConstants.viewProjMatrix;
            globalConstantBuffer.unity_MatrixInvVP = viewConstants.invViewProjMatrix;
            for (int i = 0; i < 6; i++)
                for (int j = 0; j < 4; j++)
                    globalConstantBuffer._FrustumPlanes[i * 4 + j] = frustumPlaneEquations[i][j];
        }


        private void UpdateViewConstants()
        {
            // var proj = Matrix4x4.Perspective(BuiltinCamera.fieldOfView, BuiltinCamera.aspect, BuiltinCamera.nearClipPlane, BuiltinCamera.farClipPlane);
            var proj = BuiltinCamera.projectionMatrix;
            var view = BuiltinCamera.worldToCameraMatrix;
            var cameraPosition = transform.position;

            var gpuProj = GL.GetGPUProjectionMatrix(proj, true);
            var gpuView = view;
            var gpuVP = gpuProj * gpuView;

            viewConstants.viewMatrix = gpuView;
            viewConstants.invViewMatrix = gpuView.inverse;
            viewConstants.projMatrix = gpuProj;
            viewConstants.invProjMatrix = gpuProj.inverse;
            viewConstants.viewProjMatrix = gpuVP;
            viewConstants.invViewProjMatrix = gpuVP.inverse;
            viewConstants.worldSpaceCameraPos = cameraPosition;
        }

        private void UpdateFrustum()
        {
            float n = BuiltinCamera.nearClipPlane;
            float f = BuiltinCamera.farClipPlane;

            // p[2][3] = (reverseZ ? 1 : -1) * (depth_0_1 ? 1 : 2) * (f * n) / (f - n)，据此推断 reverseZ 与深度约定
            float scale = viewConstants.projMatrix[2, 3] / (f * n) * (f - n);
            bool reverseZ = scale > 0;
            bool flipProj = viewConstants.invProjMatrix.MultiplyPoint(new Vector3(0, 1, 0)).y < 0;

            if (reverseZ)
            {
                viewConstants.zBufferParams = new Vector4(-1 + f / n, 1, -1 / f + 1 / n, 1 / f);
            }
            else
            {
                viewConstants.zBufferParams = new Vector4(1 - f / n, f / n, 1 / f - 1 / n, 1 / n);
            }

            viewConstants.projectionParams = new Vector4(flipProj ? -1 : 1, n, f, 1.0f / f);

            float cameraWidth = BuiltinCamera.pixelWidth;
            float cameraHeight = BuiltinCamera.pixelHeight;
            viewConstants.screenParams = new Vector4(cameraWidth, cameraHeight, 1.0f + 1.0f / cameraWidth, 1.0f + 1.0f / cameraHeight);

            float orthoHeight = BuiltinCamera.orthographic ? 2 * BuiltinCamera.orthographicSize : 0;
            float orthoWidth = orthoHeight * BuiltinCamera.aspect;
            viewConstants.unity_OrthoParams = new Vector4(orthoWidth, orthoHeight, 0, BuiltinCamera.orthographic ? 1 : 0);

            Vector3 viewDir = -viewConstants.invViewMatrix.GetColumn(2);
            viewDir.Normalize();
            Frustum.Create(ref frustum, viewConstants.viewProjMatrix, viewConstants.invViewMatrix.GetColumn(3), viewDir, n, f);
            for (int i = 0; i < 6; i++)
            {
                frustumPlaneEquations[i] = new Vector4(frustum.planes[i].normal.x, frustum.planes[i].normal.y, frustum.planes[i].normal.z, frustum.planes[i].distance);
            }
        }


        public Camera BuiltinCamera
        {
            get
            {
                if (!builtinCamera)
                {
                    gameObject.TryGetComponent<Camera>(out builtinCamera);
                }
                return builtinCamera;
            }
        }

        /// <summary>
        /// 每相机的渲染图视图索引。
        /// 用于从 <see cref="HNRenderPipelineAsset"/> 中选择使用哪个渲染图视图。
        /// 索引对应渲染图视图键列表中的位置。
        /// </summary>
        public int RenderGraphViewIndex
        {
            get => renderGraphViewIndex;
            set => renderGraphViewIndex = value;
        }

        public bool Dithering
        {
            get { return dithering; }
            set { dithering = value; }
        }

        public bool StopNaNs
        {
            get { return stopNaNs; }
            set { stopNaNs = value; }
        }

        public bool AllowDynamicResolution
        {
            get { return allowDynamicResolution; }
            set { allowDynamicResolution = value; }
        }

        public LayerMask VolumeLayerMask
        {
            get { return volumeLayerMask; }
            set { volumeLayerMask = value; }
        }

        public bool ClearDepth
        {
            get { return clearDepth; }
            set { clearDepth = value; }
        }

        public Rect FinalViewport
        {
            get { return new Rect(builtinCamera.pixelRect.x, builtinCamera.pixelRect.y, builtinCamera.pixelWidth, builtinCamera.pixelHeight); }
        }


        public ViewConstants viewConstants = default;
        public Frustum frustum = default;
        public Vector4[] frustumPlaneEquations;

        [SerializeField]
        private Camera builtinCamera;

        [SerializeField]
        private int renderGraphViewIndex;

        [SerializeField]
        private bool dithering = false;

        [SerializeField]
        private bool stopNaNs = false;

        [SerializeField]
        private bool allowDynamicResolution = false;

        [SerializeField]
        private LayerMask volumeLayerMask = 1;

        [SerializeField]
        private bool clearDepth = true;

        /// <summary>
        /// 本相机的运行时渲染器。运行时状态，不序列化。
        /// 由 <see cref="GetOrCreateRenderer"/> 懒创建，<see cref="OnDestroy"/> 释放。
        /// </summary>
        [System.NonSerialized]
        private CameraRenderer cameraRenderer;




        public struct ViewConstants
        {
            public Matrix4x4 viewMatrix;
            public Matrix4x4 invViewMatrix;
            public Matrix4x4 projMatrix;
            public Matrix4x4 invProjMatrix;
            public Matrix4x4 viewProjMatrix;
            public Matrix4x4 invViewProjMatrix;
            public Vector3 worldSpaceCameraPos;
            public Vector4 zBufferParams;
            public Vector4 projectionParams;
            public Vector4 screenParams;
            public Vector4 unity_OrthoParams;
        }


        public struct Frustum
        {
            public Plane[] planes;
            public Vector3[] corners;

            static Vector3 IntersectFrustumPlanes(Plane p0, Plane p1, Plane p2)
            {
                Vector3 n0 = p0.normal;
                Vector3 n1 = p1.normal;
                Vector3 n2 = p2.normal;

                float det = Vector3.Dot(Vector3.Cross(n0, n1), n2);
                return (Vector3.Cross(n2, n1) * p0.distance + Vector3.Cross(n0, n2) * p1.distance - Vector3.Cross(n0, n1) * p2.distance) * (1.0f / det);
            }

            public static void Create(ref Frustum frustum, Matrix4x4 viewProjMatrix, Vector3 viewPos, Vector3 viewDir, float nearClipPlane, float farClipPlane)
            {
                GeometryUtility.CalculateFrustumPlanes(viewProjMatrix, frustum.planes);

                // 需要重算近/远平面，否则反射所用的斜投影矩阵下近/远平面不正确。
                Plane nearPlane = new Plane();
                nearPlane.SetNormalAndPosition(viewDir, viewPos);
                nearPlane.distance -= nearClipPlane;

                Plane farPlane = new Plane();
                farPlane.SetNormalAndPosition(-viewDir, viewPos);
                farPlane.distance += farClipPlane;

                frustum.planes[4] = nearPlane;
                frustum.planes[5] = farPlane;

                // 依据平面而非投影矩阵计算角点，否则斜投影的近/远平面存在同样问题。
                frustum.corners[0] = IntersectFrustumPlanes(frustum.planes[0], frustum.planes[3], frustum.planes[4]);
                frustum.corners[1] = IntersectFrustumPlanes(frustum.planes[1], frustum.planes[3], frustum.planes[4]);
                frustum.corners[2] = IntersectFrustumPlanes(frustum.planes[0], frustum.planes[2], frustum.planes[4]);
                frustum.corners[3] = IntersectFrustumPlanes(frustum.planes[1], frustum.planes[2], frustum.planes[4]);
                frustum.corners[4] = IntersectFrustumPlanes(frustum.planes[0], frustum.planes[3], frustum.planes[5]);
                frustum.corners[5] = IntersectFrustumPlanes(frustum.planes[1], frustum.planes[3], frustum.planes[5]);
                frustum.corners[6] = IntersectFrustumPlanes(frustum.planes[0], frustum.planes[2], frustum.planes[5]);
                frustum.corners[7] = IntersectFrustumPlanes(frustum.planes[1], frustum.planes[2], frustum.planes[5]);
            }
        }
        

    }


    public static class CameraExtensions
    {
        public static HNAdditionalCameraData GetHNRPAdditionalCameraData(this Camera camera)
        {
            var gameObject = camera.gameObject;
            bool componentExists = gameObject.TryGetComponent<HNAdditionalCameraData>(out var cameraData);
            if (!componentExists)
            {
                cameraData = gameObject.AddComponent<HNAdditionalCameraData>();
            }
            return cameraData;
        }

    }
}
