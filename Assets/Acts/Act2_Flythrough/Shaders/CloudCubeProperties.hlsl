// Properties of Soledad/CloudCube. Like the terrain, HDRP's LitProperties.hlsl owns the UnityPerMaterial buffer,
// so these are plain uniforms (per-cube variation comes from a MaterialPropertyBlock: _CloudSeed).

float4 _CloudColor;         // albedo of the cloud
float  _CloudDensity;       // extinction per metre inside the cloud: higher = more opaque
float  _CloudFluff;         // how far (m) the fluffy edge reaches beyond the cube, and how deep it eats into it
float  _CloudNoiseSize;     // size (m) of the largest puffs
float  _CloudSoftness;      // width (m) of the density ramp at the edge
float  _CloudRoundness;     // corner radius (m)
float  _CloudSteps;         // ray-march samples per pixel
float  _CloudScatter;       // fake subsurface glow when looking towards the sun
float  _CloudScatterPhase;  // 0 = glow in every direction, towards 1 = only right around the sun
float  _CloudSmoothness;
float  _CloudShadowCore;    // size of the shadow caster, relative to the cube
float  _CloudDent;          // how much the puffs dig into the cube (0 = never, 1 = as much as they grow)
float  _CloudPuffShading;   // 0 = shaded like a smooth rounded box, 1 = every puff shaded
float  _CloudScaleWithSize; // 0 = fixed lengths (m), 1 = lengths grow with the cube size
float  _CloudReferenceSize; // cube edge (m) at which the lengths are exactly as set
float  _CloudShadeRounding; // how round the cube is for shading (0 = its own corners, 1 = a sphere): soft light around edges
float  _CloudSeed;          // per cube (MaterialPropertyBlock)
float  _CloudFade;          // per cluster (MaterialPropertyBlock, always set by CloudCluster): 1 = seen, 0 = faded out at the draw distance
