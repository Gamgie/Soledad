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

// Fine relief, computed per pixel (below the mesh resolution)
float  _DetailStrength;
float  _DetailScale;        // size of the largest detail feature (m)

// World curl, in absolute world space. Origin = the ground point under the flight anchor; the flight runs along +Z.
float4 _CurlOrigin;         // xyz
float  _CurlLateral;        // 1 / radius of the tube the ground rolls into around the flight axis (0 = flat)
float  _CurlForward;        // 1 / radius of the bend that lifts the ground ahead into a wall (0 = flat)
