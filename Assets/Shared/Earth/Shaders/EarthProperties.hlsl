// Properties of Soledad/Earth. HDRP's LitProperties.hlsl already declares the UnityPerMaterial buffer (needed by the
// HDRP passes), so these are plain uniforms (the shader is not SRP Batcher compatible, which does not matter here).

// One sampler for every map (trilinear, repeat, anisotropic 8): u wraps around the globe
SAMPLER(sampler_trilinear_repeat_aniso8);

TEXTURE2D(_DayMap);         // equirectangular colour, no clouds (u 0.5 = Greenwich, v 1 = north pole)
TEXTURE2D(_NightMap);       // city lights
TEXTURE2D(_WaterMask);      // 1 = sea, 0 = land
TEXTURE2D(_CloudMap);       // cloud cover (red channel)

float4 _DayTint;
float  _LandSmoothness;
float  _OceanSmoothness;

float4 _CloudColor;
float  _CloudOpacity;
float  _CloudSmoothness;
float  _CloudOffset;        // cloud drift around the axis, in turns (set by EarthGlobe)

float4 _CityLightsColor;
float  _CityLightsIntensity;
float  _CityLightsExposureWeight;   // 0 = same brightness whatever the exposure, 1 = physical (nits)
float  _CityLightsBlackLevel;       // the night map's dark background, removed
float  _CityLightsUnderClouds;      // how much the clouds hide the lights
float  _TerminatorStart;            // sun height (sine) where the lights start to show
float  _TerminatorEnd;              // sun height (sine) where they are fully on

float4 _AtmosphereColor;
float  _AtmosphereIntensity;
float  _AtmospherePower;

// Direction towards the sun, world space (set every frame by EarthGlobe)
float4 _EarthSunDirection;
