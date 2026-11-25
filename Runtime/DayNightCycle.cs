using UnityEngine;

namespace PaintedSky
{
    /// <summary>Everything the cycle computed for the current hour. Colors are sRGB unless named Linear.</summary>
    public struct SkyState
    {
        public float hour;
        /// <summary>Main light for shading: the sun by day, the moon by night, never lower than the minimum elevation.</summary>
        public Vector3 lightDirection;
        /// <summary>Real sun direction (goes below the horizon at night).</summary>
        public Vector3 sunDirection;
        public Vector3 moonDirection;
        public float sunVisibility, moonVisibility;
        /// <summary>0 by day, 1 at night.</summary>
        public float nightness;
        public Color zenith, horizon, ambient, glow, fogScatter, cloudLit, cloudShade;
        /// <summary>Direct light (sun or moon) with its fade at the horizon already applied.</summary>
        public Color direct;
        /// <summary>Strength of the direct light relative to noon (1 = noon).</summary>
        public float directAmount;
        public float glowAmount, fogScatterAmount, stars;
        /// <summary>Light that colors the clouds (it keeps lighting high clouds a while after sunset).</summary>
        public Vector3 cloudLightDirection;
        public float cloudLightStrength;
    }

    /// <summary>
    /// Stylized day/night cycle driven by a hand-painted <see cref="SkyPalette"/>.
    ///
    /// The sun rises at 6:00 and sets at 18:00 on a tilted arc; at night the moon (opposite the sun) is the
    /// main light. The switch from sun to moon happens while both are almost dark, so it never pops.
    ///
    /// Every frame it publishes shader globals (see PaintedSkyCommon.hlsl: _SkyLightDir, _SkyHorizon,
    /// _SkyAmbient, _SkyDirect...) and a C# <see cref="State"/>, so any shader or system can follow the time
    /// of day. Set <see cref="manualTick"/> to drive it from your own game loop with <see cref="Tick"/>.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-50)]
    [AddComponentMenu("Painted Sky/Day Night Cycle")]
    public sealed class DayNightCycle : MonoBehaviour
    {
        [Header("Time")]
        [Range(0f, 24f), Tooltip("Hour at start (0-24)")]
        public float startHour = 14.85f;
        [Tooltip("Real minutes for a whole day")]
        public float dayLengthMinutes = 24f;
        [Tooltip("If off, the hour stays where it is")]
        public bool timeFlows = true;
        [Tooltip("Extra speed multiplier (e.g. 30 for a timelapse)")]
        public float timeScale = 1f;
        [Tooltip("Call Tick(dt) yourself instead of updating in Update")]
        public bool manualTick;

        [Header("Look")]
        [Tooltip("Hand-painted palette. Empty = built-in default palette.")]
        public SkyPalette palette;

        [Header("Sun path")]
        [Tooltip("Tilt of the sun's arc (90 - max elevation at noon), degrees")]
        [Range(0f, 80f)] public float arcTilt = 35f;
        [Tooltip("Compass rotation of the sun's arc, degrees (ignored when Reference Sun Direction is set)")]
        public float azimuth;
        [Tooltip("Optional: rotate the arc so the afternoon sun passes exactly through this direction (towards the sun)")]
        public Vector3 referenceSunDirection;
        [Tooltip("Lowest elevation (sine) of the shading light, so shadows never get infinitely long")]
        [Range(0.05f, 0.6f)] public float minLightElevation = 0.22f;

        [Header("Camera")]
        [Tooltip("Paint this camera's clear color with the horizon color (useful with Solid Color clear)")]
        public Camera targetCamera;

        static readonly Color SUN_NOON = new Color(0.50f, 0.38f, 0.16f);

        Vector3 east = Vector3.right, south = Vector3.forward;
        bool basisReady;
        bool hourSet;
        float hour;

        /// <summary>Current hour (0-24). Setting it updates the sky on the next tick.</summary>
        public float Hour { get => hour; set { hour = Mathf.Repeat(value, 24f); hourSet = true; } }

        /// <summary>Everything computed for the current hour.</summary>
        public SkyState State { get; private set; }

        /// <summary>Hour at which the sun passes through <see cref="referenceSunDirection"/> (or -1).</summary>
        public float ReferenceHour { get; private set; } = -1f;

        /// <summary>Called after every tick, once the globals are published.</summary>
        public event System.Action<DayNightCycle> Ticked;

        /// <summary>The active cycle (the last one enabled).</summary>
        public static DayNightCycle Active { get; private set; }

        SkyPalette Palette => palette ? palette : SkyPalette.Default;

        void Awake()
        {
            BuildBasis();
            // a game that set the hour before this Awake (another script's Awake) keeps it
            if (!hourSet) Hour = startHour;
            Tick(0f);
        }

