// Painted Sky — shader API shared by the sky dome, the clouds and your own shaders.
//
// Include it from any URP shader to follow the time of day:
//   #include "Packages/com.paintedsky.core/Shaders/PaintedSkyCommon.hlsl"
// All colors are sRGB ("screen space", like a painter's palette): convert with PaintedSkyToLinear()
// before writing them to a linear render target.
#ifndef PAINTED_SKY_COMMON_INCLUDED
#define PAINTED_SKY_COMMON_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
// Main light cookie (the clouds write their ground shadow there). Include-guarded by URP, safe with Lighting.hlsl.
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LightCookie/LightCookieInput.hlsl"

// ---- Globals published by DayNightCycle ----
float4 _SkyLightDir;       // xyz = main light for shading (sun by day, moon by night), minimum elevation applied
float4 _SkySunDir;         // xyz = real sun (can be below the horizon), w = sun disc visibility
float4 _SkyMoonDir;        // xyz = moon, w = moon visibility
float4 _SkyZenith;         // sky color straight up
float4 _SkyHorizon;        // sky color at the horizon = distance fog color
float4 _SkyAmbient;        // ambient (sky) light
float4 _SkyDirect;         // direct light (sun or moon), with its horizon fade
float _SkyDirectAmount;    // direct light strength, 1 = noon (use it for backlight and sheen)
float4 _SkyGlow;           // rgb = sunrise/sunset glow, a = strength
float4 _SkyFogScatter;     // rgb = fog color towards the light, a = amount
float4 _SkyNight;          // x = stars, y = nightness (0 day .. 1 night)
float4 _SkyCloudLit;       // lit side of the clouds
float4 _SkyCloudShade;     // shadow side of the clouds
float4 _SkyCloudLightDir;  // xyz = light on the clouds, w = strength
float _SkyTime;            // seconds since level load (twinkling, etc.)

// ---- Globals published by PaintedClouds ----
float4 _SkyCloudShadowParams;   // x = 1 when clouds cast shadows, y = shadow strength

// Noon values of the default palette (lets painted shaders express light relative to noon).
static const float3 PAINTED_SKY_AMBIENT_NOON = float3(0.56, 0.64, 0.82);
static const float3 PAINTED_SKY_DIRECT_NOON = float3(0.50, 0.38, 0.16);

float3 PaintedSkyToLinear(float3 srgb)
{
#if defined(UNITY_COLORSPACE_GAMMA)
    return srgb;
#else
    return SRGBToLinear(saturate(srgb));
#endif
}

// ---- Lighting helpers ----

// Stylized light: cool sky light everywhere + warm direct light where lit (lit = 0 in shadow, 1 in light;
// directFactor = slope/normal term, e.g. saturate(dot(N, _SkyLightDir.xyz))).
float3 PaintedSkyLight(float3 albedo, float lit, float directFactor)
{
    return albedo * (_SkyAmbient.rgb + _SkyDirect.rgb * lit * directFactor);
}

// For hand-painted colors already lit for noon (e.g. "lit" and "shade" swatches): how much the current light
// differs from noon. l = 0 in shadow, 1 in light. Returns 1 at noon with the default palette.
float3 PaintedSkyTint(float l)
{
    return (_SkyAmbient.rgb + _SkyDirect.rgb * l) / (PAINTED_SKY_AMBIENT_NOON + PAINTED_SKY_DIRECT_NOON * l);
}

// Distance fog towards the horizon color; it turns to the glow color when looking at the light.
// density ~0.01 for a few hundred meters of visibility.
float3 PaintedSkyFog(float3 col, float3 positionWS, float density)
{
    float3 toP = positionWS - _WorldSpaceCameraPos;
    float d = length(toP);
    float f = 1.0 - exp(-pow(abs(d * density), 1.6));
    float3 V = toP / max(d, 0.001);
    float lightAmt = pow(saturate(dot(V, _SkyLightDir.xyz)), 5.0);
    float3 fogC = lerp(_SkyHorizon.rgb, _SkyFogScatter.rgb, lightAmt * _SkyFogScatter.a);
    return lerp(col, fogC, saturate(f));
}

// Cloud shadow on the ground at positionWS: 0 = no cloud, up to the shadow strength under a dense cloud.
// Multiply your direct light by (1 - PaintedSkyCloudShadow(p)).
float PaintedSkyCloudShadow(float3 positionWS)
{
    if (_SkyCloudShadowParams.x < 0.5) return 0.0;
    float2 uv = mul(_MainLightWorldToLight, float4(positionWS, 1.0)).xy * 0.5 + 0.5;
    if (any(uv < 0.0) || any(uv > 1.0)) return 0.0;
    float t = SAMPLE_TEXTURE2D_LOD(_MainLightCookieTexture, sampler_MainLightCookieTexture, uv, 0).r;
    return saturate(1.0 - t) * _SkyCloudShadowParams.y;
}

// ---- Sky ----

// Sky gradient without sun or stars: horizon to zenith, plus the sunrise/sunset glow along the horizon.
float3 PaintedSkyGradient(float3 dir)
{
    float h = dir.y;
    float3 col = lerp(_SkyHorizon.rgb, _SkyZenith.rgb, pow(smoothstep(0.0, 0.65, max(h, 0.0)), 0.75));
    float sd = saturate(dot(dir, _SkySunDir.xyz));
    float band = pow(1.0 - saturate(abs(h) * 2.2), 2.0);
    col = lerp(col, _SkyGlow.rgb, saturate(_SkyGlow.a * band * (0.25 + 0.75 * pow(sd, 2.5))));
    return col;
}

// ---- Noise ----

float PaintedSkyHash12(float2 p)
{
    float3 p3 = frac(p.xyx * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

float3 PaintedSkyHash33(float3 p)
{
    p = frac(p * float3(0.1031, 0.1030, 0.0973));
    p += dot(p, p.yxz + 33.33);
    return frac((p.xxy + p.yxx) * p.zyx);
}

float PaintedSkyNoise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    float2 u = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(PaintedSkyHash12(i), PaintedSkyHash12(i + float2(1.0, 0.0)), u.x),
                lerp(PaintedSkyHash12(i + float2(0.0, 1.0)), PaintedSkyHash12(i + float2(1.0, 1.0)), u.x), u.y);
}

float PaintedSkyFbm(float2 p)
{
    return PaintedSkyNoise(p) * 0.55 + PaintedSkyNoise(p * 2.07 + 5.2) * 0.3 + PaintedSkyNoise(p * 4.31 + 1.7) * 0.15;
}

#endif
