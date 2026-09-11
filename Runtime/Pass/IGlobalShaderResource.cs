// <copyright file="IGlobalShaderResource.cs" company="HN">
// Copyright (c) HN. All rights reserved.
// </copyright>

using UnityEngine.Rendering;

namespace HN.HNRP
{
    /// <summary>
    /// 由「产出 shader 全局资源」的 Pass 实现的契约。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 前向着色器通过全局属性（<c>SetGlobalBuffer</c> / <c>SetGlobalTexture</c> /
    /// <c>PushGlobal</c>）与全局 keyword 读取各子系统（光照数据、簇剔除、反射探针、
    /// 阴影）产出的资源。本契约把这些资源的<b>绑定知识归还给生产者</b>：
    /// 生产者自行决定绑定哪些属性、使用哪个 keyword、何时算「可用」；
    /// 消费者（如 <see cref="DrawObjectPass"/>）只负责在绘制前对每个已连接输入
    /// slot 的属主调用一次 <see cref="BindGlobalShaderResources"/>，无需硬编码任何
    /// 具体生产者类型与属性 ID。
    /// </para>
    /// <para>
    /// <b>时序：</b>绑定仍在消费者侧（draw 前）执行，不改动「全局状态在帧内统一
    /// 设置」的语义，也保证每帧的 enable/disable 是确定性的。生产者的 render func
    /// 通过读取依赖先于消费者执行，故其已发布输出句柄在绑定时可用。
    /// </para>
    /// <para>
    /// <b>自洽要求：</b>实现者必须在资源不可用时（被禁用 / 未产出 / 提前退出）
    /// 主动<b>关闭</b>其对应 keyword，避免全局 keyword 残留导致着色器走错分支。
    /// </para>
    /// </remarks>
    public interface IGlobalShaderResource
    {
        /// <summary>
        /// 向命令缓冲绑定本 pass 产出的全局 shader 资源
        /// （结构化缓冲 / 纹理 / 常量缓冲 / 全局 keyword）。
        /// </summary>
        /// <remarks>
        /// 资源不可用时实现应关闭其对应 keyword；对无 keyword 的资源则不做任何绑定。
        /// 本方法在绘制命令记录前被调用，实现不得在此记录绘制命令。
        /// </remarks>
        /// <param name="cmd">接收绑定命令的命令缓冲。</param>
        void BindGlobalShaderResources(CommandBuffer cmd);
    }
}
