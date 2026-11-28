// Bogota's map (the GIS chunks under World/Bogota) on HDRP's own Lit passes: the chunk's base colour map and
// smoothness, like the HDRP Lit materials it replaces, plus the reveal (BogotaRevealData.hlsl) that makes Bogota appear
// from a centre outwards in Play mode, driven by BogotaSequence.bogotaReveal. Double sided, alpha tested, no decals,
// no SSR, no motion vectors (Bogota does not move).
Shader "Soledad/BogotaReveal"
{
    Properties
    {
        _BaseColorMap("Map", 2D) = "white" {}
        _BaseColor("Tint", Color) = (1, 1, 1, 1)
        _Smoothness("Smoothness", Range(0, 1)) = 0.05
        _Metallic("Metallic", Range(0, 1)) = 0

        // HDRP state, as the Lit material inspector would set it for an opaque alpha tested surface without SSR
        [HideInInspector] _StencilRef("_StencilRef", Int) = 0
        [HideInInspector] _StencilWriteMask("_StencilWriteMask", Int) = 6
        [HideInInspector] _StencilRefDepth("_StencilRefDepth", Int) = 0
        [HideInInspector] _StencilWriteMaskDepth("_StencilWriteMaskDepth", Int) = 8
        [HideInInspector] _StencilRefGBuffer("_StencilRefGBuffer", Int) = 2
        [HideInInspector] _StencilWriteMaskGBuffer("_StencilWriteMaskGBuffer", Int) = 14
        [HideInInspector] _ZTestGBuffer("_ZTestGBuffer", Int) = 4
        [HideInInspector] _ZTestDepthEqualForOpaque("_ZTestDepthEqualForOpaque", Int) = 4
        [HideInInspector] _ZClip("_ZClip", Float) = 1.0
    }

    HLSLINCLUDE
    #pragma target 4.5
    #pragma only_renderers d3d11 playstation xboxone xboxseries vulkan metal switch switch2

    #define _DEFERRED_CAPABLE_MATERIAL
    #define _DISABLE_SSR
    #define _ALPHATEST_ON
    #define PREFER_HALF 0

    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/FragInputs.hlsl"
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/ShaderPass.cs.hlsl"
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/LitProperties.hlsl"
    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "HDLitShader" "Queue" = "AlphaTest" }

        Pass
        {
            Name "SceneSelectionPass"
            Tags { "LightMode" = "SceneSelectionPass" }
            Cull Off

            HLSLPROGRAM
            #pragma multi_compile_instancing
            #define SHADERPASS SHADERPASS_DEPTH_ONLY
            #define SCENESELECTIONPASS
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/PickingSpaceTransforms.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Material.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/Lit.hlsl"
            #include "BogotaRevealPass.hlsl"
            #include "BogotaRevealData.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/ShaderPassDepthOnly.hlsl"
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma editor_sync_compilation
            ENDHLSL
        }

        Pass
        {
            Name "GBuffer"
            Tags { "LightMode" = "GBuffer" }
            Cull Off
            ZTest [_ZTestGBuffer]
            Stencil
            {
                WriteMask [_StencilWriteMaskGBuffer]
                Ref [_StencilRefGBuffer]
                Comp Always
                Pass Replace
            }

            HLSLPROGRAM
            #pragma multi_compile_instancing
            #pragma multi_compile _ DEBUG_DISPLAY
            #pragma multi_compile_fragment _ SHADOWS_SHADOWMASK
            #pragma multi_compile_fragment _ PROBE_VOLUMES_L1 PROBE_VOLUMES_L2
            #pragma multi_compile_fragment _ RENDERING_LAYERS
            #define SHADERPASS SHADERPASS_GBUFFER
            #ifdef DEBUG_DISPLAY
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Debug/DebugDisplay.hlsl"
            #endif
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Material.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/Lit.hlsl"
            #include "BogotaRevealPass.hlsl"
            #include "BogotaRevealData.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/ShaderPassGBuffer.hlsl"
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            Cull Off
            ZClip [_ZClip]
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma multi_compile_instancing
            #define SHADERPASS SHADERPASS_SHADOWS
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Material.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/Lit.hlsl"
            #include "BogotaRevealPass.hlsl"
            #include "BogotaRevealData.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/ShaderPassDepthOnly.hlsl"
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            Cull Off
            ZWrite On
            Stencil
            {
                WriteMask [_StencilWriteMaskDepth]
                Ref [_StencilRefDepth]
                Comp Always
                Pass Replace
            }

            HLSLPROGRAM
            #pragma multi_compile_instancing
            #pragma multi_compile _ WRITE_NORMAL_BUFFER
            #pragma multi_compile_fragment _ WRITE_MSAA_DEPTH
            #define SHADERPASS SHADERPASS_DEPTH_ONLY
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Material.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/Lit.hlsl"
            #include "BogotaRevealPass.hlsl"
            #include "BogotaRevealData.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/ShaderPassDepthOnly.hlsl"
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "Forward" }
            Stencil
            {
                WriteMask [_StencilWriteMask]
                Ref [_StencilRef]
                Comp Always
                Pass Replace
            }
            ZTest [_ZTestDepthEqualForOpaque]
            ZWrite On
            Cull Off

            HLSLPROGRAM
            #pragma multi_compile_instancing
            #pragma multi_compile _ DEBUG_DISPLAY
            #pragma multi_compile_fragment _ SHADOWS_SHADOWMASK
            #pragma multi_compile_fragment _ PROBE_VOLUMES_L1 PROBE_VOLUMES_L2
            #pragma multi_compile_fragment SCREEN_SPACE_SHADOWS_OFF SCREEN_SPACE_SHADOWS_ON
            #pragma multi_compile_fragment PUNCTUAL_SHADOW_LOW PUNCTUAL_SHADOW_MEDIUM PUNCTUAL_SHADOW_HIGH
            #pragma multi_compile_fragment DIRECTIONAL_SHADOW_LOW DIRECTIONAL_SHADOW_MEDIUM DIRECTIONAL_SHADOW_HIGH
            #pragma multi_compile_fragment AREA_SHADOW_MEDIUM AREA_SHADOW_HIGH
            #pragma multi_compile_fragment USE_FPTL_LIGHTLIST USE_CLUSTERED_LIGHTLIST

            #ifndef SHADER_STAGE_FRAGMENT
            #define SHADOW_LOW
            #ifndef USE_FPTL_LIGHTLIST
            #define USE_FPTL_LIGHTLIST
            #endif
            #endif

            #define SHADERPASS SHADERPASS_FORWARD
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Material.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/Lighting.hlsl"
            #ifdef DEBUG_DISPLAY
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Debug/DebugDisplay.hlsl"
            #endif
            #define HAS_LIGHTLOOP
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/LightLoop/LightLoopDef.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/Lit.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/LightLoop/LightLoop.hlsl"
            #include "BogotaRevealPass.hlsl"
            #include "BogotaRevealData.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/ShaderPassForward.hlsl"
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }
    }

    FallBack "Hidden/HDRP/FallbackError"
}
