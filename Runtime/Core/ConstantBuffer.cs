using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace HN.HNRP
{
    /// <summary>
    /// 从相机当前状态填充 <see cref="GlobalConstantBuffer"/>。
    /// 用于把每相机的全局 shader 常量（矩阵、相机位置、屏幕参数）推入
    /// <c>ShaderVariablesGlobal</c> cbuffer，使离屏相机（如实时反射探针面）
    /// 使用自身矩阵渲染，而非最后一次 <c>SetupCameraProperties</c> 的相机。
    /// </summary>
    public static class GlobalConstantBufferUtility
    {
        /// <summary>
        /// 从 <paramref name="camera"/> 填充 <paramref name="cb"/>。
        /// </summary>
        /// <param name="camera">要采集矩阵/参数的相机。</param>
        /// <param name="renderIntoTexture">
        /// 相机渲染到渲染纹理时为 <c>true</c>（投影 Y 翻转）；
        /// 渲染到屏幕/后备缓冲时为 <c>false</c>。
        /// </param>
        /// <param name="cb">要填充的缓冲（覆盖矩阵/相机字段）。</param>
        public static void FillFromCamera(Camera camera, bool renderIntoTexture, ref GlobalConstantBuffer cb)
        {
            float time = Time.time;
            cb._Time = new Vector4(time / 20f, time, time * 2f, time * 3f);
            cb._SinTime = new Vector4(Mathf.Sin(time / 8f), Mathf.Sin(time / 4f), Mathf.Sin(time / 2f), Mathf.Sin(time));
            cb._CosTime = new Vector4(Mathf.Cos(time / 8f), Mathf.Cos(time / 4f), Mathf.Cos(time / 2f), Mathf.Cos(time));
            cb.unity_DeltaTime = new Vector4(
                Time.deltaTime,
                Time.deltaTime > 0f ? 1f / Time.deltaTime : 0f,
                Time.smoothDeltaTime,
                Time.smoothDeltaTime > 0f ? 1f / Time.smoothDeltaTime : 0f);

            int width = camera.pixelWidth;
            int height = camera.pixelHeight;
            cb._ScreenSize = new Vector4(width, height, 1f / width, 1f / height);
            cb._ScreenParams = new Vector4(width, height, 1f + 1f / width, 1f + 1f / height);
            cb._WorldSpaceCameraPos = camera.transform.position;

            var proj = camera.projectionMatrix;
            var view = camera.worldToCameraMatrix;
            var gpuProj = GL.GetGPUProjectionMatrix(proj, renderIntoTexture);
            var gpuVP = gpuProj * view;

            cb.unity_MatrixV = view;
            cb.unity_MatrixInvV = view.inverse;
            cb.glstate_matrix_projection = gpuProj;
            cb.unity_MatrixInvP = gpuProj.inverse;
            cb.unity_MatrixVP = gpuVP;
            cb.unity_MatrixInvVP = gpuVP.inverse;

            float near = camera.nearClipPlane;
            float far = camera.farClipPlane;
            float scale = gpuProj[2, 3] / (far * near) * (far - near);
            bool reverseZ = scale > 0;
            bool flipProj = gpuProj.inverse.MultiplyPoint(new Vector3(0, 1, 0)).y < 0;
            cb._ZBufferParams = reverseZ
                ? new Vector4(-1f + far / near, 1f, -1f / far + 1f / near, 1f / far)
                : new Vector4(1f - far / near, far / near, 1f / far - 1f / near, 1f / near);
            cb._ProjectionParams = new Vector4(flipProj ? -1f : 1f, near, far, 1f / far);
            cb.unity_OrthoParams = camera.orthographic
                ? new Vector4(2f * camera.orthographicSize * camera.aspect, 2f * camera.orthographicSize, 0f, 1f)
                : new Vector4(0f, 0f, 0f, 0f);
        }
    }

    /// <summary>
    /// 与 HLSL <c>ShaderVariablesGlobal</c> cbuffer 布局一致的全局 shader 常量结构体。
    /// 字段名须与 shader 侧声明的常量保持一致（属代码关键词，不做汉化）。
    /// </summary>
    unsafe public struct GlobalConstantBuffer
    {
        public Vector4 _Time;
        public Vector4 _SinTime;
        public Vector4 _CosTime;
        public Vector4 unity_DeltaTime;
        public Vector4 _TimeParameters;

        public Vector4 _ScreenSize;
        public Vector4 _WorldSpaceCameraPos;
        public Vector4 _ProjectionParams;
        public Vector4 _ScreenParams;
        public Vector4 _ZBufferParams;
        public Vector4 unity_OrthoParams;
        
        public Matrix4x4 unity_MatrixV;
        public Matrix4x4 unity_MatrixInvV;
        public Matrix4x4 glstate_matrix_projection;
        public Matrix4x4 unity_MatrixInvP;
        public Matrix4x4 unity_MatrixVP;
        public Matrix4x4 unity_MatrixInvVP; 

        /// <summary>视锥体平面参数数组（每平面 4 个 float）。</summary>
        public fixed float _FrustumPlanes[6 * 4];

        /// <summary>光源常量数据（由光源收集填充）。</summary>
        public Vector4 _LightConstantData;

        /// <summary>环境光与反射相关颜色参数。</summary>
        public Vector4 _GlossyEnvironmentColor;
        public Vector4 _GlossyEnvironmentCubeMap_HDR;
        public Vector4 _SubtractiveShadowColor;
        public Vector4 unity_AmbientSky;
        public Vector4 unity_AmbientEquator;
        public Vector4 unity_AmbientGround;

        // 预留但当前未启用的常量：
        // public Vector4 glstate_lightmodel_ambient;
        // public Vector4 unity_IndirectSpecColor;
        // public Vector4 unity_FogParams;
        // public Vector4 unity_FogColor;
        // public Vector4 unity_ShadowColor;
    }
}
