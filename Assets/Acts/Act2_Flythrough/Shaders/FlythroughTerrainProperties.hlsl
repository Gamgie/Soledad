// Properties of Soledad/FlythroughTerrain. HDRP's LitProperties.hlsl already declares the UnityPerMaterial buffer
// (needed by the HDRP passes), so these are plain uniforms: set on the material and every frame by
// FlythroughTerrain.cs. The shader is not SRP Batcher compatible, which does not matter for one terrain.

// Surface colours (sRGB in the Inspector, linear here)
float4 _SandColor;          // dune slopes
float4 _SandDarkColor;      // dune troughs and wind shadows
float4 _RockColor;          // steep slopes
float4 _RockDarkColor;
float4 _DryGrassColor;      // gentle mountain slopes
float4 _GrassColor;         // valley fields
float4 _FieldColorB;        // second field crop
float4 _SoilColor;          // ploughed fields, paths
float  _TerrainSmoothness;
float  _RockSlope;          // slope (1 - normal.y) where rock takes over
float  _FieldSize;          // size of the valley fields (m)

// Scanned sand layers (desert): rippled sand and dry sand, PBR sets of the same physical size
TEXTURE2D(_RippleSandAlbedo);    SAMPLER(sampler_RippleSandAlbedo);
TEXTURE2D(_RippleSandNormal);    SAMPLER(sampler_RippleSandNormal);
TEXTURE2D(_RippleSandHeight);    SAMPLER(sampler_RippleSandHeight);
TEXTURE2D(_RippleSandRoughness); SAMPLER(sampler_RippleSandRoughness);
TEXTURE2D(_RippleSandAO);        SAMPLER(sampler_RippleSandAO);
TEXTURE2D(_DrySandAlbedo);       SAMPLER(sampler_DrySandAlbedo);
TEXTURE2D(_DrySandNormal);       SAMPLER(sampler_DrySandNormal);
TEXTURE2D(_DrySandHeight);       SAMPLER(sampler_DrySandHeight);
TEXTURE2D(_DrySandRoughness);    SAMPLER(sampler_DrySandRoughness);
TEXTURE2D(_DrySandAO);           SAMPLER(sampler_DrySandAO);
float4 _SandTint;
float  _SandTile;           // physical size of one texture repeat (m)
float  _SandNormalStrength;
float  _SandNormalDirectX;  // 0 = OpenGL normal maps, 1 = DirectX (green flipped)
float  _SandLargeScale;     // the anti-tiling sample is this many times larger
float  _SandLargeStrength;  // relief of the large sample
float  _SandAOStrength;
float  _RippleCoverage;       // -1 = all dry sand, 1 = ripples everywhere the slope allows
float  _RippleHeightBias;     // + = ripples on the upper part of the dunes, - = on the lower part
float  _RippleSlopeLimit;     // steeper than this (1 - normal.y) is always dry sand
float  _RipplePatchiness;     // large noise patches breaking the placement
float  _RippleBlendSharpness; // sharpness of the border between the two sands

// Fine relief, computed per pixel (below the mesh resolution)
float  _DetailStrength;
float  _DetailScale;        // size of the largest detail feature (m)

// World curl, in absolute world space. Origin = the ground point under the flight anchor; the flight runs along +Z.
float4 _CurlOrigin;         // xyz
float  _CurlLateral;        // 1 / radius of the tube the ground rolls into around the flight axis (0 = flat)
float  _CurlForward;        // 1 / radius of the bend that lifts the ground ahead into a wall (0 = flat)
float4 _CurlFrame;          // xy = flight direction in world xz (unit)