        void OnEnable() => Active = this;
        void OnDisable() { if (Active == this) Active = null; }

        void OnValidate() => basisReady = false;

        void Update()
        {
            if (!manualTick) Tick(Time.deltaTime);
        }

        /// <summary>Recomputes the sun's arc after changing arcTilt, azimuth or referenceSunDirection from code.</summary>
        public void RebuildSunPath() => BuildBasis();

        void BuildBasis()
        {
            float lat = arcTilt * Mathf.Deg2Rad;
            float rot;
            Vector3 s = referenceSunDirection;
            if (s.sqrMagnitude > 1e-6f)
            {
                s.Normalize();
                // rotate the arc so the afternoon sun passes through the reference direction
                float a0 = Mathf.PI - Mathf.Asin(Mathf.Clamp(s.y / Mathf.Cos(lat), -1f, 1f));
                float localAng = Mathf.Atan2(Mathf.Sin(a0) * Mathf.Sin(lat), Mathf.Cos(a0));
                rot = Mathf.Atan2(s.z, s.x) - localAng;
                ReferenceHour = 6f + 12f * a0 / Mathf.PI;
            }
            else
            {
                rot = azimuth * Mathf.Deg2Rad;
                ReferenceHour = -1f;
            }
            east = new Vector3(Mathf.Cos(rot), 0, Mathf.Sin(rot));
            south = new Vector3(-Mathf.Sin(rot), 0, Mathf.Cos(rot));
            basisReady = true;
        }

        /// <summary>Direction towards the sun at hour h (below the horizon at night).</summary>
        public Vector3 SunDirection(float h)
        {
            if (!basisReady) BuildBasis();
            float lat = arcTilt * Mathf.Deg2Rad;
            float a = (h - 6f) / 12f * Mathf.PI;
            Vector3 up = Vector3.up * Mathf.Cos(lat) + south * Mathf.Sin(lat);
            return (east * Mathf.Cos(a) + up * Mathf.Sin(a)).normalized;
        }

        /// <summary>Shading light (sun or moon) at hour h, with the minimum elevation applied.</summary>
        public Vector3 LightDirection(float h)
        {
            Vector3 sun = SunDirection(h);
            return ClampElevation(sun.y > -0.05f ? sun : -sun, minLightElevation);
        }

        static Vector3 ClampElevation(Vector3 d, float minY)
        {
            if (d.y >= minY) return d;
            var hz = new Vector2(d.x, d.z);
            hz = hz.sqrMagnitude > 1e-8f ? hz.normalized : Vector2.right;
            float k = Mathf.Sqrt(1f - minY * minY);
            return new Vector3(hz.x * k, minY, hz.y * k);
        }

