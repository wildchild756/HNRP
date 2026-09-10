using System;
using System.Collections.Generic;
using UnityEngine;

namespace HN.HNRP
{
    /// <summary>
    /// cascade 级数，仅支持 1、2、4、8 四级。
    /// </summary>
    public enum CascadeCountType
    {
        One = 1,
        Two = 2,
        Four = 4,
        Eight = 8
    }

    /// <summary>
    /// 每级 cascade 的分辨率。
    /// </summary>
    public enum ResolutionType
    {
        Low = 512,
        Medium = 1024,
        High = 2048,
        Ultra = 4096
    }

    /// <summary>
    /// 阴影更新模式。
    /// </summary>
    public enum ShadowUpdateModeType
    {
        EveryFrame,
        OnDemand,
        Custom
    }

    /// <summary>
    /// 标记 <see cref="CascadeShadowSettings"/> 字段，交给 CascadeShadowDrawer 自定义绘制。
    /// </summary>
    public class CascadeShadowAttribute : PropertyAttribute
    {
    }

    /// <summary>
    /// 级联阴影设置：级数、每级分辨率、各级远边界（世界空间）、更新模式与更新帧间隔。
    /// </summary>
    [Serializable]
    public struct CascadeShadowSettings
    {
        /// <summary>cascade 最大级数，级联边界数组固定长度。</summary>
        public const int MaxCascadeCount = 8;

        /// <summary>默认的 8 级 cascade 远边界（世界空间，单位米）。</summary>
        public static readonly float[] DefaultSplits =
        {
            1f, 4f, 10f, 30f, 50f, 100f, 300f, 1000f
        };

        /// <summary>默认的 8 级 cascade 更新帧间隔。</summary>
        public static readonly int[] DefaultTimeSlices =
        {
            0, 0, 2, 2, 4, 4, 8, 8
        };

        [SerializeField]
        private CascadeCountType cascadeCount;

        [SerializeField]
        private ResolutionType cascadeResolution;

        [SerializeField]
        private List<float> cascadeSplits;

        [SerializeField]
        private ShadowUpdateModeType shadowUpdateMode;

        [SerializeField]
        private List<int> cascadeTimeSlices;

        /// <summary>
        /// 创建一个使用默认值的新实例。
        /// </summary>
        public static CascadeShadowSettings Default
        {
            get
            {
                CascadeShadowSettings settings = new CascadeShadowSettings
                {
                    cascadeCount = CascadeCountType.Four,
                    cascadeResolution = ResolutionType.Medium,
                    cascadeSplits = new List<float>(DefaultSplits),
                    shadowUpdateMode = ShadowUpdateModeType.EveryFrame,
                    cascadeTimeSlices = new List<int>(DefaultTimeSlices)
                };
                settings.EnsureValid();
                return settings;
            }
        }

        /// <summary>当前 cascade 级数。</summary>
        public CascadeCountType CascadeCount
        {
            get => cascadeCount;
            set
            {
                cascadeCount = value;
                cascadeResolution = CascadeShadowUtils.ClampResolution(cascadeCount, cascadeResolution);
            }
        }

        /// <summary>每级 cascade 分辨率。</summary>
        public ResolutionType CascadeResolution
        {
            get => cascadeResolution;
            set => cascadeResolution = value;
        }

        /// <summary>最大 8 级 cascade 时每级 cascade 的远边界距离（世界空间）。</summary>
        public List<float> CascadeSplits
        {
            get => cascadeSplits;
            set => cascadeSplits = value;
        }

        /// <summary>阴影更新模式。</summary>
        public ShadowUpdateModeType ShadowUpdateMode
        {
            get => shadowUpdateMode;
            set => shadowUpdateMode = value;
        }

        /// <summary>当更新模式为 Custom 时，每级 cascade 的更新帧间隔。</summary>
        public List<int> CascadeTimeSlices
        {
            get => cascadeTimeSlices;
            set => cascadeTimeSlices = value;
        }

        /// <summary>
        /// 修正缺失或非法的数据，保证数组长度与取值合法。
        /// 用于兼容新增字段后的旧序列化数据。
        /// </summary>
        public void EnsureValid()
        {
            if (cascadeCount != CascadeCountType.One
                && cascadeCount != CascadeCountType.Two
                && cascadeCount != CascadeCountType.Four
                && cascadeCount != CascadeCountType.Eight)
            {
                cascadeCount = CascadeCountType.Four;
            }

            if (cascadeSplits == null)
            {
                cascadeSplits = new List<float>(DefaultSplits);
            }
            else
            {
                EnsureFloatList(cascadeSplits, DefaultSplits);
            }

            if (cascadeTimeSlices == null)
            {
                cascadeTimeSlices = new List<int>(DefaultTimeSlices);
            }
            else
            {
                EnsureIntList(cascadeTimeSlices, DefaultTimeSlices);
            }

            cascadeResolution = CascadeShadowUtils.ClampResolution(cascadeCount, cascadeResolution);
        }

        private static void EnsureFloatList(List<float> values, float[] defaults)
        {
            for (int i = values.Count; i < MaxCascadeCount; i++)
            {
                values.Add(defaults[i]);
            }

            if (values.Count > MaxCascadeCount)
            {
                values.RemoveRange(MaxCascadeCount, values.Count - MaxCascadeCount);
            }
        }

        private static void EnsureIntList(List<int> values, int[] defaults)
        {
            for (int i = values.Count; i < MaxCascadeCount; i++)
            {
                values.Add(defaults[i]);
            }

            if (values.Count > MaxCascadeCount)
            {
                values.RemoveRange(MaxCascadeCount, values.Count - MaxCascadeCount);
            }
        }
    }

    /// <summary>
    /// <see cref="CascadeShadowSettings"/> 的纯逻辑校验工具，便于单独测试。
    /// </summary>
    public static class CascadeShadowUtils
    {
        /// <summary>
        /// 判断给定级数是否支持指定分辨率：
        /// 4096 仅支持 1 级，2048 最大支持 4 级，1024 与 512 支持最大 8 级。
        /// </summary>
        public static bool IsResolutionSupported(CascadeCountType count, ResolutionType resolution)
        {
            switch (resolution)
            {
                case ResolutionType.Ultra:
                    return count == CascadeCountType.One;
                case ResolutionType.High:
                    return (int)count <= (int)CascadeCountType.Four;
                default:
                    return true;
            }
        }

        /// <summary>
        /// 把分辨率收敛到当前级数支持的最大分辨率。
        /// </summary>
        public static ResolutionType ClampResolution(CascadeCountType count, ResolutionType resolution)
        {
            if (IsResolutionSupported(count, resolution))
            {
                return resolution;
            }

            if (count == CascadeCountType.One)
            {
                return ResolutionType.Ultra;
            }

            if ((int)count <= (int)CascadeCountType.Four)
            {
                return ResolutionType.High;
            }

            return ResolutionType.Medium;
        }
    }
}
