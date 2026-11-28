// Attributes / varyings of Soledad/BogotaReveal, plugged into HDRP's Lit passes (see BogotaReveal.shader).
// The reveal is computed in every pass (depth, shadows, GBuffer, forward), so the hidden part of Bogota neither
// draws nor casts a shadow.
#ifndef SHADERPASS
#error Undefine_SHADERPASS
#endif

#define _DISABLE_DECALS

#define ATTRIBUTES_NEED_NORMAL
#define ATTRIBUTES_NEED_TEXCOORD0
#define VARYINGS_NEED_POSITION_WS

#if (SHADERPASS == SHADERPASS_GBUFFER) || (SHADERPASS == SHADERPASS_FORWARD) || defined(WRITE_NORMAL_BUFFER)
#define BR_FULL_SURFACE
#define VARYINGS_NEED_TANGENT_TO_WORLD
#define VARYINGS_NEED_TEXCOORD0
#define VARYINGS_NEED_CULLFACE
#endif

#include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/VaryingMesh.hlsl"
