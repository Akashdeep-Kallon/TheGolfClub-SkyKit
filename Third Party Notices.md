# Third Party Notices

## UnityVolumetricCloudsURP

- Source: https://github.com/jiaozi158/UnityVolumetricCloudsURP
- License: MIT, Copyright (c) 2024 jiaozi158 (full text in `ThirdParty/VolumetricCloudsURP/LICENSE.txt`)
- Location: `ThirdParty/VolumetricCloudsURP`
- Changes, all marked `[Painted Sky]` in the code:
  - `VolumetricCloudsURP.cs`: the Compatibility Mode render-pass methods (`OnCameraSetup` / `Execute`) are
    excluded on Unity 6000.6+, where URP only has the Render Graph path.
  - `VolumetricClouds.hlsl`, `VolumetricCloudsUtilities.hlsl`, `VolumetricCloudsDefs.hlsl`: painted lighting mode
    (`_SkyGhibliClouds`): average sun visibility without the phase function, per-lobe normal from the density
    gradient, light ramp, milky shadows, lobe rim, silver lining and aerial perspective, colored by the
    Painted Sky palette globals.

## Custom-Shader (techniques)

- Source: https://github.com/yuanqiubai/Custom-Shader
- License: MIT, Copyright (c) 2026 yuanqiubai
- No files are copied. The painted cloud lighting re-implements its ideas in HLSL: `ToonRemapGradient`
  (light through a soft ramp), `ElevateBlack` (lifted, milky shadows), `ConstructNormal` (normal from the
  gradient of a field) and `HalfLambert`.
