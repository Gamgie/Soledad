// Attributes / varyings of Soledad/Earth, plugged into HDRP's Lit passes. The globe mesh (built by EarthGlobe) is a
// unit sphere: the surface is looked up per pixel from the object space direction, so its UVs are not needed.
#ifndef SHADERPASS
#error Undefine_SHADERPASS
#endif

#define _DISABLE_DECALS

#define ATTRIBUTES_NEED_NORMAL
#define ATTRIBUTES_NEED_TANGENT

#if (SHADERPASS == SHADERPASS_GBUFFER) || (SHADERPASS == SHADERPASS_FORWARD) || defined(WRITE_NORMAL_BUFFER)
#define EARTH_FULL_SURFACE
#define VARYINGS_NEED_POSITION_WS
#define VARYINGS_NEED_TANGENT_TO_WORLD
#elif SHADERPASS == SHADERPASS_MOTION_VECTORS
#define VARYINGS_NEED_POSITION_WS
#endif

#include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/VaryingMesh.hlsl"
