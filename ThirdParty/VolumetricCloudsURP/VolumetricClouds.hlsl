#ifndef URP_VOLUMETRIC_CLOUDS_HLSL
#define URP_VOLUMETRIC_CLOUDS_HLSL

#include "./VolumetricCloudsDefs.hlsl"
#include "./VolumetricCloudsUtilities.hlsl"

CloudRay BuildCloudsRay(float2 screenUV, float depth, half3 invViewDirWS, bool isOccluded)
{
    CloudRay ray;

#ifdef _LOCAL_VOLUMETRIC_CLOUDS
    ray.originWS = GetCameraPositionWS();
#else
    ray.originWS = float3(0.0, 0.0, 0.0);
#endif

    ray.direction = invViewDirWS;

    // Compute the max cloud ray length
    // For opaque objects, we only care about clouds in front of them.
#ifdef _LOCAL_VOLUMETRIC_CLOUDS
    // The depth may from a high-res texture which isn't ideal but can save performance.
    float distance = LinearEyeDepth(depth, _ZBufferParams) * rcp(dot(ray.direction, -UNITY_MATRIX_V[2].xyz));
    ray.maxRayLength = lerp(MAX_SKYBOX_VOLUMETRIC_CLOUDS_DISTANCE, distance, isOccluded);
#else
    ray.maxRayLength = MAX_SKYBOX_VOLUMETRIC_CLOUDS_DISTANCE;
#endif

    ray.integrationNoise = GenerateRandomFloat(screenUV);

    return ray;
}

