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
  and biome functions). The landscape is fixed in the space of `Act2_Flythrough/World`; the bird flies along
  `World/Flight Path` (a Splines `SplineContainer`, editable; `pathOffset`, `loopPath`) at `altitude` metres above the
  (smoothed, anticipated) ground plus the spline's own height. Floating origin: the Oxipital camera rig stays at the
  `Flythrough` object and the act only translates `World` (never rotates it) so the bird sits there; `World` goes
  back to the origin when the act ends and while a scene is saved. `cameraFollowsHeading` adds the path's turns to
  the Orbital and Spaceship rigs (not Sensor). The terrain's pass and valley follow the route centre line
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
  their own names. In Play mode the act pushes every camera's far plane to `farClip` (40 km) and restores it when
  disabled. Act 2 Volume: landscape haze (fog base -250 m, 2 km thick, 9 km mean free path, volumetric; the Physically
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
  (`sunElevation`, `sunAzimuth`, animatable; default 14 degrees, ahead-right) and raises its shadow resolution to
  4096 while shown, restoring both when disabled and around scene saves, so Soledad_Main never keeps them.
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
  Next: Bogota mesh from Blender (Clement).
  Kept for later (agreed with Clement): mountain erosion and more varied ridges; per-pixel dune relief to remove the
  stepped slip-face edges; vegetation and rocks scattered on the terrain; better light for the final render (SSGI,
  contact shadows, higher shadow/fog quality, path tracing: the terrain shader would need ray tracing passes and
  the vertex curl is invisible to it); 70s look. Option parked: render the Act 2 film shot in Unreal (Megascans,
  Nanite, Lumen; fisheye via cubemaps) if there is time before the September 2027 deadline.

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
