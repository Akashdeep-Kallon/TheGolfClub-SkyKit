using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PaintedSky
{
    /// <summary>
    /// Huge volumetric cumulus with a painted (Ghibli-like) look, driven by the <see cref="DayNightCycle"/>.
    ///
    /// Rendering is done by the bundled UnityVolumetricCloudsURP renderer feature (jiaozi158, MIT); add it to
    /// your URP renderer with Tools ▸ Painted Sky ▸ Install Clouds Renderer Feature. This component:
    /// - creates a global Volume with the clouds override and keeps it in sync with the fields below;
    /// - creates a directional light only for the clouds (sun by day, moon by night) and the ambient probe,
    ///   so it works even when the game itself uses no Unity lights (custom unlit shaders);
    /// - feeds the painted mode: the volume gives shape and opacity, the colors come from the sky palette,
    ///   with a per-lobe normal (ConstructNormal + HalfLambert), a soft light ramp (ToonRemapGradient) and
    ///   milky shadows (ElevateBlack), techniques from yuanqiubai/Custom-Shader (MIT);
    /// - publishes the cloud shadow on the ground (_SkyCloudShadowParams + the light cookie): sample it with
    ///   PaintedSkyCloudShadow() from PaintedSkyCommon.hlsl in your own shaders.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Painted Sky/Painted Clouds")]
    public sealed class PaintedClouds : MonoBehaviour
    {
        [Tooltip("Day/night cycle that colors the clouds. Empty = the active one.")]
        public DayNightCycle cycle;

        [Header("Amount and wind")]
        [Range(0f, 1f), Tooltip("How many cumulus there are")] public float coverage = 0.8f;
        [Range(0.2f, 2f), Tooltip("How solid they are")] public float density = 1f;
        [Tooltip("Wind speed (m/s)")] public float windSpeed = 7f;
        [Tooltip("Wind direction on the ground plane (x, z)")] public Vector2 windDirection = new Vector2(0.8f, 0.6f);
        [Tooltip("The clouds speed up with the cycle's time scale, capped at this value (timelapse)")]
        public float maxTimelapse = 8f;

        [Header("Shape")]
        [Tooltip("Cloud base altitude (m)")] public float bottomAltitude = 1300f;
        [Tooltip("Height of the cloud layer (m): tall towers need a tall layer")] public float altitudeRange = 5500f;
        [Tooltip("Size of the big shapes (lower = bigger)")] public float shapeScale = 2.4f;
        [Range(0f, 1f), Tooltip("Small noise eating the edges")] public float erosionFactor = 0.45f;
        [Tooltip("Size of the edge noise")] public float erosionScale = 55f;
        [Range(0f, 1f), Tooltip("How much the cloud layer curves down to the horizon")] public float earthCurvature = 0.25f;

        [Header("Painted look")]
        [Range(0f, 1f), Tooltip("0 = physical lighting of the volume, 1 = painted with the sky palette")]
        public float painted = 1f;
        [Tooltip("x,y = light ramp (ToonRemapGradient), z = elevate black (milky shadows), w = silver lining")]
        public Vector4 ramp = new Vector4(0.08f, 0.45f, 0.9f, 0.6f);
        [Tooltip("x = lobe normal sample offset (m), y = per-lobe shading, z = lobe rim light, w = sky light on lobe tops")]
        public Vector4 lobes = new Vector4(220f, 0.85f, 0.22f, 0.18f);

        [Header("Ground shadows")]
        public bool castShadows = true;
        [Range(0f, 1f), Tooltip("Darkness of the cloud shadows on the ground")] public float shadowStrength = 0.6f;
        [Tooltip("Size of the area covered by the shadow map around the camera (m): smaller = sharper")]
        public float shadowDistance = 2500f;

        [Header("Quality")]
        [Range(0, 2), Tooltip("0 = low, 1 = medium, 2 = high (ray-march and light steps)")] public int quality = 2;
        [Range(0f, 1f), Tooltip("Temporal accumulation: higher = less noise, more ghosting")] public float temporalAccumulation = 0.95f;

        static readonly int[] STEPS = { 32, 48, 64 };
        static readonly int[] LIGHT_STEPS = { 3, 4, 6 };

        Volume volumeHost;
        VolumeProfile profile;
        VolumetricClouds volume;
        Light cloudLight;

        /// <summary>The clouds override (for advanced tweaks).</summary>
        public VolumetricClouds Volume => volume;

        void OnEnable()
        {
            var go = new GameObject("Painted Clouds (runtime)") { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(transform, false);

            // light only for the clouds: it carries their sun/moon and the shadow cookie
            var lightGo = new GameObject("Painted Clouds Light") { hideFlags = HideFlags.DontSave };
            lightGo.transform.SetParent(go.transform, false);
            cloudLight = lightGo.AddComponent<Light>();
            cloudLight.type = LightType.Directional;
            cloudLight.shadows = LightShadows.None;
            cloudLight.GetUniversalAdditionalLightData();   // the clouds put their shadow in this light's cookie
            RenderSettings.sun = cloudLight;
            RenderSettings.ambientMode = AmbientMode.Flat;

            volumeHost = go.AddComponent<Volume>();
            volumeHost.isGlobal = true;
            volumeHost.priority = 50;
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "Painted Clouds";
            profile.hideFlags = HideFlags.DontSave;
            volumeHost.sharedProfile = profile;

            volume = profile.Add<VolumetricClouds>(true);
            volume.cloudPreset = VolumetricClouds.CloudPresets.Custom;
            volume.state.Override(true);
            volume.localClouds.Override(false);
            // tall cumulus towers: flat base, dense body, round top
            volume.densityCurve.Override(new AnimationCurve(
                new Keyframe(0f, 0f), new Keyframe(0.03f, 1f), new Keyframe(0.7f, 1f), new Keyframe(0.92f, 0.65f), new Keyframe(1f, 0f)));
            volume.erosionCurve.Override(new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.1f, 0.9f), new Keyframe(1f, 1f)));
            volume.ambientOcclusionCurve.Override(new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.2f, 0.45f), new Keyframe(1f, 0f)));
            volume.microErosion.Override(false);
            volume.altitudeDistortion.Override(0.2f);
            volume.powderEffectIntensity.Override(0.35f);
            volume.multiScattering.Override(0.55f);
            volume.ambientLightProbeDimmer.Override(1f);
            volume.sunLightDimmer.Override(1f);
            volume.shadowOpacity.Override(0.85f);
            volume.fadeInMode.Override(VolumetricClouds.CloudFadeInMode.Automatic);
            volume.verticalShapeWindSpeed.Override(0.6f);
            volume.verticalErosionWindSpeed.Override(1.2f);
            ApplySettings(1f);
            LateUpdate();
        }

        void OnDisable()
        {
            Shader.SetGlobalVector(SkyIds.CloudShadowParams, Vector4.zero);
            Shader.SetGlobalFloat(SkyIds.GhibliClouds, 0f);
            if (volumeHost) Destroy(volumeHost.gameObject);
            if (profile) Destroy(profile);
            volumeHost = null; profile = null; volume = null; cloudLight = null;
        }

        void ApplySettings(float timeScale)
        {
            if (volume == null) return;
            volume.shapeFactor.Override(Mathf.Lerp(0.98f, 0.85f, Mathf.Clamp01(coverage)));
            volume.densityMultiplier.Override(Mathf.Clamp01(0.8f * density));
            volume.shapeScale.Override(Mathf.Max(0.1f, shapeScale));
            volume.erosionFactor.Override(erosionFactor);
            volume.erosionScale.Override(Mathf.Max(1f, erosionScale));
            volume.bottomAltitude.Override(Mathf.Max(1f, bottomAltitude));
            volume.altitudeRange.Override(Mathf.Max(100f, altitudeRange));
            volume.earthCurvature.Override(earthCurvature);
            // wind (km/h) towards windDirection; it speeds up with the cycle's time scale (timelapse)
            volume.globalSpeed.Override(windSpeed * 3.6f * Mathf.Clamp(timeScale, 1f, Mathf.Max(1f, maxTimelapse)));
            Vector2 wd = windDirection.sqrMagnitude > 1e-6f ? windDirection.normalized : Vector2.right;
            volume.globalOrientation.Override(Mathf.Repeat(Mathf.Atan2(wd.y, wd.x) * Mathf.Rad2Deg, 360f));
            volume.shadows.Override(castShadows);
            volume.shadowDistance.Override(Mathf.Max(1000f, shadowDistance));
            int q = Mathf.Clamp(quality, 0, 2);
            volume.numPrimarySteps.Override(STEPS[q]);
            volume.numLightSteps.Override(LIGHT_STEPS[q]);
            volume.temporalAccumulationFactor.Override(temporalAccumulation);

            Shader.SetGlobalFloat(SkyIds.GhibliClouds, Mathf.Clamp01(painted));
            Shader.SetGlobalVector(SkyIds.GhibliRamp, ramp);
            Shader.SetGlobalVector(SkyIds.GhibliShape, lobes);
        }

        void LateUpdate()
        {
            var c = cycle ? cycle : DayNightCycle.Active;
            ApplySettings(c ? (c.timeFlows ? c.timeScale : 1f) : 1f);
            if (!c || !cloudLight) return;

            SkyState s = c.State;
            // the light points from the sun (or the moon) down to the ground
            cloudLight.transform.rotation = Quaternion.LookRotation(-s.cloudLightDirection, Vector3.up);
            cloudLight.color = s.cloudLit.linear;
            // never fully 0: the clouds drop their shadow map when the light has no intensity
            cloudLight.intensity = Mathf.Max(0.02f, s.cloudLightStrength);

            // ambient for the physical mode and the cloud ambient occlusion: palette sky and horizon
            var sh = new SphericalHarmonicsL2();
            Color top = s.zenith.linear, hor = s.horizon.linear;
            sh.AddAmbientLight(Color.Lerp(hor, top, 0.5f) * 0.9f);
            sh.AddDirectionalLight(Vector3.up, (top - hor) * 0.5f, 1f);
            RenderSettings.ambientProbe = sh;

            // ground shadows: softer when the light is weak
            float strength = castShadows ? shadowStrength * Mathf.Clamp01(s.directAmount * 1.2f) : 0f;
            Shader.SetGlobalVector(SkyIds.CloudShadowParams, new Vector4(castShadows ? 1f : 0f, strength, 0, 0));
        }
    }
}
