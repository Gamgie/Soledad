// Attributes / varyings of Soledad/CloudCluster: Unity's cube mesh, scaled by CloudCluster.cs to a box that holds
// the whole cluster and its fluff (no vertex change). Forward pass only.
#ifndef SHADERPASS
#error Undefine_SHADERPASS
#endif

#define _DISABLE_DECALS
#define ATTRIBUTES_NEED_NORMAL
#define ATTRIBUTES_NEED_TANGENT

#if (SHADERPASS == SHADERPASS_FORWARD)
#define CC_FULL_SURFACE
#define VARYINGS_NEED_POSITION_WS
#define VARYINGS_NEED_TANGENT_TO_WORLD
#endif

#include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/VaryingMesh.hlsl"

// The cubes of the cluster, set every frame by CloudCluster.cs (absolute world space)
#define CC_MAX_CUBES 40
float    _CubeCount;
float4x4 _CubeWorldToLocal[CC_MAX_CUBES];  // world -> cube axes, metres, cube centre at 0 (rotation + translation)
float4   _CubeHalf[CC_MAX_CUBES];          // xyz = half size (m), w = size scale of the length settings
float4   _CubeSeed[CC_MAX_CUBES];          // x = noise seed
