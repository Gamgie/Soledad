// Attributes / varyings of Soledad/GridTunnel and its vertex deformation, plugged into HDRP's own passes
// through the HAVE_MESH_MODIFICATION hook of VertMesh.hlsl.
#ifndef SHADERPASS
#error Undefine_SHADERPASS
#endif

#define HAVE_MESH_MODIFICATION

#if SHADERPASS == SHADERPASS_FORWARD_UNLIT
#define ATTRIBUTES_NEED_TEXCOORD0
#define ATTRIBUTES_NEED_TEXCOORD1
#define VARYINGS_NEED_TEXCOORD0
#define VARYINGS_NEED_TEXCOORD1
#define VARYINGS_NEED_POSITION_WS
#endif

#include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/VaryingMesh.hlsl"
#include "GridTunnelNoise.hlsl"

AttributesMesh ApplyMeshModification(AttributesMesh input, float3 timeParameters)
{
    input.positionOS += GT_NoiseDisplacement(input.positionOS);
    return input;
}
