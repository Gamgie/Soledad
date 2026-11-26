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

- The main scene (called Soledad_Main in this file and in the code) comes in two versions, so neither carries what
  only the other needs: `Assets/Scenes/Soledad_Main_Film.unity` for the film (the `Film` object and its Timelines,
  Recorder) and `Soledad_Main_Performance.unity` for the live show (to come, without the film setup). The film one
  kept the GUID of the former `Soledad_Main.unity`. One of them is always loaded and is the active scene. It holds
  everything permanent:
  - `Root Scene`: the object OSCQuery exposes (`OSCQuery.rootObject`), so Chataigne addresses start under it, as in
    Oxipital-Engine (the Chataigne module looks for a top-level `ballet`). It contains `Cameras` (Oxipital Camera
    prefab), `Ballet` (Oxipital Core prefab), `Show` (`ShowController`), `Sun`, `Sky and Fog Volume`, `Take Recorder`
    (see Takes, film scene only), and the loaded acts.
  - `OSCQuery`: server `Unity-Oxipital` on port 8999, same filters as Oxipital-Engine plus the `Animator` component
    excluded (the Timeline Animators on `Show` and the camera objects: nothing to drive from Chataigne, and reading
    their properties logged warnings on every tree rebuild).
  - `Dome Creator` (pfc dome-tools): the fulldome rig. `CameraController.isFullDome` switches between the dome
    (main camera off, `Dome Creator` renders `Assets/Textures/Dome Content Texture` and the DomeMaster
    `Dome Output Texture`) and 16:9 (`Dome Creator` disabled). `DomeCameraRigFollow` on its rig follows
    `Cameras/Main Camera` through `Dome Target`. Wired like Oxipital-Engine's `Oxipital Dome` scene.
- Each act is its own scene with **one root object named like the scene, carrying an `Act` component**. Only
  environment content goes there (meshes, terrain, point clouds, lights, local Volumes, act-specific scripts).
- `ShowController` (`Assets/Scripts/Show`): OSCQuery only sees `Root Scene`, so an act scene is loaded additively,
  its root is moved under `Root Scene` in the main scene, and the emptied act scene is unloaded at once. Setting
  `act` (1 to 5, 0 = none; also `NextAct()` / `PreviousAct()`) crossfades `Act.weight` over `transitionDuration`,
  keeps only the shown act and the next one (preloaded, inactive), and destroys the others. After a crossfade the
  previous act is deactivated (its meshes and particles do not fade with the weight). Loaded act roots are
  added to OSCQuery's excluded objects (`hideActsFromOSCQuery`, on by default; OSCQuery's object filter must stay in
  Exclude mode), so the Chataigne tree is the same whichever act is loaded and a resync rebuilds it identically.
  Act parameters are reached through the generated controllers on `Show` (below).
