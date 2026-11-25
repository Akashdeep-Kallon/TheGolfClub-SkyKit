using System;
using System.Collections.Generic;
using UnityEngine;

namespace PaintedSky
{
    /// <summary>
    /// One hand-picked look for a time of day. All colors are sRGB (screen space), like a painter's palette;
    /// shaders convert them to linear when needed.
    /// </summary>
    [Serializable]
    public struct SkyKey
    {
        [Range(0f, 24f)] public float hour;
        [Tooltip("Sky color straight up")] public Color zenith;
        [Tooltip("Sky color at the horizon; also the distance fog color")] public Color horizon;
        [Tooltip("Ambient (sky) light that reaches shadowed surfaces")] public Color ambient;
        [Tooltip("Direct sunlight (before the sun/moon fade at the horizon)")] public Color sun;
        [Tooltip("Sunrise/sunset glow along the horizon, and fog color towards the light")] public Color glow;
        [Range(0f, 1f), Tooltip("Strength of the horizon glow")] public float glowAmount;
        [Range(0f, 1f), Tooltip("How much the fog turns to the glow color when looking at the light")] public float fogScatter;
        [Tooltip("Lit side of the clouds")] public Color cloudLit;
        [Tooltip("Shadow side of the clouds (usually takes the sky's blue)")] public Color cloudShade;
        [Range(0f, 1f), Tooltip("Star visibility")] public float stars;

        public static SkyKey Lerp(SkyKey a, SkyKey b, float t) => new SkyKey
        {
            hour = Mathf.Lerp(a.hour, b.hour, t),
            zenith = Color.Lerp(a.zenith, b.zenith, t),
            horizon = Color.Lerp(a.horizon, b.horizon, t),
            ambient = Color.Lerp(a.ambient, b.ambient, t),
            sun = Color.Lerp(a.sun, b.sun, t),
            glow = Color.Lerp(a.glow, b.glow, t),
            glowAmount = Mathf.Lerp(a.glowAmount, b.glowAmount, t),
            fogScatter = Mathf.Lerp(a.fogScatter, b.fogScatter, t),
            cloudLit = Color.Lerp(a.cloudLit, b.cloudLit, t),
            cloudShade = Color.Lerp(a.cloudShade, b.cloudShade, t),
            stars = Mathf.Lerp(a.stars, b.stars, t),
        };
    }

    /// <summary>
    /// Hand-painted palette for the day/night cycle: one <see cref="SkyKey"/> per moment of the day,
    /// blended smoothly. Create one with Assets ▸ Create ▸ Painted Sky ▸ Sky Palette (it starts with the
    /// default Ghibli-like palette).
    /// </summary>
    [CreateAssetMenu(menuName = "Painted Sky/Sky Palette", fileName = "SkyPalette")]
    public sealed class SkyPalette : ScriptableObject
    {
        [Tooltip("Keys sorted by hour. The first and last keys should match so midnight wraps smoothly.")]
        public List<SkyKey> keys = new List<SkyKey>();

        [Header("Moon")]
        [Tooltip("Direct moonlight color (sRGB)")] public Color moonLight = C(0.10f, 0.13f, 0.22f);
        [Tooltip("Fog color when looking towards the moon (sRGB)")] public Color moonScatter = C(0.62f, 0.70f, 0.92f);

        static Color C(float r, float g, float b) => new Color(r, g, b, 1f);

        void Reset() => FillDefault(this);
        void OnEnable() { if (keys == null || keys.Count == 0) FillDefault(this); }

        /// <summary>Blended key at hour h (0-24).</summary>
        public SkyKey Sample(float h)
        {
            if (keys == null || keys.Count == 0) FillDefault(this);
            if (keys.Count == 1) return keys[0];
            h = Mathf.Repeat(h, 24f);
            int i = 0;
            while (i < keys.Count - 2 && h >= keys[i + 1].hour) i++;
            SkyKey a = keys[i], b = keys[i + 1];
            float t = Mathf.Clamp01((h - a.hour) / Mathf.Max(b.hour - a.hour, 1e-4f));
            t = t * t * (3f - 2f * t);
            return SkyKey.Lerp(a, b, t);
        }

        static SkyKey K(float hour, Color zenith, Color horizon, Color ambient, Color sun, Color glow, float glowAmount,
                        float fogScatter, Color cloudLit, Color cloudShade, float stars)
            => new SkyKey
            {
                hour = hour, zenith = zenith, horizon = horizon, ambient = ambient, sun = sun, glow = glow,
                glowAmount = glowAmount, fogScatter = fogScatter, cloudLit = cloudLit, cloudShade = cloudShade, stars = stars,
            };