VolumetricRayResult TraceVolumetricRay(CloudRay cloudRay)
{
    // Initiliaze the volumetric ray
    VolumetricRayResult volumetricRay;
    volumetricRay.scattering = 0.0;
    volumetricRay.ambient = 0.0;
    volumetricRay.sunVis = 0.0;
    volumetricRay.transmittance = 1.0;
    volumetricRay.meanDistance = FLT_MAX;
    volumetricRay.invalidRay = true;

    // Determine if ray intersects bounding volume, if the ray does not intersect the cloud volume AABB, skip right away
    RayMarchRange rayMarchRange;
    if (GetCloudVolumeIntersection(cloudRay.originWS, cloudRay.direction, rayMarchRange))
    {
        if (cloudRay.maxRayLength >= rayMarchRange.start)
        {
            // Initialize the depth for accumulation
            volumetricRay.meanDistance = 0.0;

            // Total distance that the ray must travel including empty spaces
            // Clamp the travel distance to whatever is closer
            // - Sky Occluder
            // - Volume end
            // - Far plane
            float totalDistance = min(rayMarchRange.end, cloudRay.maxRayLength) - rayMarchRange.start;

            // Evaluate our integration step
            float stepS = min(totalDistance / (float)_NumPrimarySteps, _MaxStepSize);
            totalDistance = stepS * _NumPrimarySteps;

            // Compute the environment lighting that is going to be used for the cloud evaluation
            float3 rayMarchStartPS = ConvertToPS(cloudRay.originWS) + rayMarchRange.start * cloudRay.direction;
            float3 rayMarchEndPS = rayMarchStartPS + totalDistance * cloudRay.direction;

            // Tracking the number of steps that have been made
            int currentIndex = 0;

            // Normalization value of the depth
            float meanDistanceDivider = 0.0;

            // Current position for the evaluation, apply blue noise to start position
            float currentDistance = cloudRay.integrationNoise;
            float3 currentPositionWS = cloudRay.originWS + (rayMarchRange.start + currentDistance) * cloudRay.direction;

            // Initialize the values for the optimized ray marching
            bool activeSampling = true;
            int sequentialEmptySamples = 0;

            // Do the ray march for every step that we can.
            while (currentIndex < (int)_NumPrimarySteps && currentDistance < totalDistance)
            {
                // Compute the camera-distance based attenuation
                float densityAttenuationValue = DensityFadeValue(rayMarchRange.start + currentDistance);
                // Compute the mip offset for the erosion texture
                float erosionMipOffset = ErosionMipOffset(rayMarchRange.start + currentDistance);

                // Accumulate in WS and convert at each iteration to avoid precision issues
                float3 currentPositionPS = ConvertToPS(currentPositionWS);

                // Should we be evaluating the clouds or just doing the large ray marching
                if (activeSampling)
                {
                    // If the density is null, we can skip as there will be no contribution
                    CloudProperties properties;
                    EvaluateCloudProperties(currentPositionPS, 0.0, erosionMipOffset, false, false, properties);

                    // Apply the fade in function to the density
                    properties.density *= densityAttenuationValue;

                    if (properties.density > CLOUD_DENSITY_TRESHOLD)
                    {
                        // Contribute to the average depth (must be done first in case we end up inside a cloud at the next step)
                        half transmitanceXdensity = volumetricRay.transmittance * properties.density;
                        volumetricRay.meanDistance += (rayMarchRange.start + currentDistance) * transmitanceXdensity;
                        meanDistanceDivider += transmitanceXdensity;

                        // Evaluate the cloud at the position
                        EvaluateCloud(properties, cloudRay.direction, currentPositionPS, stepS, currentDistance / totalDistance, volumetricRay);

                        // if most of the energy is absorbed, just leave.
                        if (volumetricRay.transmittance < 0.003)
                        {
                            volumetricRay.transmittance = 0.0;
                            break;
                        }

                        // Reset the empty sample counter
                        sequentialEmptySamples = 0;
                    }
                    else
                        sequentialEmptySamples++;

                    // If it has been more than EMPTY_STEPS_BEFORE_LARGE_STEPS, disable active sampling and start large steps
                    if (sequentialEmptySamples == EMPTY_STEPS_BEFORE_LARGE_STEPS)
                        activeSampling = false;

                    // Do the next step
                    float relativeStepSize = lerp(cloudRay.integrationNoise, 1.0, saturate(currentIndex));
                    currentPositionWS += cloudRay.direction * stepS * relativeStepSize;
                    currentDistance += stepS * relativeStepSize;

                }
                else
                {
                    CloudProperties properties;
                    EvaluateCloudProperties(currentPositionPS, 1.0, 0.0, true, false, properties);

                    // Apply the fade in function to the density
                    properties.density *= densityAttenuationValue;

                    // If the density is lower than our tolerance,
                    if (properties.density < CLOUD_DENSITY_TRESHOLD)
                    {
                        currentPositionWS += cloudRay.direction * stepS * 2.0;
                        currentDistance += stepS * 2.0;
                    }
                    else
                    {
                        // Somewhere between this step and the previous clouds started
                        // We reset all the counters and enable active sampling
                        currentPositionWS -= cloudRay.direction * stepS;
                        currentDistance -= stepS;
                        currentIndex -= 1;
                        activeSampling = true;
                        sequentialEmptySamples = 0;
                    }
                }
                currentIndex++;
            }

            // Normalized the depth we computed
            if (volumetricRay.meanDistance != 0.0)
            {
                volumetricRay.invalidRay = false;
                volumetricRay.meanDistance /= meanDistanceDivider;
                volumetricRay.meanDistance = min(volumetricRay.meanDistance, cloudRay.maxRayLength);

                float3 currentPositionPS = ConvertToPS(cloudRay.originWS) + volumetricRay.meanDistance * cloudRay.direction;
                float relativeHeight = EvaluateNormalizedCloudHeight(currentPositionPS);

                Light sun = GetMainLight();

                // Evaluate the sun color at the position
            #ifdef _PHYSICALLY_BASED_SUN
                half3 sunColor = _SunColor * EvaluateSunColorAttenuation(currentPositionPS, sun.direction, true) * _SunLightDimmer; // _SunColor includes PI
            #else
                half3 sunColor = sun.color * PI * _SunLightDimmer;
            #endif

                // Evaluate the environement lighting contribution
            #ifdef _CLOUDS_AMBIENT_PROBE
                half3 ambientTermTop = SAMPLE_TEXTURECUBE_LOD(_VolumetricCloudsAmbientProbe, sampler_VolumetricCloudsAmbientProbe, half3(0.0, 1.0, 0.0), 4.0).rgb;
                half3 ambientTermBottom = SAMPLE_TEXTURECUBE_LOD(_VolumetricCloudsAmbientProbe, sampler_VolumetricCloudsAmbientProbe, half3(0.0, -1.0, 0.0), 4.0).rgb;
            #else
                half3 ambientTermTop = EvaluateVolumetricCloudsAmbientProbe(half3(0.0, 1.0, 0.0));
                half3 ambientTermBottom = EvaluateVolumetricCloudsAmbientProbe(half3(0.0, -1.0, 0.0));
            #endif
                half3 ambient = max(0, lerp(ambientTermBottom, ambientTermTop, relativeHeight) * _AmbientProbeDimmer);

                half3 sunScattering = volumetricRay.scattering;
                volumetricRay.scattering = sunColor * volumetricRay.scattering;
                volumetricRay.scattering += ambient * volumetricRay.ambient;

                // [Painted Sky] Modo pintado estilo Ghibli: la forma y la opacidad son las del volumen,
                // pero el color se pinta con la paleta del ciclo de día y noche, como un fondo a mano.
                // Técnica de yuanqiubai/Custom-Shader: ToonRemapGradient (la luz pasa por una rampa: masas de
                // color con un borde suave) + ElevateBlack (las sombras nunca se van a negro, quedan lechosas).
                if (_SkyGhibliClouds > 0.0)
                {
                    half opacity = max(1.0 - volumetricRay.transmittance, 1e-3);
                    half sunVis = saturate(volumetricRay.sunVis / opacity);
                    half ambVis = saturate(volumetricRay.ambient / opacity);

                    // [Painted Sky] Per-lobe volume (Custom-Shader's ConstructNormal + HalfLambert):
                    // a normal for each cauliflower lobe from the density gradient, so every lump gets its
                    // own lit side and its own soft blue shadow, like the painted cumulus in Ghibli films.
                    // Sampled on the big shape only (cheap version, no erosion): broad, smooth painted lobes.
                    float e = _SkyGhibliShape.x;
                    CloudProperties c0, cx, cy, cz;
                    EvaluateCloudProperties(currentPositionPS, 0.0, 0.0, true, false, c0);
                    EvaluateCloudProperties(currentPositionPS + float3(e, 0.0, 0.0), 0.0, 0.0, true, false, cx);
                    EvaluateCloudProperties(currentPositionPS + float3(0.0, e, 0.0), 0.0, 0.0, true, false, cy);
                    EvaluateCloudProperties(currentPositionPS + float3(0.0, 0.0, e), 0.0, 0.0, true, false, cz);
                    float3 grad = float3(cx.density - c0.density, cy.density - c0.density, cz.density - c0.density);
                    half3 lobeN = dot(grad, grad) > 1e-10 ? (half3)(-normalize(grad)) : (half3)(-cloudRay.direction);
                    // lobes facing away from the camera are hidden: bend them back towards it
                    half facing = dot(lobeN, -cloudRay.direction);
                    lobeN = normalize(lobeN + (half3)(-cloudRay.direction) * saturate(-facing + 0.1));
                    half halfLambert = dot(lobeN, sun.direction) * 0.5 + 0.5;

                    // light = big-shape self shadow (sunVis) x lobe shading (halfLambert)
                    half light = lerp(sunVis, halfLambert * lerp(0.45, 1.0, sunVis), _SkyGhibliShape.y);
                    // ToonRemapGradient: masses of colour with a soft edge
                    half ramp = smoothstep(_SkyGhibliRamp.x, _SkyGhibliRamp.y, light);
                    // ElevateBlack: shadows never go to black, they stay milky
                    ramp = ramp * _SkyGhibliRamp.z + (1.0 - _SkyGhibliRamp.z);
                    ramp *= _SkyCloudLightDir.w;

                    half3 litCol = SRGBToLinear(_SkyCloudLit.rgb);
                    half3 shadeCol = SRGBToLinear(_SkyCloudShade.rgb) * lerp(0.72, 1.06, saturate(relativeHeight * 1.6)) * lerp(0.85, 1.0, ambVis);
                    // lobe tops catch the sky light, lobe bellies are darker
                    shadeCol *= lerp(1.0 - _SkyGhibliShape.w, 1.0 + _SkyGhibliShape.w, saturate(lobeN.y * 0.5 + 0.5));
                    half3 painted = lerp(shadeCol, litCol, ramp);
                    // soft rim on lit lobe silhouettes (the bright outline of painted cumulus)
                    half rim = pow(1.0 - saturate(dot(lobeN, -cloudRay.direction)), 3.0);
                    painted += litCol * rim * ramp * _SkyGhibliShape.z;
                    // silver lining against the light, where the cloud is thin
                    half cosSun = dot(cloudRay.direction, sun.direction);
                    half silver = pow(saturate(cosSun), 6.0) * saturate(Luminance(sunScattering) / opacity) * 2.0;
                    painted += litCol * silver * _SkyCloudLightDir.w * _SkyGhibliRamp.w;
                    // aerial perspective: far clouds melt into the horizon colour
                    half haze = 1.0 - exp(-volumetricRay.meanDistance / 40000.0);
                    painted = lerp(painted, SRGBToLinear(_SkyHorizon.rgb), haze * 0.55);
                    volumetricRay.scattering = lerp(volumetricRay.scattering, painted * opacity, _SkyGhibliClouds);
                }
            }
        }
    }
    return volumetricRay;
}

#endif