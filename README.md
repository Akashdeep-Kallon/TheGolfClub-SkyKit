# Painted Sky — day/night cycle + volumetric clouds (URP)

Stylized, Ghibli-like sky for Unity 6 + URP 17:

- **Day/night cycle** (`DayNightCycle`) driven by a hand-painted palette (`SkyPalette`): night, blue hour,
  peach sunrise, fresh morning, noon, golden hour, orange sunset with purple shadows, pink dusk. Sun rises at
  6:00 and sets at 18:00 on a tilted arc; the moon takes over at night without a pop.
- **Sky dome** (`SkyDome` + `Painted Sky/Sky` shader): palette gradient, horizon glow, painted sun with a warm
  glow, big pale moon, twinkling stars, faint milky way. Works with a Solid Color camera clear, no skybox.
- **Volumetric clouds** (`PaintedClouds`): huge cumulus towers rendered by the bundled
  [UnityVolumetricCloudsURP](https://github.com/jiaozi158/UnityVolumetricCloudsURP), with a **painted mode**:
  the volume gives shape and opacity, the colors come from the palette, each cauliflower lobe gets its own lit
  side and soft blue shadow. Clouds drift with the wind, their shadows cross the ground.
- **Shader API** (`PaintedSkyCommon.hlsl`) so your own shaders follow the time of day.

## Install in another project

Requirements: Unity 6000.0+, URP 17 (Render Graph), HDR on in the URP asset.

1. Add it to the project's `Packages/manifest.json` from git (Package Manager ▸ + ▸ Add package from git URL
   works too):
   `"com.paintedsky.core": "https://github.com/Akashdeep-Kallon/TheGolfClub-SkyKit.git"`
   (append `#<commit or tag>` to pin a version). To edit it inside a project, clone this repository into that
   project's `Packages/com.paintedsky.core` folder instead (embedded package).
2. **Tools ▸ Painted Sky ▸ Add Painted Sky To Main Camera**. It adds `DayNightCycle`, `SkyDome` and
   `PaintedClouds` to the main (or selected) camera and installs the clouds renderer feature in every URP
   renderer of the project (**Tools ▸ Painted Sky ▸ Install Clouds Renderer Feature** does only that part).
3. Set the camera clear to Solid Color (the cycle paints it with the horizon color) and press Play.

Everything is tuned on the three components. For your own palette: **Assets ▸ Create ▸ Painted Sky ▸ Sky
Palette** (starts as a copy of the default) and assign it to `DayNightCycle.palette`.

## Components

| Component | What it does | Main settings |
|---|---|---|
| `DayNightCycle` | Time, sun/moon, palette blend, shader globals, `State` for C# | start hour, day length, time scale, palette, sun arc (tilt, azimuth or a reference sun direction), `manualTick` |
| `SkyDome` | Sky sphere that follows the camera | shader, radius (keep it inside the far plane) |
| `PaintedClouds` | Clouds volume, cloud light, ambient, ground shadows | coverage, density, wind, altitude, shape, painted amount, light ramp, per-lobe shading, shadows, quality |

Driving it from game code:

```csharp
var cycle = DayNightCycle.Active;
cycle.Hour = 18f;                 // jump to sunset
cycle.timeScale = 30f;            // timelapse (clouds speed up too, up to PaintedClouds.maxTimelapse)
SkyState s = cycle.State;         // light direction, colors, nightness...
cycle.Ticked += c => { /* react every frame, e.g. re-bake shadows when s.lightDirection moved */ };
// Own loop: cycle.manualTick = true; then call cycle.Tick(dt) yourself.
```

## Shader API

```hlsl
#include "Packages/com.paintedsky.core/Shaders/PaintedSkyCommon.hlsl"

float3 albedo = ...;                                   // sRGB, like the palette
float lit = 1.0 - PaintedSkyCloudShadow(positionWS);    // cloud shadows on the ground
float ndl = saturate(dot(normalWS, _SkyLightDir.xyz));
float3 col = PaintedSkyLight(albedo, lit, ndl);         // cool sky light + warm direct light
col = PaintedSkyFog(col, positionWS, 0.01);             // fog to the horizon color, glow towards the light
return float4(PaintedSkyToLinear(col), 1);
```

Globals (all sRGB): `_SkyLightDir`, `_SkySunDir` (w = disc visibility), `_SkyMoonDir`, `_SkyZenith`,
`_SkyHorizon`, `_SkyAmbient`, `_SkyDirect`, `_SkyDirectAmount`, `_SkyGlow`, `_SkyFogScatter`, `_SkyNight`
(x = stars, y = nightness), `_SkyCloudLit`, `_SkyCloudShade`, `_SkyCloudLightDir`, `_SkyTime`,
`_SkyCloudShadowParams`. Helpers: `PaintedSkyLight`, `PaintedSkyTint` (for colors painted for noon),
`PaintedSkyFog`, `PaintedSkyCloudShadow`, `PaintedSkyGradient`, `PaintedSkyFbm`.

## Integrating with a game that has its own lighting

Two ways, pick per project:

- **Shader macros** (what THE GOLF CLUB's meadow does): include `PaintedSkyCommon.hlsl` in the game's common
  shader file and `#define` the game's light/fog globals to the `_Sky*` ones
  (e.g. `#define _GameSunDir _SkyLightDir`). No C# glue.
- **C# bridge**: read `DayNightCycle.State` every tick (`Ticked` event) and write it into the game's own
  environment system (light direction, ambient, fog color...). Use this when the game already has a central
  environment component that other systems read from.

Game-specific work stays in the game, as an adapter: baked shadows that depend on the sun direction, post
processing tints per time of day, HUD clock, input. See `Assets/PraderaDorada/Scripts/PraderaDayNight.cs` in
THE GOLF CLUB for a complete example.

## Notes

- The clouds need a directional light for their sun and shadow cookie: `PaintedClouds` creates its own and
  sets it as `RenderSettings.sun`. Unlit games are not affected; lit games will see this light (tint it or
  set its culling mask if needed).
- Ambient for the clouds is set from the palette (`RenderSettings.ambientProbe`, renderer feature ambient mode
  Static): no skybox needed.
- Third-party code and licenses: see `Third Party Notices.md`.