        static SkyKey Night(float h) => K(h, C(0.035f, 0.06f, 0.16f), C(0.10f, 0.15f, 0.30f), C(0.20f, 0.27f, 0.48f), C(0, 0, 0),
                                          C(0.55f, 0.62f, 0.88f), 0f, 0.25f, C(0.42f, 0.49f, 0.68f), C(0.10f, 0.13f, 0.26f), 1f);
        static SkyKey Noon(float h) => K(h, C(0.28f, 0.50f, 0.86f), C(0.72f, 0.84f, 0.94f), C(0.56f, 0.64f, 0.82f), C(0.50f, 0.38f, 0.16f),
                                         C(1.00f, 0.92f, 0.78f), 0f, 0.6f, C(1.00f, 0.99f, 0.96f), C(0.60f, 0.68f, 0.85f), 0f);

        /// <summary>The default palette: night, blue hour, peach sunrise, fresh morning, noon, golden hour,
        /// orange sunset with purple shadows, pink dusk.</summary>
        public static void FillDefault(SkyPalette p)
        {
            p.keys = new List<SkyKey>
            {
                Night(0f),
                Night(4.6f),
                // blue hour
                K(5.4f, C(0.10f, 0.16f, 0.36f), C(0.42f, 0.40f, 0.58f), C(0.30f, 0.33f, 0.52f), C(0.30f, 0.18f, 0.20f),
                  C(0.95f, 0.55f, 0.58f), 0.55f, 0.5f, C(0.86f, 0.62f, 0.70f), C(0.28f, 0.27f, 0.45f), 0.45f),
                // sunrise: peach and pink
                K(6.1f, C(0.24f, 0.36f, 0.64f), C(0.98f, 0.74f, 0.60f), C(0.44f, 0.45f, 0.62f), C(0.70f, 0.40f, 0.22f),
                  C(1.00f, 0.64f, 0.42f), 0.9f, 0.8f, C(1.00f, 0.76f, 0.60f), C(0.50f, 0.46f, 0.64f), 0f),
                // fresh morning
                K(7.4f, C(0.28f, 0.49f, 0.84f), C(0.80f, 0.85f, 0.92f), C(0.52f, 0.59f, 0.78f), C(0.54f, 0.40f, 0.20f),
                  C(1.00f, 0.88f, 0.72f), 0.15f, 0.65f, C(1.00f, 0.96f, 0.90f), C(0.60f, 0.66f, 0.82f), 0f),
                Noon(9.5f),
                Noon(15.6f),
                // golden hour
                K(16.9f, C(0.26f, 0.45f, 0.80f), C(0.92f, 0.82f, 0.68f), C(0.52f, 0.54f, 0.72f), C(0.62f, 0.42f, 0.14f),
                  C(1.00f, 0.80f, 0.55f), 0.35f, 0.8f, C(1.00f, 0.88f, 0.68f), C(0.58f, 0.58f, 0.76f), 0f),
                // sunset: deep orange, purple shadows
                K(17.9f, C(0.20f, 0.30f, 0.58f), C(1.00f, 0.62f, 0.40f), C(0.46f, 0.40f, 0.56f), C(0.72f, 0.34f, 0.12f),
                  C(1.00f, 0.50f, 0.28f), 1f, 0.9f, C(1.00f, 0.64f, 0.44f), C(0.50f, 0.38f, 0.54f), 0f),
                // dusk: high clouds stay pink
                K(18.7f, C(0.10f, 0.13f, 0.32f), C(0.56f, 0.38f, 0.50f), C(0.30f, 0.29f, 0.48f), C(0.30f, 0.12f, 0.08f),
                  C(0.90f, 0.42f, 0.42f), 0.6f, 0.4f, C(0.90f, 0.52f, 0.58f), C(0.24f, 0.22f, 0.38f), 0.3f),
                Night(19.7f),
                Night(24f),
            };
            p.moonLight = C(0.10f, 0.13f, 0.22f);
            p.moonScatter = C(0.62f, 0.70f, 0.92f);
        }

        static SkyPalette s_Default;

        /// <summary>Shared in-memory default palette (used when a cycle has no palette asset).</summary>
        public static SkyPalette Default
        {
            get
            {
                if (!s_Default)
                {
                    s_Default = CreateInstance<SkyPalette>();
                    s_Default.name = "Painted Sky Default Palette";
                    s_Default.hideFlags = HideFlags.DontSave;
                    FillDefault(s_Default);
                }
                return s_Default;
            }
        }
    }
}
