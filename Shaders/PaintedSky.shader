// Painted sky dome: palette gradient, sunrise/sunset glow, painted sun, big pale moon, stars and a faint
// milky way. Drawn at the far plane after the opaques; the volumetric clouds are composited on top later.
Shader "Painted Sky/Sky"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry+499" }
        Pass
        {
            Name "PaintedSky"
            Tags { "LightMode"="UniversalForward" }
            Cull Off
            ZWrite Off
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "PaintedSkyCommon.hlsl"

            struct Attributes { float3 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.dir = IN.positionOS;
                float4 p = TransformObjectToHClip(IN.positionOS);
            #if UNITY_REVERSED_Z
                p.z = p.w * 1e-6;
            #else
                p.z = p.w * 0.999999;
            #endif
                OUT.positionCS = p;
                return OUT;
            }

            // Stars: points on a grid over the sphere, each with its own brightness and twinkle
            float3 Stars(float3 d)
            {
                float3 p = d * 150.0;
                float3 i = floor(p), f = frac(p);
                float3 h = PaintedSkyHash33(i);
                if (h.x < 0.985) return 0;
                float b = (h.x - 0.985) / 0.015;
                float3 c = 0.3 + 0.4 * PaintedSkyHash33(i + 7.3);
                float r = lerp(0.12, 0.24, b * b);
                float tw = 0.7 + 0.3 * sin(_SkyTime * (1.2 + h.y * 3.5) + h.z * 40.0);
                float s = smoothstep(r, 0.0, length(f - c)) * lerp(0.35, 1.3, b) * tw;
                float3 tint = lerp(float3(1.0, 0.9, 0.78), float3(0.8, 0.88, 1.0), h.y);
                return tint * s;
            }

            // Big pale moon with soft painted spots
            float3 Moon(float3 d)
            {
                float3 M = _SkyMoonDir.xyz;
                float md = dot(d, M);
                float disc = smoothstep(0.99905, 0.99925, md);
                float3 col = float3(0.62, 0.70, 0.92) * (pow(saturate(md), 900.0) * 0.35 + pow(saturate(md), 60.0) * 0.08);
                if (disc > 0.0)
                {
                    float3 tx = normalize(cross(M, float3(0.0, 1.0, 0.0)));
                    float3 ty = cross(tx, M);
                    float2 q = float2(dot(d, tx), dot(d, ty)) / 0.043;
                    float m = PaintedSkyFbm(q * 2.6 + 4.0);
                    float3 mc = float3(1.0, 0.97, 0.88) * lerp(0.80, 1.06, smoothstep(0.35, 0.7, m));
                    col += mc * disc;
                }
                return col;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                float3 d = normalize(IN.dir);
                float h = d.y;
                float above = smoothstep(-0.02, 0.03, h);
                float3 col = PaintedSkyGradient(d);

                // night: faint milky way, stars and moon
                float night = _SkyNight.x;
                if (night > 0.001)
                {
                    float band = exp(-pow(dot(d, normalize(float3(0.35, 0.55, 0.76))) * 3.2, 2.0));
                    float neb = PaintedSkyFbm(d.xz / (abs(d.y) + 0.6) * 4.0 + 3.0);
                    col += float3(0.30, 0.34, 0.58) * band * smoothstep(0.35, 0.8, neb) * 0.16 * night * above;
                    col += Stars(d) * night * smoothstep(0.0, 0.25, h);
                }
                col += Moon(d) * _SkyMoonDir.w * above;

                // painted sun: small bright core plus a warm layered glow (the clouds cover it later)
                float sd = saturate(dot(d, _SkySunDir.xyz));
                float3 sunC = lerp(float3(1.0, 0.95, 0.82), _SkyGlow.rgb, saturate(_SkyGlow.a * 0.6));
                float core = smoothstep(0.99975, 0.99988, sd);
                float glow = pow(sd, 600.0) * 0.55 + pow(sd, 90.0) * 0.22;
                col += (sunC * core * 1.3 + lerp(sunC, _SkyGlow.rgb, 0.5) * glow) * _SkySunDir.w * above;
                // wide halo: light scattered by the air
                col += sunC * pow(sd, 10.0) * 0.16 * _SkySunDir.w;
                return float4(PaintedSkyToLinear(col), 1.0);
            }
            ENDHLSL
        }
    }
}
