# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this project is

Soledad is a Unity project (Editor `6000.3.15f1`, HDRP) for one piece in two forms: a fulldome short film
(also exported in 16:9) and a 45 min live performance where Gamgie speaks the text and plays the visuals live over
recorded music. Everything is driven from Chataigne over OSCQuery. The film is edited in DaVinci Resolve, with some
effects added in TouchDesigner. Everything in this repo is written in English.

The visual engine is **Oxipital**, installed as two packages by git URL and tag (`Packages/manifest.json`):
`com.gamgie.oxipital.core` and `com.gamgie.oxipital.camera` from
[Oxipital-Packages](https://github.com/Gamgie/Oxipital-Packages). Their architecture and packaging rules are in that
repository's `CLAUDE.md`. Engine features (anything generic, such as a light ballet or a render type blend) are
developed in [Oxipital-Engine](https://github.com/Gamgie/Oxipital-Engine), released with a new tag, then picked up
here by changing `#vX.Y.Z` in the manifest. Never edit the package copies under `Library/PackageCache`.

Soledad is the first show project built this way: it must never depend on Oxipital-Engine's `Assets`.

## The show: five acts

| # | Scene | Act |
|---|---|---|
| 1 | `Assets/Scenes/Acts/Act1_BeyondTheVeil.unity` | Beyond the Veil: screens, smoke, a Tron-like digital world |
| 2 | `Assets/Scenes/Acts/Act2_Flythrough.unity` | Flythrough: a bird's flight over desert, mountains, the Bogotá valley |
| 3 | `Assets/Scenes/Acts/Act3_Abyss.unity` | Abyss: lit particles, noise architectures, volumetric light, flocking |
| 4 | `Assets/Scenes/Acts/Act4_SeeThrough.unity` | See Through: the tree, insects, the invisible links |
| 5 | `Assets/Scenes/Acts/Act5_MementoMori.unity` | Memento Mori: Earth from the Moon, light ballet, gold and violet finale |

The moodboard, intentions and text of each act are in Notion (Art > Soledad > Création > Moodboard).

## Scene architecture

- `Assets/Scenes/Soledad_Main.unity` is always loaded and is the active scene. It holds everything permanent:
  - `Root Scene`: the object OSCQuery exposes (`OSCQuery.rootObject`), so Chataigne addresses start under it, as in
    Oxipital-Engine (the Chataigne module looks for a top-level `ballet`). It contains `Cameras` (Oxipital Camera
    prefab), `Ballet` (Oxipital Core prefab), `Show` (`ShowController`), `Sun`, `Sky and Fog Volume`, and the loaded
    acts.
  - `OSCQuery`: server `Unity-Oxipital` on port 8999, same filters as Oxipital-Engine.
  - `Dome Creator` (pfc dome-tools): the fulldome rig. `CameraController.isFullDome` switches between the dome
    (main camera off, `Dome Creator` renders `Assets/Textures/Dome Content Texture` and the DomeMaster
    `Dome Output Texture`) and 16:9 (`Dome Creator` disabled). `DomeCameraRigFollow` on its rig follows
    `Cameras/Main Camera` through `Dome Target`. Wired like Oxipital-Engine's `Oxipital Dome` scene.
- Each act is its own scene with **one root object named like the scene, carrying an `Act` component**. Only
  environment content goes there (meshes, terrain, point clouds, lights, local Volumes, act-specific scripts).
- `ShowController` (`Assets/Scripts/Show`): OSCQuery only sees `Root Scene`, so an act scene is loaded additively,
  its root is moved under `Root Scene` in the main scene, and the emptied act scene is unloaded at once. Setting
  `act` (1 to 5, 0 = none; also `NextAct()` / `PreviousAct()`) crossfades `Act.weight` over `transitionDuration`,
  keeps only the shown act and the next one (preloaded, inactive), and destroys the others. Loaded act roots are
  added to OSCQuery's excluded objects (`hideActsFromOSCQuery`, on by default; OSCQuery's object filter must stay in
  Exclude mode), so the Chataigne tree is the same whichever act is loaded and a resync rebuilds it identically.
  Act parameters are reached through the generated controllers on `Show` (below).
- **Act parameters for Chataigne and Timeline**: act objects come and go, so their addresses are not stable. Mark
  the act script members to drive with `[Animate]` (`Assets/Scripts/Show/AnimateAttribute.cs`; public fields of
  type float, int, bool, enum, Color, Vector3, and parameterless public methods; `[Animate("fixedName")]` keeps the
  address when the field is renamed). After each compile, `ActControllerGenerator` (`Assets/Scripts/Show/Editor`,
  also menu `Soledad > Generate Act Controllers`) writes `Assets/Scripts/Show/Generated/ActNController.cs` for each
  `Assets/Acts/ActN_Name/` folder: flat fields `Type_field` (same default, Range, Tooltip) and forwarding methods.
  Never edit them by hand. The generator adds missing controllers to `Show` (Soledad_Main must be open, then save
  it) and copies new fields' values from the act scene when it is open (`Soledad > Pull Act Controller Values From
  Open Acts` copies all of them). In Play mode the controller (`ActController`) is the source of truth: it binds
  to its act root under `Root Scene` once loaded, pushes its values every frame (also while the act is preloaded),
  and drops the references when the act is destroyed. So drive and record (Timeline) `Show/ActNController`, not the
  act objects: values set on the act itself are overwritten. One component of each type per act is driven. If an
  `[Animate]` field is renamed or removed, the generated code stops compiling, the generator writes a stub and the
  next compile restores it; the controller value of a renamed field is lost unless it had a fixed name.
- `Act.weight` scales every Volume under the act, so two acts blend during a transition. Act-specific scripts read it
  to fade their own content.
- Working on an act in the Editor: open `Soledad_Main`, then the act scene additively. On Play, `ShowController`
  takes over the open act scene and shows it. Keep `Soledad_Main` as the active scene.
- Object names under `Root Scene` and public fields are Chataigne addresses: do not rename them casually.
- `Player Settings > Run In Background` is on (as in Oxipital-Engine): the show keeps running while Chataigne or
  TouchDesigner has the focus.

## Act content

Each act keeps its own assets in `Assets/Acts/<ActScene>/` (Scripts, Shaders, Materials), code in `namespace Soledad`.
Same performance rule as Oxipital: no allocation in `Update`, `Shader.PropertyToID` for material properties.
Each act root holds a `<ActScene> Volume` (global, priority 10, profile `<ActScene> Volume Profile.asset` in the
act folder) for the act's own look; `Act` scales its weight, so it crossfades with the act. Act 1: black gradient
sky, no bloom, fog off.

- **Act 1, `GridTunnel`** (`Assets/Acts/Act1_BeyondTheVeil/`): endless box tunnel of thin grid lines whose cells are
  "windows" that light up (random blink, or one solo window: the lonely lit window of a building at night), deformed
  by a fractal noise like a Noise SOP. The script builds the dense mesh on the child `Grid Tunnel Mesh` (not saved
  in the scene) and slides it in whole cells to follow the camera named "Main Camera"; grid, blink and noise are
  computed in tunnel space so the slide is invisible. Two line families: rails (along the tunnel, drawn from the
  camera towards the end) and rings (around it, drawn from the middle of the floor up both sides), each with a
  `Reveal` (0 = no line, 1 = all drawn, meant to be animated from a Timeline or Chataigne), a `Stagger` (0 = all
  lines together, 1 = one after another; per line `saturate((reveal - stagger x id) / (1 - stagger))`), an order
  (Chase, Reverse, Random, Mirror) and a light effect modelled on Blux effects (None, Sine, Noise, Pulse, Saw:
  phase = time x speed - delay x line order), played on top of the reveal. Shader `Soledad/GridTunnel` is a hand-written HDRP unlit shader
  on HDRP's own passes (`DepthForwardOnly`, `ForwardOnly`, `SceneSelectionPass`), deforming vertices through the
  `HAVE_MESH_MODIFICATION` hook of `VertMesh.hlsl`; the displacement depends on position only so walls never crack
  at the corners. Its time is accumulated from `Time.deltaTime` (deterministic Recorder renders). Do not reference
  objects of `Soledad_Main` from an act scene (cross-scene references are dropped on save): find them at run time.

- **Act 2, `FlythroughTerrain`** (`Assets/Acts/Act2_Flythrough/`, on `Act2_Flythrough/Flythrough`): a bird's
  flight over an endless landscape laid out along a route (terrain +Z): desert, mountains with a winding pass, then
  the valley (fields between two ridges); shape in `TerrainShape` (`TerrainField.cs`, pure Burst-compatible height
  and biome functions). The landscape is fixed in the space of `Act2_Flythrough/World` (at the origin, no floating
  origin: HDRP renders camera-relative and the route is about 13 km) and the Oxipital camera really flies through it.
  Film: the **Spline camera** (Oxipital Camera 1.5.0, `Cameras/Spline Camera target`, `SplineMovement`) rides
  `World/Flight Path` (a Splines `SplineContainer`, editable), which the act hands to it in Play mode with itself as
  the ground (`Oxipital.ICameraGround`, `GetGroundHeight`), so it flies at its `altitude` above the (smoothed,
  anticipated) relief plus the spline's own height. Speed, altitude, offset, look (gamepad on `yaw1D` / `pitch1D` /
  `roll1D` in Spline And Look or Free), bank and seeded drift are set on that object (Soledad_Main: 60 m/s, 140 m,
  600 m look-ahead, no loop), not on the act. Show: the Spaceship camera flies freely. `ResetFlight()` (also on the
  first show of the act) restarts the ride and puts the Spaceship at the start of the path, at the same altitude.
  Movie mode for now (`startOnSplineCamera`, on): the first show of the act also switches the camera to Spline, so
  Play rides the path from its start (later the gamepad picks the mode).
  Switching back to Spline joins the path at the point nearest to the camera. The curl follows the camera: around
  the ground under it (smoothed), oriented by `curlFrame`: Camera Direction = along its smoothed direction of motion
  (`curlHeadingSmoothing`), Axis = along the fixed landscape direction `curlAxis` (degrees, 0 = +Z). The terrain's pass and valley follow the route centre line
  (`TerrainField.RouteX`), not the spline: keep the spline near it. Tiles (`Terrain (generated)` under `World`) form a quadtree around "Main Camera" (dense near
  it, coarse to the horizon, `detailDistance`), built in terrain space by a parallel Burst job with skirts hiding
  the seams, never saved. Shader `Soledad/FlythroughTerrain`: HDRP's own Lit passes (GBuffer, Forward, depth,
  shadows, motion vectors) with a hand-written surface (`FlythroughTerrainData.hlsl`: biome colours, rock on slopes,
  fields, per-pixel relief from terrain coordinates) and the world curl in the `HAVE_MESH_MODIFICATION` hook
  (`curlLateral` rolls the ground into a tube around the flight direction, `curlForward` lifts it ahead into a wall;
  in Play mode the act works on a copy of its Volume profile to thin the fog and fade the volumetric clouds while
  curled, `curlClearsFog` / `curlClearsClouds`, fully cleared at `curlClearRamp`; a deep copy of its components: a plain
  Instantiate of a VolumeProfile shares them and would edit the real profile);
  Sand placement: vertex colour alpha = dune level (0 trough, 1 crest)
  and material settings Ripple Coverage, Height Preference, Slope Limit, Patchiness, Border Sharpness;
  `_Smoothness` and other Lit names are taken by `LitProperties.hlsl`, so terrain properties are plain uniforms with
  their own names. In Play mode the act pushes every camera's far plane, and every Cinemachine camera's lens (the brain gives
  it to the main camera each frame), to `farClip` (40 km) and restores them when disabled. Act 2 Volume: sunset haze (fog base 0 m, fading out by 2.5 km, 2.2 km mean free path, warm albedo
  (1, 0.86, 0.72), volumetric with forward glow 0.8 towards the sun, white balance +14 / +4; the Physically
  Based Sky's own Atmospheric Scattering stays off: its aerial haze flickers on ridges near the sun while flying) and
  shadows out to 8 km (4 cascades). Desert: two scanned PBR sand sets (`Textures/rippled_sand_*`, `dry_sand_*`,
  2 x 2 m, Megascans layout: BaseColor, Normal, Displacement, Roughness, AO) height-blended (ripples on gentle
  slopes, dry sand on slip faces) plus a larger rotated sample against tiling. Mountains and foothills: volcanic
  ground scan (`_Volcanic*`, planar + anti-tiling sample; `_Ground*` names are taken by HDRP's sky); steep slopes in
  every biome: cliff rock scan (`_Rock*`), triplanar in terrain space from the pre-curl terrain normal (mesh uv2 =
  normal xz), height-blended (Rock Slope, Rock Slope Softness, Rock Border Sharpness). All scanned textures share one
  inline sampler (`sampler_trilinear_repeat_aniso8`): a pixel shader has only 16 samplers. Only BaseColor, Normal,
  Displacement, Roughness and AO maps are kept. Dunes in three size families and two
  wind regimes with draa underneath (`TerrainField.Dunes`). The act also aims the main scene's `Sun`
  (`sunElevation`, `sunAzimuth`, `sunTemperature`, animatable; set to 5 degrees, azimuth -81, 3500 K for a sunset;
  the Physically Based Sky reddens it near the horizon) and raises its shadow resolution to
  4096 while shown, restoring all of it (direction, colour, shadows) when disabled and around scene saves, so Soledad_Main never keeps them.
  For the film, the Bogota valley is a separate shot (cut in editing).
  Live transitions planned (performance): Act 1 to 2 = black cut, then the sun light fades in smoothly. Inside Act 2,
  valley to Bogota: the valley curls as the bird flies through it until it is a closed tube halfway; ahead, the
  Bogota terrain stands at 90 degrees, seen as from above; on reaching it the camera turns 90 degrees up and follows
  the Bogota terrain, the valley left behind (sky and sun orientation to be solved then). Act 2 to 3 = cut to the
  abyss as the bird enters a lake in the Bogota landscape.
  Cubic clouds: `World/Clouds` (`CloudField`, the cluster manager) spreads `clusterCount` `CloudCluster`s over the
  route at `altitude` above the ground, from its base cube, motion and look values, each varied by `variation`
  (+-20%), deterministic per seed, generated and never saved. A cluster is cubes that only cast the shadow
  (`Soledad/CloudCube`, `CloudCube.mat`, GPU instancing) plus one fake volume through all of them
  (`Soledad/CloudCluster`: smooth union of rounded boxes eroded by billowy noise, ray-marched, lit at the point
  where it becomes visible, fake forward scattering). Colour, scattering, Samples and shadow core come from
  `CloudCube.mat`; Fluff, Puff Size, Density and Softness from the field. No physics unless `collisions` (rigidbodies
  cost ~4 ms CPU for 40 clusters); far clusters take fewer samples and are hidden beyond `drawDistance`.
  The prefab `Prefabs/Cloud Cluster.prefab` is a single standalone cluster.
  Monoliths: `World/Monoliths` (`MonolithField`) spreads copies of `Prefabs/Monolith.prefab` along the route,
  outside `routeClearance` of the centre line, on the lowest ground under their foot, with base shape and growth
  values varied by `variation`, random yaw, generated and never saved. A `Monolith` is a standing triangular prism
  with rounded edges (mesh generated on the child `Slab`, Default layer: the Dome Creator cameras only render
  Default, TransparentFX, Ignore Raycast and Water, so never put act content on another layer), blurred mirror finish (`Monolith.mat`,
  HDRP Lit metallic), growing (Scale or Rise) by depth ahead of the camera along the route (Z), from `growStart` to `growEnd`, staying grown (`keepGrown`). Its own realtime
  reflection probe (128, time-sliced, inside the prism so its own faces are culled) is live only for the `liveReflections` nearest grown
  monoliths (about 1 ms GPU each), frozen for the next ones up to `reflectionProbes`, and switched off beyond (they
  reflect the sky): HDRP draws at most 32 reflection probes on screen (Maximum Cube Reflection Probes on Screen in
  the HDRP assets).
  Bogota: `World/Bogota` (prefab `Bogota/Bogota.prefab`, the GIS terrain of Bogota_GIS: zoom-18 strip and zoom-15
  surroundings, local x east, y height, z north, metres from San Rafael lake). Film ending (`Act2_Flythrough/Bogota
  Sequence`, `BogotaSequence`): Bogota stands upright across the valley about 9 km after the hand-off, its map facing
  the camera, so it closes the hole of the curled tube. Three `[Animate]` switches, Play mode only (in the Editor
  everything stays visible): `showBogota` (off by default: hidden in the show), `showLandscape` (terrain, clouds,
  monoliths, Valley) and `rideBogota` (the act hands the Spline camera `Bogota/Bogota Path` instead of the flight path,
  without ground; the camera joins it where it is). The context menu **Build Bogota Path** places Bogota and rebuilds
  the path from the flight path and the settings on the component (`filmStartDistance` 8924 m, `handoffDistance` 10700 m, `approachLength`
  9000 m, `climbRadius` 500 m, `bogotaAltitude` 300 m above the ridges near the route): the flight path from
  `filmStartDistance` (its ground height baked in at `handoffAltitude`, anticipated and smoothed), straight on from
  `handoffDistance`, a quarter turn
  up onto Bogota (the knots' up turns from the sky to Bogota's face), then the z18 route to the lake (64.8 km, from
  `Bogota/film_path.json`, written by Bogota_GIS `scripts/film_path.py`: route smoothed, ground = highest within
  300 m). On Bogota the camera needs `splineUp` 1 (its up is then the map's). Bogota closer than ~9 km sticks its
  relief (up to 1.3 km towards the camera) into the tube. Sun and sky over upright Bogota are still to be solved.
  `Bogota/Bogota Volume` (local Volume, priority 20, profile `Bogota/Bogota Volume Profile.asset`): a box over the
  whole map (Bogota local x and z, height -3 to +2 km), blend distance 1.5 km, so the look changes smoothly during
  the approach; it lowers the exposure compensation (-1.5 EV) because the automatic exposure (HDRP default profile:
  the main `Sky and Fog Volume` is at weight 0) brightens the dark upright map. Volumes cannot change a light's
  intensity.
  **Act 2 film Timeline**: `Act2_Timeline` in Soledad_Main (PlayableDirector, `Assets/Acts/Act2_Flythrough/Timeline/Act2_Timeline.playable`,
  120 s at 60 fps) with two Animation tracks: `Show/Act2Controller` (curl, Bogota switches) and
  `Cameras/Spline Camera target` (`useProgress` on, `progress` 0 to 1 along Bogota's path, the whole film: 76 km;
  look, `splineUp`, offset), plus markers for each beat; the director holds its last values when it ends (Wrap Mode Hold), otherwise the
  camera values snap back to their defaults over Bogota. `FilmTimeline`
  (`Assets/Scripts/Show`) starts it in Play mode once the act has handed its path: camera to Spline, ride reset at
  `startDistance` (rides by speed only), then the director plays. The camera rides `Bogota Path` from t = 0
  (`rideBogota` on) by progress, so its position is keyed directly and the other beats stay put when it changes;
  speed = slope of the curve. Keys: curl 2 to 26 s, leaves the flight path 24 s, look right 30 to 36 s, Bogota
  appears 33 s, faster towards Bogota from 37 s, landscape hidden 61.5 s, turn up 63 s, faster again and 300 m
  higher over Bogota (progress 0.68 at 120 s; 1 = the lake). Rebuilding the path changes its length, so the
  progress keys must be checked after Build Bogota Path.
  Kept for later (agreed with Clement): mountain erosion and more varied ridges; per-pixel dune relief to remove the
  stepped slip-face edges; vegetation and rocks scattered on the terrain; better light for the final render (SSGI,
  contact shadows, higher shadow/fog quality, path tracing: the terrain shader would need ray tracing passes and
  the vertex curl is invisible to it); 70s look. Option parked: render the Act 2 film shot in Unreal (Megascans,
  Nanite, Lumen; fisheye via cubemaps) if there is time before the September 2027 deadline.

- **Act 3, Abyss** (`Assets/Acts/Act3_Abyss/`): a pit of Megascans rocks (`Environnement/Rocks`, about 80 x 150 x 80 m
  around the origin, `Zone` = the placeholder walls, off) open at the top around y = 75 to 90. Very dark, lit by:
  - `Abyss Lights`: an Oxipital **Light Group** (Core 1.6.0, `Light Group.prefab` instance in the act): 5 glowing
    orange spheres on a flat circle of 12 m at the centre (circle pattern only, group turned 90 degrees on X),
    20000 lm, range 60 m, volumetric halos, shadows. Lights exist in Play mode only, like every Oxipital dancer group.
  - `Light Shafts`: a second Light Group of 4 narrow spots (8 degrees, 2500 lm, volumetric 3.5, shadows) on a 7 m
    circle at y = 85, the opening: beams through the fog, cut by the rocks. Aimed down by the group (turned 90 degrees
    on X, `dancerRotation` 8 degrees), wavering with a `NoiseDancePattern` (0.6 m) and flickering with the Noise wave.
    Any group can follow another one (`targetMasterBallet`, Oxipital 1.6.0), the ring included.
  - `Abyss Lighting` (`AbyssLighting`): while the act is shown, aims the Sun of Soledad_Main almost straight down
    (78 degrees, `sunElevation` / `sunAzimuth`, cold `sunColor`, `sunIntensity` 2500 lux, `shaftStrength` = its
    volumetric dimmer), all `[Animate]` (`Show/Act3Controller`), and restores the Sun when disabled and around saves.
  - Act 3 Volume: **Fixed exposure EV 8.5** (automatic exposure would lift the dark back up), gradient sky x6 (almost
    no ambient), fog in constant near-black colour with no sky light in it (`globalLightProbeDimmer` 0), volumetric
    fog only in the pit (full below 40 m, gone by 95 m, mean free path 55 m, neutral cool albedo, quality Medium: High
    cost 4 ms more for the same image), bloom 0.45. Measured GPU cost (laptop, dome 2048): volumetric fog ~7 ms,
    volumetric lights ~6.5 ms, the ring's point light shadows ~3 ms, the dome itself ~11 ms.
  `1m Ref` (scale cube at the centre) is turned off: it sat in the middle of the ring.

## Film workflow: perform, then render

The film is rendered offline, slower than real time, so it cannot be driven by Chataigne live. Each act is
performed live with Chataigne (gamepad for the camera, Leap for the hands) against the recorded music at preview
resolution, the values reaching Unity are recorded into a Timeline, then Unity Recorder renders that Timeline at full
resolution (image sequences for Resolve). Camera moves can also be written directly in the Timeline. Keep VFX seeds
fixed so re-renders match. Inputs that are not recorded (Leap, Spout, optical flow) do not reach the film.

## Commits

`.asset`, `.glb` and other binaries go through Git LFS (`.gitattributes`). Commit act scenes together with any new
`.meta` files. Never commit `Library`, `Temp`, `Logs`, `UserSettings` or the generated `.csproj` / `.slnx` files
(already in `.gitignore`).
