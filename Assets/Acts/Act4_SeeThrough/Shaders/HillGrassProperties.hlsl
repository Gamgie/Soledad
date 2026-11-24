// Properties of Soledad/HillGrass. HDRP's LitProperties.hlsl already declares the UnityPerMaterial buffer (needed by
// the HDRP passes), so these are plain uniforms. _ShellHeight is set per draw by HillGrass.cs (MaterialPropertyBlock):
// 0 on the renderer itself (the ground), 0 to 1 for each shell above it.

float  _ShellHeight;        // this shell, 0 (ground) to 1 (tip height)
float  _GroundOnly;         // 1: never shells on this material (the landscape around the hill)

// Scanned grass (Megascans set: albedo, normal, height, roughness, AO, all the same physical size)
SAMPLER(sampler_trilinear_repeat_aniso8);
TEXTURE2D(_GrassAlbedo);
TEXTURE2D(_GrassNormal);
TEXTURE2D(_GrassHeightMap);
TEXTURE2D(_GrassRoughness);
TEXTURE2D(_GrassAO);
float  _TextureSize;        // physical size of the scan (m)
float  _TileBreakScale;     // size of the patches where a second, rotated and scaled sample takes over (m)
float  _TileBreakSharpness;
float4 _GrassTint;          // multiplies the scanned colour
float  _Saturation;
float4 _RecolorTarget;      // hue the scan is pulled towards (its dry, straw blades become green)
float  _Recolor;
float  _NormalStrength;
float  _AOStrength;
float  _RoughnessScale;     // 1 = the scan's roughness, higher = more matte

// Relief: the shells keep the scan where its height map is above them
float  _GrassHeight;        // height of the tallest blades (m)
float  _HeightMin;          // height map value at the ground...
float  _HeightMax;          // ...and at the tips
float  _Lean;               // how far the blades lean with the comb (m at the tip)
float  _CombScale;          // size of the combed patches (m)
float  _WindStrength;       // wind sway at the tip (m)
float  _WindSpeed;
float  _WindScale;          // size of the wind gusts (m)
float  _SelfShadow;         // darkening deep in the grass

// Soil patch around the tree's roots: the grass shortens and gives way to bare soil and leaf litter
TEXTURE2D(_SoilAlbedo);
TEXTURE2D(_SoilNormal);
TEXTURE2D(_LitterAlbedo);
float4 _SoilCenter;         // xy = centre in ground coordinates (m)
float  _SoilRadius;         // 0 = no soil patch
float  _SoilEdge;           // width of the transition (m)
float  _SoilEdgeNoise;      // how irregular the edge is (m)
float  _SoilSize;           // texture size (m)
float4 _SoilTint;
float  _LitterAmount;       // leaf litter patches over the soil
float  _GrassInSoil;        // how far the grass shortens before it is gone (0 = sharp edge)

// Large scale
float  _PatchAmount;        // light / dark patches
float  _PatchScale;         // their size (m)
float4 _PatchColor;         // colour of the light patches (dry, yellower grass)
float  _Sheen;              // velvet rim: lighter where the grass is seen edge-on
float  _SheenPower;
float4 _SheenColor;

// Distance
float  _FadeStart;          // shells fade from here (m)...
float  _FadeEnd;            // ...to here, and when the texture gets smaller than a pixel

// Underground transition (globals, set by Act4Underground.cs, never on the materials): the hill and the landscape
// dissolve in noisy patches, 0 = whole, 1 = gone
float  _GroundDissolve;
float  _GroundDissolveScale;    // size of the patches (m)
