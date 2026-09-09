// <copyright file="SlotConnection.cs" company="HN">
// Copyright (c) HN. All rights reserved.
// </copyright>

using System;
using UnityEngine;

namespace HN.HNRP
{
    /// <summary>
    /// 描述源 pass slot 与目标 pass slot 之间连接的可序列化数据类。
    /// 用于在管线配置中把不同 pass 之间的数据流接起来。
    /// </summary>
    [Serializable]
    public class SlotConnection
    {
        [SerializeField]
        private string sourcePass;

        [SerializeField]
        private string sourceSlot;

        [SerializeField]
        private string targetPass;

        [SerializeField]
        private string targetSlot;

        /// <summary>
        /// 源 pass 的实例名。不能为 <c>null</c> 或空。
        /// </summary>
        public string SourcePass
        {
            get => sourcePass;
            set => sourcePass = value;
        }

        /// <summary>
        /// 源 pass 上的输出 slot 名。不能为 <c>null</c> 或空。
        /// </summary>
        public string SourceSlot
        {
            get => sourceSlot;
            set => sourceSlot = value;
        }

        /// <summary>
        /// 目标 pass 的实例名。不能为 <c>null</c> 或空。
        /// </summary>
        public string TargetPass
        {
            get => targetPass;
            set => targetPass = value;
        }

        /// <summary>
        /// 目标 pass 上的输入 slot 名。不能为 <c>null</c> 或空。
        /// </summary>
        public string TargetSlot
        {
            get => targetSlot;
            set => targetSlot = value;
        }

        /// <summary>
        /// 校验所有名字字段均非 null 且非空。
        /// </summary>
        /// <returns>全部字段有效时返回 <c>true</c>；否则返回 <c>false</c>。</returns>
        public bool IsValid()
        {
            return !string.IsNullOrEmpty(sourcePass)
                && !string.IsNullOrEmpty(sourceSlot)
                && !string.IsNullOrEmpty(targetPass)
                && !string.IsNullOrEmpty(targetSlot);
        }

        /// <summary>
        /// 用指定的源与目标创建新的 <see cref="SlotConnection"/>。
        /// </summary>
        /// <param name="sourcePass">源 pass 的实例名。</param>
        /// <param name="sourceSlot">源 pass 上的输出 slot 名。</param>
        /// <param name="targetPass">目标 pass 的实例名。</param>
        /// <param name="targetSlot">目标 pass 上的输入 slot 名。</param>
        /// <returns>新的 <see cref="SlotConnection"/> 实例。</returns>
        public static SlotConnection Create(
            string sourcePass,
            string sourceSlot,
            string targetPass,
            string targetSlot)
        {
            return new SlotConnection
            {
                SourcePass = sourcePass,
                SourceSlot = sourceSlot,
                TargetPass = targetPass,
                TargetSlot = targetSlot,
            };
        }
    }
}
