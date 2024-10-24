using System;
using UnityEngine;

[Serializable]
public class PostProcessingSettings
{
    [SerializeField]
    Shader shader = default;

    [NonSerialized]
    Material material;

    [SerializeField]
    BloomSettings bloom = new BloomSettings
    {
        scatter = 0.7f
    };

    [SerializeField]
    ToneMappingSettings toneMapping = default;

    [SerializeField]
    ColorAdjustmentSettings colorAdjustments = new ColorAdjustmentSettings
    {
        colorFilter = Color.white
    };

    [SerializeField]
    WhiteBalanceSettings whiteBalance = default;

    [SerializeField]
    SplitToningSettings splitToning = new SplitToningSettings
    {
        shadows = Color.gray,
        highlights = Color.gray
    };

    [SerializeField]
    ChannelMixerSettings channelMixer = new ChannelMixerSettings
    {
        red = Vector3.right,
        green = Vector3.up,
        blue = Vector3.forward
    };

    [SerializeField]
    ShadowsMidtonesHighlightsSettings shadowsMidtonesHighlights = new ShadowsMidtonesHighlightsSettings
    {
        shadows = Color.white,
        midtones = Color.white,
        highlights = Color.white,
        shadowsEnd = 0.3f,
        highlightsStart = 0.55f,
        highlightsEnd = 1f
    };


    public Material Material
    {
        get
        {
            if (material == null && shader != null)
            {
                material = new Material(shader);
                material.hideFlags = HideFlags.HideAndDontSave;
            }
            return material;
        }
    }
    public ToneMappingSettings ToneMapping => toneMapping;

    public ColorAdjustmentSettings ColorAdjustments => colorAdjustments;

    public WhiteBalanceSettings WhiteBalance => whiteBalance;

    public SplitToningSettings SplitToning => splitToning;

    public ChannelMixerSettings ChannelMixer => channelMixer;

    public ShadowsMidtonesHighlightsSettings ShadowsMidtonesHighlights => shadowsMidtonesHighlights;

    public BloomSettings Bloom => bloom;


    [Serializable]
    public class BloomSettings
    {
        public bool ignoreRenderScale = false;

        [Range(0f, 16f)]
        public int maxIterations = 16;

        [Min(1f)]
        public int downScaleLimit = 2;

        public bool bicubicUpsampling = true;

        [Min(0f)]
        public float threshold = 1.0f;

        [Range(0f, 1f)]
        public float thresholdKnee = 0.5f;

        [Min(0f)]
        public float intensity = 1.0f;

        public bool fadeFireflies = true;

        public enum Mode
        {
            Additive,
            Scattering
        }
        public Mode mode = Mode.Scattering;

        [Range(0.05f, 0.95f)]
        public float scatter = 0.5f;
    }

    [Serializable]
    public class ToneMappingSettings
    {
        public enum Mode
        {
            None,
            ACES,
            Neutral,
            Reinhard,
        }

        public Mode mode = Mode.ACES;
    }

    [Serializable]
    public class ColorAdjustmentSettings
    {
        public float postExposure = 0.0f;

        [Range(-100f, 100f)]
        public float contrast = 0.0f;

        [ColorUsage(false, true)]
        public Color colorFilter = Color.white;

        [Range(-180f, 180f)]
        public float hueShift = 0.0f;

        [Range(-100f, 100f)]
        public float saturation = 0.0f;
    }

    [Serializable]
    public class WhiteBalanceSettings
    {
        [Range(-100f, 100f)]
        public float temperature = 0.0f;
        [Range(-100f, 100f)]
        public float tint = 0.0f;
    }

    [Serializable]
    public class SplitToningSettings
    {
        [ColorUsage(false)]
        public Color shadows = Color.gray;
        public Color highlights = Color.gray;

        [Range(-100f, 100f)]
        public float balance = 0.0f;
    }

    [Serializable]
    public class ChannelMixerSettings
    {
        public Vector3 red = new Vector3(1.0f, 0.0f, 0.0f);
        public Vector3 green = new Vector3(0.0f, 1.0f, 0.0f);
        public Vector3 blue = new Vector3(0.0f, 0.0f, 1.0f);
    }

    [Serializable]
    public class ShadowsMidtonesHighlightsSettings
    {
        [ColorUsage(false, true)]
        public Color shadows = Color.white;

        [ColorUsage(false, true)]
        public Color midtones = Color.white;

        [ColorUsage(false, true)]
        public Color highlights = Color.white;

        [Range(0f, 2f)]
        public float shadowsStart = 0.0f;

        [Range(0f, 2f)]
        public float shadowsEnd = 0.3f;

        [Range(0f, 2f)]
        public float highlightsStart = 0.55f;

        [Range(0f, 2f)]
        public float highlightsEnd = 1.0f;
    }
}
