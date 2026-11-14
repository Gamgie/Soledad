// Terrain of Act 2 (Flythrough): an HDRP Lit standard surface (sun, shadows, fog, volumetrics, GI) on HDRP's own Lit
// passes, with a procedural surface (FlythroughTerrainData.hlsl) and the world curl applied to the vertices
// (FlythroughTerrainPass.hlsl). The mesh is built and moved by FlythroughTerrain.cs. Opaque only, no SSR, no decals.
Shader "Soledad/FlythroughTerrain"
{
    Properties
    {
        [Header(Colours)]
        _SandColor("Sand", Color) = (0.80, 0.58, 0.36, 1)
        _SandDarkColor("Sand Shade", Color) = (0.62, 0.40, 0.23, 1)
        _RockColor("Rock", Color) = (0.50, 0.44, 0.38, 1)
        _RockDarkColor("Rock Dark", Color) = (0.27, 0.23, 0.20, 1)
        _DryGrassColor("Dry Grass", Color) = (0.62, 0.55, 0.32, 1)
        _GrassColor("Grass", Color) = (0.30, 0.45, 0.16, 1)
        _FieldColorB("Field Crop", Color) = (0.55, 0.60, 0.24, 1)
        _SoilColor("Soil", Color) = (0.36, 0.27, 0.18, 1)
        _TerrainSmoothness("Smoothness", Range(0, 1)) = 0.12
        _RockSlope("Rock Slope (1 - normal.y)", Range(0, 1)) = 0.35
        _FieldSize("Field Size (m)", Float) = 140

        [Header(Desert Sand (scanned PBR sets))]
        [NoScaleOffset] _RippleSandAlbedo("Ripple Albedo", 2D) = "white" {}
        [NoScaleOffset][Normal] _RippleSandNormal("Ripple Normal", 2D) = "bump" {}
        [NoScaleOffset] _RippleSandHeight("Ripple Height", 2D) = "gray" {}
        [NoScaleOffset] _RippleSandRoughness("Ripple Roughness", 2D) = "white" {}
        [NoScaleOffset] _RippleSandAO("Ripple AO", 2D) = "white" {}
        [NoScaleOffset] _DrySandAlbedo("Dry Albedo", 2D) = "white" {}
        [NoScaleOffset][Normal] _DrySandNormal("Dry Normal", 2D) = "bump" {}
        [NoScaleOffset] _DrySandHeight("Dry Height", 2D) = "gray" {}
        [NoScaleOffset] _DrySandRoughness("Dry Roughness", 2D) = "white" {}
        [NoScaleOffset] _DrySandAO("Dry AO", 2D) = "white" {}
        _SandTint("Sand Tint", Color) = (1, 1, 1, 1)
        _SandTile("Texture Size (m)", Float) = 2
        _SandNormalStrength("Normal Strength", Range(0, 3)) = 1
        [Enum(OpenGL, 0, DirectX, 1)] _SandNormalDirectX("Normal Map Convention", Float) = 0
        _SandLargeScale("Anti-tiling Scale", Range(2, 50)) = 11
        _SandLargeStrength("Anti-tiling Relief", Range(0, 20)) = 6
        _SandAOStrength("AO Strength", Range(0, 1)) = 0.7
        _RippleCoverage("Ripple Coverage", Range(-1, 1)) = 0
        _RippleHeightBias("Ripple Height Preference (+ crests, - troughs)", Range(-2, 2)) = 0.8
        _RippleSlopeLimit("Ripple Slope Limit", Range(0, 1)) = 0.3
        _RipplePatchiness("Ripple Patchiness", Range(0, 2)) = 0.6
        _RippleBlendSharpness("Sand Border Sharpness", Range(0.5, 12)) = 4

        [Header(Fine Relief)]
        _DetailStrength("Relief (m)", Float) = 1.5
        _DetailScale("Largest Feature (m)", Float) = 24

        [Header(World Curl (set by FlythroughTerrain))]
        _CurlOrigin("Curl Origin (world)", Vector) = (0, 0, 0, 0)
        _CurlLateral("Lateral (1 / radius)", Float) = 0
        _CurlForward("Forward (1 / radius)", Float) = 0
        _CurlFrame("Flight Direction (world xz)", Vector) = (0, 1, 0, 0)

        // HDRP state, as the Lit material inspector would set it for an opaque standard surface without SSR
        [HideInInspector] _CullMode("__cullmode", Float) = 2.0
        [HideInInspector] _StencilRef("_StencilRef", Int) = 0
        [HideInInspector] _StencilWriteMask("_StencilWriteMask", Int) = 6
        [HideInInspector] _StencilRefDepth("_StencilRefDepth", Int) = 0
        [HideInInspector] _StencilWriteMaskDepth("_StencilWriteMaskDepth", Int) = 8
        [HideInInspector] _StencilRefGBuffer("_StencilRefGBuffer", Int) = 2
        [HideInInspector] _StencilWriteMaskGBuffer("_StencilWriteMaskGBuffer", Int) = 14
        [HideInInspector] _StencilRefMV("_StencilRefMV", Int) = 32
        [HideInInspector] _StencilWriteMaskMV("_StencilWriteMaskMV", Int) = 40
        [HideInInspector] _ZTestGBuffer("_ZTestGBuffer", Int) = 4
        [HideInInspector] _ZTestDepthEqualForOpaque("_ZTestDepthEqualForOpaque", Int) = 3
        [HideInInspector] _ZClip("_ZClip", Float) = 1.0
    }

    HLSLINCLUDE
    #pragma target 4.5
    #pragma only_renderers d3d11 playstation xboxone xboxseries vulkan metal switch switch2

    // Opaque, deferred capable
    #define _DEFERRED_CAPABLE_MATERIAL
    #define _DISABLE_SSR
    #define PREFER_HALF 0

    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/FragInputs.hlsl"
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/ShaderPass.cs.hlsl"
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/LitProperties.hlsl"
    #include "FlythroughTerrainProperties.hlsl"
    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "HDLitShader" "Queue" = "Geometry" }

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
            #include "FlythroughTerrainPass.hlsl"
            #include "FlythroughTerrainData.hlsl"
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
            Cull [_CullMode]
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
            #include "FlythroughTerrainPass.hlsl"
            #include "FlythroughTerrainData.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/ShaderPassGBuffer.hlsl"
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            Cull [_CullMode]
            ZClip [_ZClip]
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma multi_compile_instancing
            #define SHADERPASS SHADERPASS_SHADOWS
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Material.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/Lit.hlsl"
            #include "FlythroughTerrainPass.hlsl"
            #include "FlythroughTerrainData.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/ShaderPassDepthOnly.hlsl"
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            Cull [_CullMode]
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
            #include "FlythroughTerrainPass.hlsl"
            #include "FlythroughTerrainData.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/ShaderPassDepthOnly.hlsl"
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }

        Pass
        {
            Name "MotionVectors"
            Tags { "LightMode" = "MotionVectors" }
            Stencil
            {
                WriteMask [_StencilWriteMaskMV]
                Ref [_StencilRefMV]
                Comp Always
                Pass Replace
            }
            Cull [_CullMode]
            ZWrite On

            HLSLPROGRAM
            #pragma multi_compile_instancing
            #pragma multi_compile _ WRITE_NORMAL_BUFFER
            #pragma multi_compile_fragment _ WRITE_MSAA_DEPTH
            #define SHADERPASS SHADERPASS_MOTION_VECTORS
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Material.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/Lit.hlsl"
            #include "FlythroughTerrainPass.hlsl"
            #include "FlythroughTerrainData.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/ShaderPassMotionVectors.hlsl"
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
            Cull [_CullMode]

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
            #define SHADERPASS_FORWARD_BYPASS_ALPHA_TEST
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Material.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/Lighting.hlsl"
            #ifdef DEBUG_DISPLAY
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Debug/DebugDisplay.hlsl"
            #endif
            #define HAS_LIGHTLOOP
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/LightLoop/LightLoopDef.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/Lit.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/LightLoop/LightLoop.hlsl"
            #include "FlythroughTerrainPass.hlsl"
            #include "FlythroughTerrainData.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/ShaderPassForward.hlsl"
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }
    }

    FallBack "Hidden/HDRP/FallbackError"
}
