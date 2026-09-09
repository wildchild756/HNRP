using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace HN.HNRP
{
    /// <summary>
    /// 每相机渲染上下文。
    /// 包装 <see cref="Camera"/> 与 <see cref="ScriptableRenderContext"/>，携带裁剪结果、
    /// 命令缓冲、光照数据等全部每帧渲染状态。实现 <see cref="System.IDisposable"/>——
    /// 帧结束时调用 <see cref="Dispose"/> 以释放池化资源与原生数组。
    /// </summary>
    public class CameraContext
    {
        /// <summary>
        /// 本帧正在渲染的相机。
        /// </summary>
        public Camera Camera { get; set; }

        /// <summary>
        /// 用于调度与执行渲染命令的 ScriptableRenderContext。
        /// </summary>
        public ScriptableRenderContext Context { get; set; }

        /// <summary>
        /// 当前帧 <c>context.Cull()</c> 产生的裁剪结果，
        /// 包含可见光、反射探针与待绘制渲染器。
        /// 仅当 <see cref="HasCullingResults"/> 为 <c>true</c> 时有效。
        /// </summary>
        public CullingResults CullingResults { get; set; }

        /// <summary>
        /// 获取或设置本帧相机裁剪是否成功。为 <c>false</c> 时
        /// <see cref="CullingResults"/> 为 <c>default(CullingResults)</c>，
        /// 不得用来构建渲染器列表（无效描述符会在渲染图编译期抛错）。
        /// </summary>
        public bool HasCullingResults { get; set; }

        /// <summary>
        /// 记录渲染命令的命令缓冲。构造时经 <see cref="CommandBufferPool.Get"/>
        /// 从池中分配，<see cref="Dispose"/> 中经 <see cref="CommandBufferPool.Release"/>
        /// 归还。
        /// </summary>
        public CommandBuffer Cmd { get; private set; }

        /// <summary>
        /// 获取或设置输出是否垂直翻转。
        /// </summary>
        public bool Flip { get; set; }

        /// <summary>
        /// 自定义渲染目标的面（cubemap 渲染用）。未知时为 <see cref="CubemapFace.Unknown"/>。
        /// </summary>
        public CubemapFace TargetFace { get; set; } = CubemapFace.Unknown;

        /// <summary>
        /// 自定义渲染目标的目标深度切片。
        /// </summary>
        public int TargetDepthSlice { get; set; } = -1;

        /// <summary>
        /// 包装自定义目标纹理的 RTHandle。
        /// </summary>
        public RTHandle CustomTargetRTHandle { get; set; }

        /// <summary>
        /// 从 <see cref="CullingResults"/> 得到的可见光。该原生数组
        /// 必须经 <see cref="Dispose"/> 释放。
        /// </summary>
        public NativeArray<VisibleLight> VisibleLights { get; set; }

        /// <summary>
        /// 从 <see cref="CullingResults"/> 得到的可见反射探针。该原生数组
        /// 必须经 <see cref="Dispose"/> 释放。
        /// </summary>
        public NativeArray<VisibleReflectionProbe> VisibleReflectionProbes { get; set; }

        /// <summary>
        /// 管线共享的运行时资源（shader、纹理、compute buffer）。
        /// </summary>
        public HNRenderPipelineRuntimeResources RuntimeResources { get; set; }

        /// <summary>
        /// 每帧填充时间、相机与光照参数的全局 shader 常量缓冲。
        /// </summary>
        public GlobalConstantBuffer ConstantBuffer { get; set; }

        /// <summary>
        /// 初始化 <see cref="CameraContext"/> 的新实例。
        /// 从池中分配名为 <c>"CameraContext"</c> 的命令缓冲。
        /// </summary>
        /// <param name="camera">本帧要渲染的相机。</param>
        /// <param name="context">当前帧的 ScriptableRenderContext。</param>
        public CameraContext(Camera camera, ScriptableRenderContext context)
        {
            Camera = camera;
            Context = context;
            Cmd = CommandBufferPool.Get("CameraContext");
        }

        /// <summary>
        /// 释放池化命令缓冲与原生数组。可多次调用——后续调用为空操作。
        /// </summary>
        public void Dispose()
        {
            if (Cmd != null)
            {
                CommandBufferPool.Release(Cmd);
                Cmd = null;
            }

            if (VisibleLights.IsCreated)
            {
                VisibleLights.Dispose();
            }

            if (VisibleReflectionProbes.IsCreated)
            {
                VisibleReflectionProbes.Dispose();
            }

            // 有意不在此释放 CustomTargetRTHandle：渲染图命令在 Dispose 之后
            // （HNRenderPipeline.Render 内）才提交 GPU，句柄须跨帧存活；
            // 由拥有者 RealtimeProbeRenderer 在 context.Submit() 后释放。
        }
    }
}