- **Act parameters for Chataigne and Timeline**: act objects come and go, so their addresses are not stable. Mark
  the act script members to drive with `[Animate]` (`Assets/Scripts/Show/AnimateAttribute.cs`; public fields of
  type float, int, bool, enum, Color, Vector3, and parameterless public methods; `[Animate("fixedName")]` keeps the
  address when the field is renamed). After each compile, `ActControllerGenerator` (`Assets/Scripts/Show/Editor`,
  also menu `Soledad > Generate Act Controllers`) writes `Assets/Scripts/Show/Generated/ActNController.cs` for each
  `Assets/Acts/ActN_Name/` folder: flat fields `Type_field` (same default, Range or PowerRange, Tooltip) and
  forwarding methods. `[PowerRange(min, max, power)]` (`Assets/Scripts/Show`) is a slider with a curved response
  (value = min + range x t^power) for values whose useful part is tiny, e.g. Act 1 `randomDensity` (power 4); the
  field still stores the real value.
  Never edit them by hand. The generator adds missing controllers to `Show` (Soledad_Main must be open, then save
  it) and copies new fields' values from the act scene when it is open (`Soledad > Pull Act Controller Values From
  Open Acts` copies all of them). In Play mode the controller (`ActController`) is the source of truth: it binds
  to its act root under `Root Scene` once loaded, pushes its values every frame (also while the act is preloaded),
  and drops the references when the act is destroyed. So drive and record (Timeline) `Show/ActNController`, not the
  act objects: values set on the act itself are overwritten. In edit mode (`[ExecuteAlways]`, to build a Timeline
  without Play) it binds to the act scene opened next to Soledad_Main and, on every editor update, pushes only the
  values that changed on the controller (Timeline preview, inspector; generated `PushChanged`, last values in
  `_last_*`): a value set on the act itself stays until the controller's own value changes. Timeline preview restores
  the controller when it stops, which pushes the restored values to the act too. One component of each type per act is driven. If an
  `[Animate]` field is renamed or removed, the generated code stops compiling, the generator writes a stub and the
  next compile restores it; the controller value of a renamed field is lost unless it had a fixed name.
- `Act.weight` scales every Volume under the act, so two acts blend during a transition. Act-specific scripts read it
  to fade their own content.
- Forces: the Ballet feeds its force groups only to the Orb Groups of its own `Orb Groups` manager, so `Act` forwards
  the same buffers (by name, `Force Group N`) to every Orb Group under the act in Play mode (`linkForces`, on): act
  particles answer the Chataigne force groups, each scaled by its own `forceWeight`.
- The Sun of Soledad_Main is lit by the acts through `SunControl` (`Assets/Scripts/Show`): an act script implements
  `ISunDriver` (`DrivesSun` = its `overrideSun`, `DriveSun(sun, data)` sets what it wants), calls `Register` /
  `Unregister` in OnEnable / OnDisable and `Drive(this)` every frame. Act scripts never keep the Sun's own values:
  `SunControl` takes them once, when the first driver takes the Sun, on a hidden never-saved `SunOriginal` on the
  Sun (kept through script reloads), gives them back while any scene is saved and when the last driver lets go.
  One driver sets the Sun at a time: the one whose act has the highest `weight` (the incoming act halfway through a
  crossfade), the last enabled on a tie (in the Editor, the act scene opened last); when it changes, the Sun's own
  values come back first. Before this, two acts enabled at once took each other's light for the original and saved
  it into Soledad_Main. Entering Play mode it gives the Sun its own values first (Unity keeps a copy of the open
  scenes for the way back, without the hidden `SunOriginal`: the Sun came back with the act's light, which the act
  then took for the original and the next save wrote into Soledad_Main). Drivers: Act 2 `FlythroughTerrain`, Act 3 `AbyssLighting`, Act 4 `HillLighting`, Act 5
  `SpaceLighting`.
- Working on an act in the Editor: open `Soledad_Main`, then the act scene additively. On Play, `ShowController`
  takes over the open act scene and shows it. Keep `Soledad_Main` as the active scene.
- Object names under `Root Scene` and public fields are Chataigne addresses: do not rename them casually.
- `Player Settings > Run In Background` is on (as in Oxipital-Engine): the show keeps running while Chataigne or
  TouchDesigner has the focus.
- Live show frame rate: `ShowController.targetFrameRate` (50, 0 = no cap, not exposed to OSCQuery) caps the frame
  rate in Play mode and turns vSync off meanwhile (all quality levels have vSync on, and Unity ignores the cap with
  vSync on), restored when Show is disabled. Not applied while Unity Recorder renders (`Time.captureDeltaTime` set):
  the film runs offline at its own rate. vSync off on a 60 Hz output can tear; a 50 Hz output avoids it.

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
  `PhoneScreen` (the phone): `Zdepth` and `alpha` (`[Animate]`, also in edit mode; alpha through a property block);
  its VideoPlayer (`Videos/Scrolling.MOV` into `_EmissiveColorMap`, emission exposure weight 0 so it shows under the
  fixed exposure) restarts when the phone is enabled in Play mode and plays normally live. While the Recorder renders
  (`Time.captureDeltaTime` set) it is paused and sought every frame to the frame of the game time elapsed since the
  phone was enabled: none of the VideoPlayer's own clocks (DSP, Game Time, External Time) followed the Recorder's
  stepped game time in tests, they kept running in real time (a render played the video twice too fast), and
  `StepForward` did nothing on this clip. The seek lands about a frame later.

- **Act 2, `FlythroughTerrain`** (`Assets/Acts/Act2_Flythrough/`, on `Act2_Flythrough/Flythrough`): a bird's
  flight over an endless landscape laid out along a route (terrain +Z): desert, mountains with a winding pass, then
  the valley (fields between two ridges); shape in `TerrainShape` (`TerrainField.cs`, pure Burst-compatible height
  and biome functions). The landscape is fixed in the space of `Act2_Flythrough/World` (at the origin, no floating
  origin: HDRP renders camera-relative and the route is about 13 km) and the Oxipital camera really flies through it.
  Film: the **Spline camera** (Oxipital Camera 1.5.0, `Cameras/Spline Camera target`, `SplineMovement`) rides
  `World/Flight Path` (a Splines `SplineContainer`), which the act hands to it in Play mode **without ground**: it is
  the one spline of the whole flight, valley then Bogota, its heights baked in (see Bogota below), so what the scene
  shows is exactly where the camera flies (no second spline, no switch). Speed, offset, look (gamepad on `yaw1D` /
  `pitch1D` / `roll1D` in Spline And Look or Free), bank and seeded drift are set on that object (Soledad_Main:
  60 m/s, no loop), not on the act (its `altitude` and ground settings are unused: no ground is handed). Show: the
  Spaceship camera flies freely. `ResetFlight()` (also on the first show of the act) restarts the ride and puts the
  Spaceship at the start of the path.
  **Editor preview** (`FlythroughTerrain.previewInEditor`, on): outside Play mode, while the Spline camera's
  `useProgress` is on (a Timeline preview keys it), the act puts `Spline Camera target` at its `progress` on the flight
  path (offset, aim ahead, `splineUp`, `followPitch` and the Spline And Look angles; no drift, bank, smoothing nor
  spline roll) and makes `CM Spline Camera` Cinemachine's solo camera, so scrubbing the Act 2 Timeline flies the Game
  view without Play (`SplineMovement` itself only runs in Play mode, and only gets its spline there). While previewing
  it also sets that camera's lens far plane to `farClip` (40 km; the Oxipital cameras' 1 km, which the brain gives
  the main camera, cut the landscape off in the Game view). The target's own pose, the lens's far plane and the solo
  come back when the preview stops.
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
  values varied by `variation`, random yaw. **Spread only on demand** (Gamgie, 2026-11-26: positions must never change
  on Play): the **Spread Monoliths** button of its Inspector (Edit mode, asks before replacing) places them, as prefab
  instances saved in the act scene under `World/Monoliths/Monoliths`; nothing else ever places them again (not on
  load, not on Play, not on a Spread change). Move, duplicate or delete them by hand like any object (the field picks
  up added or removed children). The Shape and Growth settings still apply at once to the monoliths there, by index
  and seed, without moving them. Spread again with the same settings and seed gives the same places (35 monoliths,
  seed 1, spread 2026-11-26 = the layout generated before). A `Monolith` is a standing triangular prism
  with rounded edges (mesh generated on the child `Slab`, Default layer: the Dome Creator cameras only render
  Default, TransparentFX, Ignore Raycast and Water, so never put act content on another layer), blurred mirror finish (`Monolith.mat`,
  HDRP Lit metallic), growing (Scale or Rise) by depth ahead of the camera along the route (Z), from `growStart` to `growEnd`, staying grown (`keepGrown`). Its own realtime
  reflection probe (128, time-sliced, inside the prism so its own faces are culled) is live only for the `liveReflections` nearest grown
  monoliths (about 1 ms GPU each), frozen for the next ones up to `reflectionProbes`, and switched off beyond (they
  reflect the sky): HDRP draws at most 32 reflection probes on screen (Maximum Cube Reflection Probes on Screen in
  the HDRP assets).
  Bogota: `World/Bogota` (prefab `Bogota/Bogota.prefab`, the GIS terrain of Bogota_GIS: zoom-18 strip and zoom-15
  surroundings, local x east, y height, z north, metres from San Rafael lake). Film ending (`Act2_Flythrough/Bogota
  Sequence`, `BogotaSequence`): Bogota stands upright across the route where the valley begins, its map facing
  the camera, so it closes the hole of the curled tube. Three `[Animate]` switches, Play mode only (in the Editor
  everything stays visible, and the sky stays the world's): `showBogota` (off by default: hidden in the show),
  `showLandscape` (terrain, clouds, monoliths, Valley) and `bogotaSky` (the sky over Bogota, below). **One flight path** (`World/Flight Path`, 41.3 km): its first `drawnKnots` knots (32, every
  200 m, 6.1 km: desert, then the mountain pass) are the **drawn part**, edited by hand, ending at the hand-off; the
  knots after them are the **generated tail**: straight on for `approachLength` (750 m), a quarter turn up onto
  Bogota (`climbRadius` 500 m; the knots' up turns from the sky to Bogota's face), then the z18 route to the lake
  (`bogotaAltitude` 300 m above the ridges near the route; 64.8 km, from `Bogota/film_path.json`, written by
  Bogota_GIS `scripts/film_path.py`: route smoothed, ground = highest within 300 m), its first `routeTrim` (18.2 km)
  skipped. The route's first point is the top of the turn, so the approach line meets the map just before it: with
  no trim that was near the western end of the z18 strip (high res ended ~1 km below eye level, its first chunks
  partial, the blurry z15 around); trimmed, the approach faces the middle of the corridor (the strip is never wider
  than ~5 km, the z15 surroundings show beyond). **The path is now partly hand-made: do not Build it** unless
  Gamgie's edits may be lost. On 2026-11-26 Gamgie reshaped the end by hand (the last four knots: a shortcut over the
  last ~10 km and the dive, the route 64.6 to 51.6 km), then the start over Bogota was moved 10.2 km on (trim 8 to
  18.2 km, Usme, to fly slower over Bogota) **without a Build**: the 34 knots after the top of the turn were removed,
  and Bogota and every knot after them moved rigidly (turned 7.8 degrees about Bogota's up, so the new first route
  direction points up) so the new start sits where the old one was; the sky's planet centre recomputed like
  `PlaceSky`. A Build would regenerate the whole tail and lose the hand-made end. **Into the lake** (`lakeDive`, the
  ending Gamgie first shaped by hand on 2026-11-26 and reproduced by the build to 0.5 m, since reshaped by hand, see
  above): the route's last knot, 300 m above San Rafael reservoir (a dam lake; its surface
  = the route's last ground height, 43 m in Bogota space), tips down (`diveEntryRotation` (17.8, 1.5, -1.3)), then one
  knot `diveForward` 107 m on, `diveSide` 43 m right, `diveDepth` 161 m under the surface, nose down
  (`diveRotation` (74.3, -176.1, -98.5)); both rotations are Euler angles in the route's frame (route direction, map
  up), the dive knot's tangent as short as the last route knot's (37 m). With `splineUp` 1 and the Timeline's
  `offset.y` 300 along the spline's up, the camera itself ends ~280 m south of the spline's end, 173 m under the
  surface, and crosses it at about progress 0.997. Context menu of
  `BogotaSequence`. Bogota's face stands about 1.3 km after the hand-off (approach + climb radius + 300 m), at
  z ~ 7.2 km, where the valley's green starts (the valley biome is 24 % there, full by 8 km): the valley's fields
  are never seen (Gamgie, 2026-11-25: their texture is not good enough). Along the route the mountain pass stays low
  (ground 0 to 90 m), so the straight level approach clears it by 130 to 200 m. **Build Flight Path** places Bogota from the last drawn knot (position and level direction) and
  rewrites the tail, keeping the drawn knots where they are; **Bake Heights And Build Flight Path** first resamples
  the drawn part every `bakeSpacing` (200 m) at `flightAltitude` (140 m) above the relief, anticipated over
  `groundLookAhead` (600 m) and smoothed, then builds. Both recompute every knot's tangents (move knots, not
  tangents), so after moving drawn knots run Build; after changing the terrain shape, Bake. The old separate
  `Bogota Path` and the `rideBogota` switch are gone (the camera stayed on one spline or the other). On Bogota the
  camera needs `splineUp` 1 (its up is then the map's). Bogota closer than ~9 km sticks its
  relief (up to 1.3 km towards the camera) into the tube. **Sun over upright Bogota**: the sunset Sun (5 degrees,
  azimuth -81) is only ~4 degrees above the map's own horizon, so its relief threw shadows over 10 km long across the
  city, and the shadows stop at the Volume's 8 km shadow distance (a lit band beyond). Once the landscape is hidden
  (61.5 s) the Timeline turns it to 10 degrees, azimuth -35 by 70 s (`FlythroughTerrain_sunElevation` /
  `sunAzimuth` on `Show/Act2Controller`): ~48 degrees above the map, from map bearing ~150 (south-south-east). Map
  elevation = asin(-dot(sun forward, Bogota up)); azimuth 0 to 10 would be noon on the map (~80 degrees, flat). Its
  colour goes from 3500 K to 6000 K over the same keys (`FlythroughTerrain_sunTemperature`): the Physically Based
  Sky takes its blue from the light's colour, and under a 3500 K Sun the sky over Bogota stayed a dull olive.
  **Sky over upright Bogota**: `Bogota/Bogota Sky Volume` (global Volume, priority 21, **inactive in the scene**,
  profile `Bogota/Bogota Sky Volume Profile.asset`): Visual Environment with the planet's `centerMode` Manual and
  `planetCenter` behind the map (along -Bogota up, its surface at the map's lowest point, ~2.5 km under the camera),
  so the Physically Based Sky (Act 2's settings) has its zenith, horizon glow and sun height over the map. HDRP blends
  a planet centre linearly between volumes (halfway the camera would be inside the planet), so it cannot fade: it is a
  cut, switched on by `bogotaSky` from the Timeline at **62 s**, while Bogota's face fills the whole frame (from the
  landscape cut at 61.5 s to the turn up at 63 s no sky is in view), so the cut is hidden. `BogotaSequence.Build`
  writes the planet centre (field `skyVolume`): rebuild after moving Bogota. Exposure stays **automatic** all along
  (`Bogota Volume` compensation -0.62, Gamgie's value): measured 46/255 just before the cut, 48 just after, then 60,
  51, 61 at 64.5, 68, 75 s, nothing clipped. Earlier attempts and why they failed: a sky box entered halfway up the
  turn showed the dark world sky until then, the automatic exposure metered it and blew out the map (96/255), and a
  fixed exposure in the sky volume then jumped to 39/255 at the cut; fixed exposure cannot go in `Bogota Volume`
  either (with its 1.5 km blend HDRP switches the mode to Fixed at once and blends `fixedExposure` up from 0). Act 2's
  own Exposure override (fixed EV 12.2) is in its profile but inactive.
  `Bogota/Bogota Volume` (local Volume, priority 20, profile `Bogota/Bogota Volume Profile.asset`): a box over the
  whole map (Bogota local x and z, height -3 to +2 km), blend distance 1.5 km, so the look changes smoothly during
  the approach; it lowers the exposure compensation (-0.62 EV) because the automatic exposure (HDRP default profile:
  the main `Sky and Fog Volume` is at weight 0) brightens the dark upright map. Volumes cannot change a light's
  intensity.
  **Act 2 film Timeline**: `Film/Act2_Timeline` in Soledad_Main (see Film workflow; `Assets/Acts/Act2_Flythrough/Timeline/Act2_Timeline.playable`,
  120 s at 60 fps): `Show/Act2Controller` (curl, Bogota switches) in the Show group and
  `Cameras/Spline Camera target` (`useProgress` on, `progress` 0 to 1 along the whole flight path, 41.3 km;
  look, `splineUp`, offset) in each camera group (the Dome copy starts as the 16:9 one), plus markers for each beat; the director holds its last values when it ends (Wrap Mode Hold), otherwise the
  camera values snap back to their defaults over Bogota. Its `FilmTimeline` (`rideSpline` on)
  starts it in Play mode once the act has handed its path: camera to Spline, ride reset at
  `startDistance` (rides by speed only), then the director plays. The camera rides the flight path by progress, so
  its position is keyed directly and the other beats stay put when it changes; speed = slope of the curve. The film
  starts at the path's start (progress 0, the desert) and flies the desert and the mountain pass. Keys: curl 2 to
  26 s, look right 30 to 36 s, Bogota appears 33 s, faster towards Bogota from 37 s (1.77 km), landscape
  hidden 61.5 s, Sun turned and whitened for Bogota 61.5 to 70 s, sky over Bogota 62 s, turn up 63 s (6.95 km, the
  turn's start; top at 7.68 km, 65.4 s), faster again over Bogota (80 s at 14.2 km), then the dive into the lake
  (16:9: Gamgie's keys from 103 s, offset.y 300 to 150 then 0, progress 0.990 at 109.1 s, 1 at 122.95 s; the Dome
  clip still ends at 120 s with the old ending). Building the flight path changes its length, so the progress keys must be checked after a Build or
  a Bake. When Bogota moved to the valley's start they were remapped by hand: before the turn, distances scaled to
  the shorter way (x 0.64, the film used to start 1.8 km before a 9 km approach in the valley), after it, the same
  distance past the turn (also when `routeTrim` went to 8 km: the flight over Bogota starts 8 km further along the
  route and keeps its distances). On 2026-11-26 (start moved to 18.2 km): keys up to the top of the turn back at
  their designed distances (the hand-made end had shortened the path, so with unchanged progress values the turn
  came ~7 s late), the flight over Bogota from the top to the first dive key (109.1 s) 0.765 times as long, so 24 %
  slower at the same times, and the dive keys on the same ground as before.
  Kept for later (agreed with Clement): mountain erosion and more varied ridges; per-pixel dune relief to remove the
  stepped slip-face edges; vegetation and rocks scattered on the terrain; better light for the final render (SSGI,
  contact shadows, higher shadow/fog quality, path tracing: the terrain shader would need ray tracing passes and
  the vertex curl is invisible to it); 70s look. Option parked: render the Act 2 film shot in Unreal (Megascans,
  Nanite, Lumen; fisheye via cubemaps) if there is time before the September 2027 deadline.

- **Act 3, Abyss** (`Assets/Acts/Act3_Abyss/`): a pit of Megascans rocks (`Environnement/Rocks`, about 80 x 150 x 80 m
  around the origin, `Zone` = the placeholder walls, off) open at the top around y = 75 to 90. Very dark, lit by:
  - `Abyss Lights`: an Oxipital **Light Group** (Core 1.6.0, `Light Group.prefab` instance in the act): 5 glowing
    orange spheres on a flat circle of 12 m at the centre (circle pattern only, group turned 90 degrees on X),
    20000 lm, range 60 m, volumetric halos, no shadows: 5 shadowed point lights = 30 shadow faces, each redrawing every
    lit particle (about 16 ms with 264 k grains). Lights exist in Play mode only, like every Oxipital dancer group.
    Lit particles (orb render types LitCube / LitSphere / LitMesh) cost per vertex and per pass: use the OrbOctahedron
    grain (6 vertices, the Orb Group default since Oxipital 1.6.1), not the icosphere (42).
  - `Rising Bulb` (`Prefabs/Rising Bulb.prefab`, `RisingBulb`): a light bulb rising from the deep, the same look as the
    ring (HDRP point light "Light" + Oxipital's Light Sphere on "Mesh"). From its start it rises along Y at
    `riseSpeed` with a slow seeded brownian drift on X and Z (`drift`, `driftFrequency`), fades in (`fadeIn`), and at
    the end of `life` fades out (`fadeOut`): light intensity and bulb size to 0. A spawner calls `Launch(start, seed)`
    and gets it back through `finished`; alone it restarts from its start (`loop`) or is destroyed.
  - `Bulb Spawner` (`BulbSpawner`): launches Rising Bulbs at `spawnRate` per second (0.5) from its children, the spawn
    points (`Spawn Point 1` to `9`, instances of `Prefabs/Spawn Point.prefab`, placed by hand on the cave floor: move,
    add (duplicate one) or remove them freely, also while playing), offset by up to `spawnRadius`. Each point carries
    a `Marker`: a 2 m orange sphere (`Materials/Spawn Point Marker.mat`, HDRP Unlit, emission with exposure weight 0 so
    it shows whatever the exposure) to place them in edit mode; the spawner deactivates every child of its spawn
    points in Play mode (also for points added while playing), so they never show in the show or the film. Each bulb draws its
    `life` (25 to 45 s), `riseSpeed` (1.5 to 3.5 m/s) and `drift` (0.5 to 3 m) between the min and max fields, from a
    seeded generator (`seed`), so renders repeat. `maxBulbs` caps the bulbs in flight, live: when reached, launches
    wait; lowered, the bulbs in flight end their life. Pool created in Play mode under `Bulbs (generated)` (never
    saved), `maxBulbs` bulbs at the start, grown (one allocation each) if `maxBulbs` is raised beyond it. The look of all bulbs is set on the
    spawner (Look: `color`, `intensity`, `range`, `size`, `meshEmission`, `volumetric`, `volumetricIntensity`,
    `shadows`), pushed every frame to the bulbs in flight (the spawner runs before them, execution order -10), so the
    prefab's own look values are ignored under a spawner. Rate and min / max are `[Animate]`
    (`Show/Act3Controller`, `BulbSpawner_*`: in Play mode edit them there, not on the spawner); the look is set on
    the spawner itself (not animated). Each live bulb is a volumetric point light, capped by `maxBulbs` (8 in the
    scene); without shadows, 8 bulbs measured no visible GPU cost (`shadows` costs 6 shadow faces per bulb).
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

- **Act 4, See Through** (`Assets/Acts/Act4_SeeThrough/`): a lone tree on top of a green hill; what looks alone is
  connected (roots, organisms around it). Camera: an insect flying around and through the tree, also under the
  ground. The tree is Gamgie's photogrammetry scan from Colombia (`Tree_AF.glb`, one mesh of 20.6 M vertices,
  about 5.7 m tall and 3.6 m wide at the roots, trunk cut where it splits into 5 or 6 stems, no crown), loaded as
  lit particles by Oxipital (`StreamingAssets/emitters`). Planned: a crown grafted on the cut stems (mesh, not
  particles), particles falling from the roots below the ground (emitted from a top-view map of the roots, pulled
  down with a linear force plus Perlin into strings; ref Marshmallow Laser Feast, Of the Oak), the act's own sun.
  **Grass: to revisit later (Gamgie, 2026-11-17: "enough for now")**: the shell grass is a good velvet lawn from
  40 cm up but stays a photo of grass at insect height (no blades to fly through). Next steps when coming back: GPU
  blades near the camera only (Ghost of Tsushima style: compute-generated blades in clumps, bent, widened with
  distance) over the current shell lawn; stronger anti-tiling; mounds less bubble-like (longer, gentler waves).
  Remind Gamgie of this when working on Act 4 again.
  - `Tree`, `Crown` and `Tree Shadow Proxy` all sit at (0, **1.63**, 0): the scan's own ground (the root plate with its scanned soil) is
    1.64 to 1.83 m below the scan's origin (rim measured per direction from its points, flat within 0.5 degree), so
    this lift lays the roots on the hilltop, the rim just at grass level. Move them together.
  - **One point cloud, scan + grafted stems**: `Tree` emits from `Tree_AF_Grafted` (the scan with the first ~2.5 m of
    the 7 grafted stems joined in, 7.94 M triangles, 1.4 GB, 2 M particles = the OrbVFX capacity), so the force groups
    move the stems with the scan. Oxipital picks a random triangle then a random point in it (Orb VFX
    `ParticleSpawnPosition`, placement Surface): density follows triangle density, so `Blender/Act4_Tree/make_stems.py`
    tessellates the stem tubes at the scan's own triangle density (56 900 tris/m2 measured on the scan's stems below
    the cut: 6 mm quads, 1.07 M triangles for 18.6 m2). Bark: each tube vertex takes the scan's UV at the same angle a
    little below the cut on the same stem (repeating every 0.9 m), with the scan's own atlas. The crown's wood starts
    where the particle stems end (`STEM_LENGTH` 2.6 m, `STEM_MIN_RADIUS` in `make_crown.py`).
  - **Tree shadows**: the grains cast no shadow (`TreeEmitter.grainShadows` off sets the VFXRenderers' shadow casting);
    `Tree Shadow Proxy` (`Tree/TreeShadowProxy.glb`: the scan decimated to 80 k faces and shrunk 2 cm inward, plus the
    stems; Shadows Only renderer; `make_shadow_proxy.py`) casts the tree's shadow instead. Same view, 2.2 M grains:
    16.6 ms GPU without grain shadows, 38.6 ms with (each grain was drawn again in every shadow cascade); the proxy
    itself costs nothing measurable (18.9 ms with or without it, 2 M grains). It shadows the ground and the grains (the
    crown shades the trunk). No particle removed. Bigger grains (`particleSize`) remain an option if holes show.
  - **Roots** (the invisible connections: particles leaving the roots and falling, seen from below the hill, whose faces
    are culled from inside), all made by `Blender/Act4_Tree/make_root_map.py` (settings at the top of each section):
    the scanned roots from the scan's points (local relief: top of each 1 cm cell minus the mean top within 31 cm,
    above 2 cm = root; the trunk foot solid), then the outermost scanned root of each of 56 directions grown on outward
    by 2D space colonization to 11 m (pipe-model widths, never under 1.2 cm; `EXTENT`, `ATTRACTORS`, `SECTORS`,
    `TIP_WIDTH`, `SEED`). Outputs: `Roots/RootMap.png` (top view, 4096 px for 24 m, white roots on black, centred on the
    tree's axis) and the emitter `StreamingAssets/emitters/RootCloud.glb`, the roots in 3D close under the real ones:
    2 cm quads on the underside of the scanned soil plate (lowest point of each cell) under every scanned root and the
    trunk foot, then the grown roots as tubes of their width diving into the hill (`DIVE` 0.11 m per m, down to 2.4 m
    under the top), ~2 cm quads everywhere so the particles spread evenly (288 k triangles, no texture: white
    particles). Scan frame, so `Roots` (Orb Group, custom mesh `RootCloud`, Unlit Additive, life 8 s, drag 1, force
    weight 1, particle size 0.05) sits where `Tree` sits. The flat `RootMap.glb` (cells of the map 40 cm under the hill, coloured
    by the map) is written to `Blender/Act4_Tree` only, unused. The fall comes from the Ballet's force groups (`Act`
    forwards them to the act's Orb Groups): tested with one force group, linear down (dancer rotation X 90, force factor
    outside 1) at 0.01: the root lines stay readable and each drips a curtain of particles (0.3 rains them all down at
    once, the roots vanish); Perlin can pull the curtains into strings. `Tree` has force weight 0.
  - **Soil around the roots** (`Hill Grass.mat`, Soil Patch section of `Soledad/HillGrass`): an irregular disc
    (`Radius` 2.3 m, `Edge Width` 0.9, `Edge Irregularity` 0.6, two noise octaves) of Meadow's dry meadow soil mixed
    with its forest leaf litter by patches (`Materials/Soil`, extracted from the Meadow package in the Asset Store
    cache), tinted towards the scan's soil; the grass shortens across the edge (`Grass Fades Into Soil`) and its lower
    blades get dusty. `Landscape Grass.mat` has the patch off (radius 0).
  - `Tree` (on `Act4_SeeThrough/Tree`): an Oxipital **Orb Group** (instance of Core's
    `Orb Group.prefab`, not `Orb Group Lit.prefab`: that one has no VFX asset and crashes Unity natively in
    `updateInfiniteLife` on the first Play frame) with custom mesh `Tree_AF_Grafted` (above), infinite life (1 999 000 particles of
    the OrbVFX's 2 M capacity), one dancer, pattern size 0, no forces, LitMesh octahedron grains size 0.3, texture
    opacity 1 (the scan's bark colours). `Tree_AF_Grafted.glb` is in `Assets/StreamingAssets/emitters` but **not in
    git** (`.gitignore`): on a new machine rebuild it with `make_crown.py` then `make_stems.py` (they read the scan from
    `E:/Projets/Oxipital/Asset/emitters/Tree_AF.glb`). Oxipital loads every .glb of that folder at Play, so keep only
    the emitters in use there. Loading takes a few seconds (2.8 GB in memory); `TreeEmitter` (same object) holds the group's intensity at 0
    until the meshes are loaded (infinite particles spawned earlier would stay on the default shape), then follows
    `density` (`[Animate]`, `Show/Act4Controller`).
  - `Crown` (sibling of `Tree`, origin, `Tree/Crown.glb` imported by glTFast, the same conversion as the scan loaded
    by Oxipital, so both line up): a crown modelled in Blender to look like Realistic Tree Pack 3 for HDRP (Gamgie's
    reference: dense rounded broadleaf crowns of glossy leaves), grown on the 7 cut stems of the scan (cut at about
    3.78 m; their tips, axes and radii in `Blender/Act4_Tree/stem_tips.json`, found from the scan's points), top at
    about 11 m, 11 m wide. Wood: space colonization from the stem tips towards attractors in 11 foliage clumps
    (lobes, one above each stem first, hollow cores), pipe-model radii scaled so each branch starts at its stem's
    radius, thin outer branches sagging (`DROOP`), continuous tubes (24 k faces: each chain follows the thickest
    child, one ring per node shared by both segments, parallel-transported frames, side branches start sunk inside
    their parent; separate frustums per segment opened gaps on the outside of every bend), bark v continuous along
    each branch. Foliage: 12 700 twig cards (`CARDS_PER_TIP`, scattered in clumps by
    `CARD_SCATTER`), front face out of the crown, normals bent outward (`NORMAL_OUTWARD`; the leaf material keeps
    the same normal on both faces) so the canopy shades as one soft volume, cut from the Meadow pack's poplar atlas (`T_Poplar_leaves_BC`, Gamgie's purchase; the full
    package is in the Asset Store cache, the trees were trimmed from the project). Materials: `Crown Leaves`
    (NatureManufacture foliage shader from `M_grass_meadow_02`, poplar maps, wind off for now), `Crown Bark` (HDRP
    Lit, poplar bark tinted grey like the scan). Rebuild: `Blender/Act4_Tree/make_crown.py` (run on
    `tree_scan_proxy.blend`, a 200 k-face proxy of the scan made by importing `Tree_AF.glb` and decimating), settings
    at its top (crown size, attractors, card count and size, seed), then copy `Crown.glb` into `Tree/`;
    `render_preview.py` renders Blender previews. GPU (laptop, 1200 x 677 game view, camera 12 m from the tree):
    whole frame 43 ms; the crown about 4 ms; **the scan's 1.9 M lit particles about 28 ms** (to reduce before the
    dome: fewer particles, no shadow casting for the grains, or only near the camera). The .blend, .npy, textures and glb in `Blender/Act4_Tree` are not in
    git (regenerated by the scripts).
  - `Hill` (on `Act4_SeeThrough/Hill`): a perfect sphere (`radius` 50 m, 100 m wide) with its top at the object's
    origin (the tree's foot), drawn down to `capAngle` (100 degrees); mesh built on the child `Hill Mesh`, never
    saved, UVs = surface coordinates in metres (seamless), outward faces only (the ground vanishes from below), casts
    shadows on the landscape. Material `Materials/Hill Grass.mat`.
  - `Landscape` (`HillLandscape`): the world around the hill, so the horizon is never a line (refs: rolling grassy
    hills): a plain (`plainHeight` -16 m, gentle undulation) covered with ~120 rounded, elongated mounds (bigger with
    the distance, merged by a smooth maximum that starts under the plain so a mound adds no step at its edge), rising
    to rounded mountains from 2.2 km (`mountainHeight` 650 m), out to 9 km. **Seen from the hilltop only sky shows
    behind the hill** (the tree stands against the sky): the whole landscape falls away from the hill
    (`fallStart` 40 m, rounded over `fallSoftness` 60 m, then `fallSlope` 0.5 m per m, plain at -20 m, mounds from
    150 m and flatter), so from up to ~1.5 m above the top everything stays under the hill's own horizon (from 5 m up
    the mountains start to show); it reappears as soon as the camera leaves the top. The hill emerges with a fillet; the
    first ring lies on the sphere, under the hill's own mesh; where the smooth maximum has become the sphere itself the
    landscape is sunk 30 cm under it (otherwise both surfaces coincide and z-fight in rings). Polar mesh (dense at the hill's foot) in two parts, never
    saved: `Landscape Mesh` up to `shadowRadius` (2 km) casts shadows, `Landscape Far Mesh` does not (a caster
    kilometres long gets clamped in the sun's shadow map and blackened the hill). Noise: its own value noise, not
    `Mathf.PerlinNoise` (it jumps by metres along thin strips at these coordinates: black steps). Material
    `Materials/Landscape Grass.mat` (same shader, `Ground Only`).
  - **Grass: the scanned Megascans "Uncut Grass" set** (`Materials/uncut_grass_oilos0_2k`, 1 x 1 m, normal map in the
    OpenGL convention, checked against its height map) with real relief: shader `Soledad/HillGrass` (`Shaders/`,
    hand-written on HDRP's Lit passes like the Act 2 terrain). The ground is the scan (albedo pulled towards green
    by `Recolour`, the straw blades of the scan otherwise read yellow; roughness, AO, normal); anti-tiling: a second
    sample rotated 40 degrees and 1.37 x larger takes over by 4 m patches, height-blended; 18 m light / dark patches.
    `HillGrass` (on `Hill`) draws the hill mesh again for each shell (`shells` 48, down to `minShells` 16 from 4 m up,
    top shell first; none closer than `minCameraHeight` 0.2 m, where the shells show as slices and the scan alone is
    better, nor beyond `drawDistance`), pushed out along the normal by `_ShellHeight` x `Height` (3 cm), each keeping
    the scan where its height map (`Height Map at Ground` / `at Tips`) reaches it: the scanned blades stand up with
    parallax, like the Fab preview (which renders the scan with displacement; a plain HDRP Lit with the same maps
    stays flat). Alpha-tested: depth prepass, then GBuffer with an equal depth test and no depth write; the shells do
    not cast shadows. Shells fade with distance (10 to 30 m) and when a blade is smaller than a pixel (footprint from
    distance and angle, not screen derivatives: those are constant per triangle and show the facets) by flattening the
    relief (each pixel keeps the shells under height x fade), not by dropping whole shells; depth shading comes from
    the blade's own height (height map), so the lawn keeps one tone where the shells fade (a darkening tied to the
    shell index made a dark cap around the camera). Comb, wind,
    anti-tiling and patch noise per vertex (uv1, uv2). GPU cost measured with the earlier procedural-blade version:
    about +2.3 ms for 40 shells at 1200 x 677, camera 25 cm above the grass.
  - `HillVegetation` (same object, all layers off since the shell grass, kept to bring back flowers or tufts):
    layers (a prefab, LOD 0 and LOD 1 of its LODGroup, or a mesh and materials)
    scattered on the hill once, seeded, along the sphere normal, with patches and a bare `clearRadius` around the
    top, drawn with `Graphics.RenderMeshInstanced` from `RenderPipelineManager.beginCameraRendering` (every camera:
    dome, Scene view in edit mode). Culling per 4 m cell: distance (`maxDistance`, density down to `farDensity`),
    behind the hill's horizon, and nothing while the camera is below the surface. Layers (Meadow prefabs, ~280 k
    instances, Act 4 at 135 fps alone): short meadow grass detailed (3D blades, shadows, up to 18 m) and crossed
    cards (up to 110 m, density down to 10 %), low tufts, a few tall clumps, clover and daisies, sparse chamomile,
    cornflower and poppy in patches. Previous ground `Materials/Hill Ground.mat`: Meadow's meadow grass ground (HDRP
    Lit, triplanar), tinted darker.
  - **Meadow Environment** (NatureManufacture, Asset Store) in `Assets/NatureManufacture Assets`, trimmed to what
    Act 4 uses (466 MB instead of 3.9 GB): the grass, flower and detail prefabs above with their models, materials
    and textures, the meadow grass ground, insects and particles (bees, butterfly, pollen dust, falling leaves, for
    later), the wind, the HDRP foliage shader (`Foliage Shaders/NM_Foliage.shader`) and its diffusion profiles
    (`HD Settings`, registered in `Assets/Settings/HDRPDefaultResources/DefaultSettingsVolumeProfile.asset`). The HDRP
    versions were taken from its HD RP 17.3 support pack (the built-in ones do not render in HDRP); trees, rocks,
    bushes, fences, roads, demo scenes and the support packs were deleted (reimport from the Asset Store if needed).
    The grass materials' Healthy and Dry colours are tinted towards a deeper green.
  - `Wind` (Meadow `Prefab_Wind`, `NM_Wind`, 20 km/h): global wind for the foliage shader, direction = its forward.
  - `Hill Lighting` (`HillLighting`): while the act is shown, a clear day Sun (32 degrees high, azimuth 120,
    100000 lux, warm white: low and from the side, so the mounds model each other), all `[Animate]`
    (`Show/Act4Controller`), restored when disabled and around saves.
  - **Underground** (`Underground Transition`, `UndergroundTransition`): the act's second step, the camera going under
    the hill to fly through the roots. Two `[Animate]` values (`Show/Act4Controller`, `UndergroundTransition_*`; the
    sequence itself is Gamgie's, from Chataigne or a Timeline): `blackSky` (0 to 1) is the weight of
    `Act4_SeeThrough Underground Volume` (global, priority 11, weight 0 in the scene, profile
    `Act4_SeeThrough Underground Volume Profile.asset`: Physically Based Sky zenith, horizon and ground tints black,
    volumetric clouds density 0, fog tint black, all blending smoothly; the sky's own multiplier does nothing on the
    Physically Based Sky); `groundFade` (0 to 1) dissolves `Hill` (shells and soil included) and `Landscape` in noisy
    patches of `dissolveScale` m (globals `_GroundDissolve` / `_GroundDissolveScale` read by `Soledad/HillGrass` in
    every pass, shadows included), and at 1 in Play mode switches their renderers and `HillGrass` off. Previewed in the
    Editor too (keep both at 0 in the saved scenes). With both at 1 only the tree and the glowing roots remain on black.
  - Act 4 Volume: Physically Based Sky tinted teal-blue (zenith and horizon tints; its ground below the horizon is
    tinted sky blue: from the hilltop the camera sees up to 14 degrees below the horizontal past the hill's edge, and a
    dark ground there read as a grey band instead of sky), volumetric clouds (Sparse preset: a few white cumulus), aerial haze (fog mean free path 3.5 km, height
    150 m to 1.2 km, out to 20 km: the mountains fade into the sky), shadows out to 2.5 km (4 cascades), indirect
    diffuse x 1.8 (lighter shaded slopes), fixed exposure EV 13.1.

- **Act 5, Memento Mori** (`Assets/Acts/Act5_MementoMori/`): the Earth seen from space, the Moon away from it: the
  scene of Oxipital-Engine's `Oxipital Seleynora` (performance version, branch `seleynora`), recentred on the Earth
  (there the camera sat by the Moon). Assets copied from Oxipital-Engine with their `.meta` (same GUIDs):
  - `Earth`: an instance of the shared Earth prefab (below, `Assets/Shared/Earth`) at the act origin, scale 16
    (radius 16 m), `month` 12.7 (21 December), `hour` 12 (UTC: the sun over Greenwich, as Seleynora had it). The
    sky's galactic frame in the scene (checked by rendering the sky): galactic centre (l 0) = +Z, l 90 = -X, l 270 =
    +X (the Large Magellanic Cloud below the plane), north galactic pole = +Y. The Earth's `poleHint` is the north
    celestial pole in that frame (l 122.93, b +27.13: 62.87 degrees from the galactic pole), so with the sun from +Z
    (towards the galactic centre, where the real sun is in late December) the December Earth is true to the stars
    (checked: noon sun 17.7 degrees high over Paris, New York before sunrise, Tokyo at night). The Moon at
    (0, 32, 40) sits 39 degrees above the galactic plane, far from the ecliptic (the real Moon stays within 5 degrees
    of it): an artistic placement.
  - `Moon` at (0, 32, 40) from the Earth (51 m), child `Moon_HighRes` (`Moon/Moon_HighRes.fbx`, 3.15 M vertices,
    radius 0.5, scale 8 = radius 4 m; `Moon.mat` HDRP Lit, colour, normal and vertex displacement maps of 200, 155
    and 32 MB, imported at 2048).
  - Earth as particles: the Oxipital emitter `StreamingAssets/emitters/Earth.glb` (from Oxipital-Engine, 18 MB: the
    same radius 1 UV sphere, 3223 vertices, 5888 triangles, the 8k Earth texture embedded), a custom mesh `Earth`
    for an Orb Group (texture opacity 1 for the Earth's colours). Oxipital spreads grains by triangle, and a UV
    sphere's triangles shrink towards the poles: the poles get denser. Not used in the scene yet.
  - `Space Lighting` (`SpaceLighting`): while the act is shown, the Sun of Soledad_Main is Seleynora's light (from +Z,
    the Moon's side: `sunElevation` 0, `sunAzimuth` 180, 78812 lux, 6570 K), all `[Animate]` (`Show/Act5Controller`),
    restored when disabled and around saves; it keeps `Sun Disc` (Unity sphere, scale 11.5, `Materials/Sun Disc.mat`
    HDRP Unlit, Seleynora's lens flare: `LensFlareComponentSRP`, default SRP flare, intensity 36.36) on the light's
    axis, `sunDiscDistance` 500 m from the Earth (`showSunDisc`).
  - Act 5 Volume (Seleynora's look): HDRI sky `Textures/ESO_-_Milky_Way.jpg` (cubemap) at **exposure 14.2** (the
    Milky Way shows; set by Gamgie, kept), **fixed exposure EV 15**, fog off (Seleynora's height fog was tied to the
    Moon's height, meaningless once recentred), bloom 0.3 (threshold 0.27, scatter 0.7, High quality, Seleynora's
    lens dirt `Textures/LensDirt02.png` at 1.4), screen space lens flare 12.71 (it feeds on the bloom: with bloom 0, as
    Seleynora's saved profile had it, neither the bloom nor the flare showed), motion blur 0.
  - Earth at the origin: the Oxipital cameras and the Ballet's particles are centred on it, so a camera closer than
    16 m to the origin is inside the Earth.

## Shared assets: the Earth

`Assets/Shared/` holds assets used across acts. The ActControllerGenerator adds the `[Animate]` members of its scripts
to the controller of every act whose scene uses them (directly or through a prefab, from the scene's dependencies):
for example `Show/Act5Controller.EarthGlobe_month`.

- **Earth** (`Assets/Shared/Earth/Earth.prefab`): drop it in an act and scale it (radius 1 m at scale 1). Its object
  stays unrotated; `EarthGlobe` builds the globe on the child `Globe` (a UV sphere, `segments` 256 around, generated,
  never saved) and turns it every frame:
  - season: `month` (1 = 1 January ... 12.7 = 21 December, up to 13) sets the sun's declination (23.44 degrees
    tilt), the angle between the axis and the sun light (`sun`, else the directional light named "Sun"); the pole
    leans towards `poleHint` (world) as far as that angle allows;
  - day: `hour` (UTC, 12 = the sun over Greenwich) plus the time elapsed, one turn per `dayDuration` seconds (0 =
    still), accumulated from `Time.deltaTime` (deterministic Recorder renders), in Play mode (`runInEditMode` for a
    preview); `ResetDay()` restarts from `hour`. `month`, `dayDuration`, `hour`, `cloudDrift` and `ResetDay` are
    `[Animate]`;
  - clouds drift by `cloudDrift` degrees per day over the ground; the sun direction goes to the shader
    (`_EarthSunDirection`, global).
  Mesh convention (as Earth.fbx and the Earth.glb emitter): north pole local +Y, Greenwich local -X, 90 E local -Z.
- Shader `Soledad/Earth` (`Shaders/`, hand-written on HDRP's Lit passes like the Act 2 terrain): the surface is looked
  up per pixel from the direction on the sphere (round and seamless whatever the mesh; two u coordinates with their
  wrap on opposite meridians, the pixel keeps the continuous one). Ground: day map; smoothness from the water mask
  (ocean 0.78: the sun glints on the sea only, land 0.25); clouds over it (colour, opacity, smoothness 0.05); city
  lights where the sun is below the horizon (`Lights Start` / `Lights Full` sun heights), the night map's dark
  background removed (`Map Black Level` 0.1), hidden by the clouds (0.8), emission exposure weight 0 (same brightness
  whatever the act's exposure); an atmosphere rim on the day side (colour, intensity, thinness). Material
  `Materials/Earth.mat`.
- Textures (`Textures/`, mipmapped, anisotropic 8, u repeat):
  - `EarthDay_BMNG_December_16k.jpg`: NASA Blue Marble Next Generation, December 2004, topography and bathymetry,
    no clouds (`world.topo.bathy.200412.3x21600x10800.jpg`, 2 km/px) resized to 16384 x 8192 (Unity's largest
    texture, 2.4 km/px), BC7. Credit: NASA Earth Observatory.
  - `EarthNight_BlackMarble2016_3km.jpg`: NASA Black Marble 2016 colour (13500 x 6750), imported at 16384 x 8192
    (6750 is not a multiple of 4: uncompressed it took ~360 MB), DXT1.
  - `EarthWaterMask_GEBCO_8k.png`: from NASA's GEBCO bathymetry (`gebco_08_rev_bath_21600x10800.jpg`: land = 255,
    even the shallowest seas below), thresholded at full resolution then 8192 x 4096, single channel. Lakes count as
    land.
  - `EarthClouds_SolarSystemScope_8k.jpg`: Solar System Scope 8k clouds (CC BY 4.0: credit Solar System Scope),
    single channel. NASA's Blue Marble cloud layer is no longer online.
  Sharper still would need NASA's 500 m tiles (eight 21600 x 21600 per map) split over several textures; AI
  upscaling would invent coastlines. Next: a separate cloud shell (parallax, shadows on the ground), a real
  atmosphere halo beyond the limb.

## Film workflow: perform, then render

The film is rendered offline, slower than real time, so it cannot be driven by Chataigne live. Each act is
performed live with Chataigne (gamepad for the camera, Leap for the hands) against the recorded music at preview
resolution, the values reaching Unity are recorded into a Timeline, then Unity Recorder renders that Timeline at full
resolution (image sequences for Resolve). Camera moves can also be written directly in the Timeline. Keep VFX seeds
fixed so re-renders match. Inputs that are not recorded (Leap, Spout, optical flow) do not reach the film.

**One Timeline per act**, under `Film` in `Soledad_Main_Film` (a root object, outside `Root Scene`: not in Chataigne):
`Film/ActN_Timeline` (PlayableDirector, Play On Awake off, Wrap Mode Hold, `FilmTimeline`) plays
`Assets/Acts/<ActScene>/Timeline/ActN_Timeline.playable` (60 fps, fixed length, 60 s until set). Three groups:
- `Show`: an Animation track on `Show` (its Animator drives `Show/ActNController`, the act's `[Animate]` values) and
  anything else common to both formats.
- `Camera 16:9` and `Camera Dome`: the camera keyed apart for each format (an Animation track on
  `Cameras/Camera Controller`: camera type, fov, noise...; add tracks on the camera targets as needed, e.g. Act 2's
  `Spline Camera target`), plus a `Recorder` track: 16:9 records the main camera at 3840 x 2160, Dome records
  `Dome Output Texture` (the DomeMaster, 2048 x 2048 for now), EXR (half float, sRGB, Zip) into
  `Recordings/<ActScene>/16x9|Dome/` (not in git).
`Film` (`Assets/Scripts/Show/Film.cs`) holds the switches: `format` (Flat / Dome) is the output of the whole project,
live show included: it sets `CameraController.isFullDome` (read once when the controller is enabled: set it before
Play), the `Dome Creator` and the main camera, and mutes the other format's camera group in all five Timelines (the
assets change: commit them in the format you work in). `record` unmutes the Recorder tracks (off: Play is a
preview, nothing written). In a preview `FilmTimeline` plays the director on the audio clock (DSPClock), so the
picture stays on the sound (the Act 1 Timeline holds the temporary soundtrack) even when frames are slow; with `record`
on, game time, stepped one frame at a time by the Recorder. `playTimelines` (off for the live show): in Play mode the Timeline of the shown act
plays from its start (`ShowController.ShownAct`, so open the act scene next to Soledad_Main, then Play);
`rideSpline` first waits for the act's path and restarts the Spline camera ride. `Soledad > Build Film Timelines`
(`FilmTimelineBuilder`) creates whatever is missing (Film, timelines, groups, tracks, bindings, Animators), moves
earlier root tracks into the groups (on Show into `Show`, others into `Camera 16:9`, copied into `Camera Dome`), and
stretches the Recorder clips to the Timeline: run it again after changing a Timeline's length.

### Takes: recording what Chataigne drives into the Timeline

`TakeRecorder` (`Assets/Scripts/Takes`, generic, meant to move into the Oxipital packages once proven) records values
in Play mode into Animation clips on the Timeline that is playing, at the Timeline's time, so a camera flown live
with the gamepad is replayed, edited in the Animation window and rendered like any keyed animation. Editor only.
- Start / stop: `record` (exposed to OSCQuery: `Root Scene/Take Recorder`, a gamepad button in Chataigne),
  `ToggleTake()`, or the Record button of its inspector. The take starts where the Timeline is (play from 1:20, record:
  a punch in at 1:20), stops with `record` off, at the end of the Timeline or when Play stops.
- What: `targets`, each a component and its members (inspector: pick the component, "Add member"): serialized fields
  (float, int, bool, enum, Vector2/3/4, Color; `*` = all of them, e.g. a whole `ActNController`), and on a Transform
  `position`, `rotation`, `scale` (local). Modes: Record, Angles (unwrapped; a Vector3 of Euler angles kept continuous
  through the poles), Constant (`value` keyed over the whole take, `liveValue` set when the take starts).
- While recording, every track of the Timeline bound to a recorded object is muted (previous takes, keyed values), so
  the live control has the hand; they come back at the stop. Values are sampled in LateUpdate (after physics
  interpolation and the Timeline); times are the director's, smoothed on the frame time (on the audio clock its time
  moves by audio buffers of about 20 ms). No allocation while recording up to `reservedSamples` per curve.
- Result (`TakeWriter`, editor): a group `<takeName> Take NN` under `timelineGroup` (a camera group follows `Film.format`),
  one Animation track per recorded object named by its scene path, its clip in `<Timeline folder>/Takes/`
  (`Act2_Timeline_Camera_Take03_<object>.anim`). Keys are reduced: smooth tangents from the recording, the fewest keys
  whose curve stays within `tolerance` (1 mm) / `angleTolerance` (0.02 degree) of every sample (a 9 s test flight: 20 to 50
  keys per curve). int and enum go through discrete bindings, whose keys hold the integer's bits (a float binding
  wrote 1.0's bits, 1065353216, into the enum). A later take overrides earlier ones where it has keys. After its end a
  take holds its last values, unless an earlier take goes on (punch in: then nothing outside itself); before its
  start it gives the hand to the tracks below, unless it starts at 0. A punch in cuts at both ends where the new
  flight left the old one: give its clip an ease in / out to blend. Change the extrapolation in the clip's inspector
  if needed.
- Bindings: takes are written in Play mode; the Timeline asset keeps them, but the director's bindings (and an
  Animator added meanwhile) are scene data Unity drops when Play stops. `TakeWriter` binds the take tracks again before
  and after Play, when a scene opens and after a script reload (menu `Soledad > Takes > Repair Take Bindings`), and
  `TakeRecorder.Start` binds what is still missing for the Play session: save the scene after a session of takes.
- Camera preset (Soledad_Main_Film, `Take Recorder`, takes named `Camera`, group `Camera 16:9`/`Camera Dome`): the
  Spaceship target's Transform (position, rotation), `SpaceshipTakePlayback.followTransform` (constant 1, live 0) and
  the Camera Controller's `cameraType` (constant Spaceship), `fov`, `cameraNoiseGain`, `cameraNoiseFrequency`.
  `SpaceshipTakePlayback` (on `Cameras/Spaceship Camera target`, with an Animator): while `followTransform` is keyed on,
  it puts the spaceship in manual control fed with its own Transform before each physics step and turns the
  rigidbody's interpolation off, so the Timeline alone places the camera at every frame (the spaceship's manual
  control sets the rigidbody at 50 Hz: a 60 fps render would have stepped; interpolation put the old pose back while
  the Timeline was paused). Checked in Play on Act 2: camera on the recorded curve to 0.1 mm, also paused, and a punch
  in overriding only its own span.
- Switching the camera type at the start of a take blends over the Camera Controller's `cameraTransitionDuration`
  (8 s in Act 2): be in Spaceship before recording.

## Commits

`.asset`, `.glb` and other binaries go through Git LFS (`.gitattributes`). Commit act scenes together with any new
`.meta` files. Never commit `Library`, `Temp`, `Logs`, `UserSettings` or the generated `.csproj` / `.slnx` files
(already in `.gitignore`).
