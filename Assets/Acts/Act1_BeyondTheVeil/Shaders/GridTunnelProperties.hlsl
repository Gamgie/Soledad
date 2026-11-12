// Material properties of Soledad/GridTunnel. GridTunnel.cs drives most of them every frame
// through a MaterialPropertyBlock, the material asset holds the defaults.

CBUFFER_START(UnityPerMaterial)

// Look
float4 _BaseColor;          // background of the walls (black for the void)
float4 _LineColor;          // HDR
float  _LineWidth;          // in cells (0.02 = 2% of a cell)
float4 _WindowColor;        // HDR, randomly blinking windows
float4 _SoloColor;          // HDR, the single window
float  _FadeStart;          // distance (m) where lines and windows start fading to the background
float  _FadeEnd;            // distance (m) where they are gone

// Random blink
float  _RandomDensity;      // probability that a cell is lit during one of its ticks (0 to 1)
float  _RandomRate;         // ticks per second per cell (0 = static pattern)
float  _RandomRateJitter;   // 0 = every cell ticks at the same rate, 1 = rates spread from 0x to 2x
float  _RandomFade;         // 0 = hard on/off, 1 = each blink fades in and out over its whole tick
float  _RandomIntensity;
float  _RandomSeed;

// Solo window: xyz = wall, column, row (tunnel space), w = 1 enabled
float4 _SoloCell;
float  _SoloLevel;          // 0 to 1, computed by GridTunnel.cs (blink rate, duty, fade)

// Script-driven time and infinite tunnel offsets (deterministic for the film)
float  _BlinkTime;
float  _NoiseTime;
float4 _TunnelOffset;       // x = tunnel z offset (m), y = row offset integer part, z = row offset fractional part

// Line appearance. Rails run along the tunnel (grow from the camera towards the end), rings go around it
// (grow from the middle of the floor up both sides). Ages are in seconds since the line family was shown / hidden.
float  _ShowTime;
float  _CameraRow;          // row of the camera, relative to the integer row offset
float4 _GridInfo;           // x = cells across floor / ceiling, y = cells across side walls, z = cell size (m), w = rail reveal length (m)
float4 _RailsGrow;          // x = age since shown, y = age since hidden (-1 = shown), z = growth speed (m/s), w = delay spread (s)
float4 _RailsFx;            // x = order, y = effect type, z = effect speed (cycles/s), w = effect spread (s)
float4 _RingsGrow;
float4 _RingsFx;
float4 _LinesFxMin;         // x = rails effect minimum, y = rings effect minimum

// Noise deformation (like a Noise SOP)
float  _NoiseAmplitude;     // meters
float  _NoisePeriod;        // meters
float  _NoiseOctaves;       // harmonics, 1 to 8
float  _NoiseRoughness;     // gain between harmonics
float  _NoiseLacunarity;    // frequency step between harmonics
float  _NoiseStretch;       // stretches the noise along the tunnel axis
float  _NoiseRadial;        // 0 = free 3D displacement, 1 = only towards / away from the tunnel axis
float4 _NoiseFlow;          // xyz = drift direction of the noise per unit of _NoiseTime
float  _NoiseSeed;

float  _CullMode;

CBUFFER_END

// Fed by the Editor for scene selection
int _ObjectId;
int _PassValue;