        static float Smooth(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        static float Lum(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
        static Vector4 V(Color c) => new Vector4(c.r, c.g, c.b, c.a);   // sRGB as-is (no linear conversion)
        static Vector4 V(Color c, float w) => new Vector4(c.r, c.g, c.b, w);

        /// <summary>Advances the time by dt seconds and publishes the sky for the new hour.</summary>
        public void Tick(float dt)
        {
            if (!basisReady) BuildBasis();
            if (timeFlows && dayLengthMinutes > 0.01f)
                hour = Mathf.Repeat(hour + dt * timeScale * 24f / (dayLengthMinutes * 60f), 24f);

            var pal = Palette;
            SkyKey k = pal.Sample(hour);
            Vector3 sun = SunDirection(hour);
            Vector3 moon = -sun;
            float sy = sun.y;

            // direct light weights: between -0.06 and -0.04 both are 0 (that's where the sun hands over to the moon)
            float sunW = Smooth(-0.04f, 0.08f, sy);
            float moonW = 1f - Smooth(-0.20f, -0.06f, sy);
            Color direct = k.sun * sunW + pal.moonLight * moonW;
            direct.a = 1f;

            // clouds are high: they keep the sun a while after sunset (pink dusk)
            float cSunW = Smooth(-0.22f, -0.10f, sy);
            float cMoonW = 1f - Smooth(-0.34f, -0.22f, sy);

            var st = new SkyState
            {
                hour = hour,
                lightDirection = ClampElevation(sy > -0.05f ? sun : moon, minLightElevation),
                sunDirection = sun,
                moonDirection = moon,
                sunVisibility = Smooth(-0.06f, 0f, sy),
                moonVisibility = Smooth(0f, 0.08f, moon.y) * (1f - sunW),
                nightness = 1f - Smooth(-0.12f, 0.06f, sy),
                zenith = k.zenith,
                horizon = k.horizon,
                ambient = k.ambient,
                glow = k.glow,
                glowAmount = k.glowAmount,
                direct = direct,
                directAmount = Mathf.Clamp(Lum(direct) / Lum(SUN_NOON), 0f, 1.3f),
                fogScatter = Color.Lerp(k.glow, pal.moonScatter, moonW),
                fogScatterAmount = k.fogScatter * Mathf.Max(sunW, moonW * 0.3f),
                cloudLit = k.cloudLit,
                cloudShade = k.cloudShade,
                stars = k.stars,
                cloudLightDirection = sy > -0.22f ? sun : moon,
                cloudLightStrength = Mathf.Max(cSunW, cMoonW),
            };
            State = st;
            Publish(st);
            if (targetCamera) targetCamera.backgroundColor = new Color(k.horizon.r, k.horizon.g, k.horizon.b);
            Ticked?.Invoke(this);
        }

        static void Publish(in SkyState s)
        {
            Shader.SetGlobalVector(SkyIds.LightDir, s.lightDirection);
            Shader.SetGlobalVector(SkyIds.SunDir, new Vector4(s.sunDirection.x, s.sunDirection.y, s.sunDirection.z, s.sunVisibility));
            Shader.SetGlobalVector(SkyIds.MoonDir, new Vector4(s.moonDirection.x, s.moonDirection.y, s.moonDirection.z, s.moonVisibility));
            Shader.SetGlobalVector(SkyIds.Zenith, V(s.zenith));
            Shader.SetGlobalVector(SkyIds.Horizon, V(s.horizon));
            Shader.SetGlobalVector(SkyIds.Ambient, V(s.ambient));
            Shader.SetGlobalVector(SkyIds.Direct, V(s.direct));
            Shader.SetGlobalFloat(SkyIds.DirectAmount, s.directAmount);
            Shader.SetGlobalVector(SkyIds.Glow, V(s.glow, s.glowAmount));
            Shader.SetGlobalVector(SkyIds.FogScatter, V(s.fogScatter, s.fogScatterAmount));
            Shader.SetGlobalVector(SkyIds.Night, new Vector4(s.stars, s.nightness, 0, 0));
            Shader.SetGlobalVector(SkyIds.CloudLit, V(s.cloudLit));
            Shader.SetGlobalVector(SkyIds.CloudShade, V(s.cloudShade));
            var cl = s.cloudLightDirection;
            Shader.SetGlobalVector(SkyIds.CloudLightDir, new Vector4(cl.x, cl.y, cl.z, s.cloudLightStrength));
            Shader.SetGlobalFloat(SkyIds.Time, Time.timeSinceLevelLoad);
        }

        /// <summary>"HH:MM" for a HUD clock.</summary>
        public string ClockText
        {
            get
            {
                int m = Mathf.FloorToInt(hour * 60f) % (24 * 60);
                return $"{m / 60:00}:{m % 60:00}";
            }
        }
    }

    /// <summary>Shader global names published by Painted Sky (declared in PaintedSkyCommon.hlsl).</summary>
    public static class SkyIds
    {
        public static readonly int LightDir = Shader.PropertyToID("_SkyLightDir");
        public static readonly int SunDir = Shader.PropertyToID("_SkySunDir");
        public static readonly int MoonDir = Shader.PropertyToID("_SkyMoonDir");
        public static readonly int Zenith = Shader.PropertyToID("_SkyZenith");
        public static readonly int Horizon = Shader.PropertyToID("_SkyHorizon");
        public static readonly int Ambient = Shader.PropertyToID("_SkyAmbient");
        public static readonly int Direct = Shader.PropertyToID("_SkyDirect");
        public static readonly int DirectAmount = Shader.PropertyToID("_SkyDirectAmount");
        public static readonly int Glow = Shader.PropertyToID("_SkyGlow");
        public static readonly int FogScatter = Shader.PropertyToID("_SkyFogScatter");
        public static readonly int Night = Shader.PropertyToID("_SkyNight");
        public static readonly int CloudLit = Shader.PropertyToID("_SkyCloudLit");
        public static readonly int CloudShade = Shader.PropertyToID("_SkyCloudShade");
        public static readonly int CloudLightDir = Shader.PropertyToID("_SkyCloudLightDir");
        public static readonly int Time = Shader.PropertyToID("_SkyTime");
        public static readonly int CloudShadowParams = Shader.PropertyToID("_SkyCloudShadowParams");
        public static readonly int GhibliClouds = Shader.PropertyToID("_SkyGhibliClouds");
        public static readonly int GhibliRamp = Shader.PropertyToID("_SkyGhibliRamp");
        public static readonly int GhibliShape = Shader.PropertyToID("_SkyGhibliShape");
    }
}
