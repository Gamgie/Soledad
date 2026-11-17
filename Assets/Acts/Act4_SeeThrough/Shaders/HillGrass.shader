// Grass of Act 4: a scanned grass set with real relief, as shells, on HDRP's own Lit passes (sun, shadows, fog, GI).
// The renderer itself (hill, landscape) draws the ground; HillGrass.cs draws the hill mesh again for each shell, pushed
// out along the normal (HillGrassPass.hlsl), keeping the scan where its height map reaches the shell
// (HillGrassData.hlsl). Alpha-tested opaque: depth prepass, then GBuffer with an equal depth test and no depth write.
// The ground casts shadows (hills on hills), the shells do not. No motion vectors, no SSR, no decals.
Shader "Soledad/HillGrass"
{
    Properties
    {
        [Header(Scanned Grass)]
        [NoScaleOffset] _GrassAlbedo("Albedo", 2D) = "white" {}
        [NoScaleOffset][Normal] _GrassNormal("Normal (OpenGL)", 2D) = "bump" {}
        [NoScaleOffset] _GrassHeightMap("Height (Displacement)", 2D) = "gray" {}
        [NoScaleOffset] _GrassRoughness("Roughness", 2D) = "white" {}
        [NoScaleOffset] _GrassAO("AO", 2D) = "white" {}
        _TextureSize("Scan Size (m)", Float) = 1
        _TileBreakScale("Anti-tiling Patch Size (m)", Float) = 4
        _TileBreakSharpness("Anti-tiling Sharpness", Range(1, 20)) = 6
        _GrassTint("Tint", Color) = (1, 1, 1, 1)
        _Saturation("Saturation", Range(0, 2)) = 1
        _RecolorTarget("Recolour Towards", Color) = (0.22, 0.42, 0.08, 1)
        _Recolor("Recolour (dry blades to green)", Range(0, 1)) = 0.5
        _NormalStrength("Normal Strength", Range(0, 3)) = 1
        _AOStrength("AO Strength", Range(0, 1)) = 1
        _RoughnessScale("Matte (x roughness)", Range(0.5, 2)) = 1.1

        [Header(Relief (shells))]
        _GrassHeight("Height (m)", Float) = 0.06
        _HeightMin("Height Map at Ground", Range(0, 1)) = 0.3
        _HeightMax("Height Map at Tips", Range(0, 1)) = 0.85
        _Lean("Lean (m at the tip)", Float) = 0.02
        _CombScale("Comb Patch Size (m)", Float) = 14
        _WindStrength("Wind (m at the tip)", Float) = 0.01
        _WindSpeed("Wind Speed", Float) = 0.35
        _WindScale("Wind Gust Size (m)", Float) = 3
        _SelfShadow("Depth Darkening", Range(0, 1)) = 0.5

        [Header(Soil Patch (around the roots))]
        [NoScaleOffset] _SoilAlbedo("Soil Albedo", 2D) = "white" {}
        [NoScaleOffset][Normal] _SoilNormal("Soil Normal", 2D) = "bump" {}
        [NoScaleOffset] _LitterAlbedo("Leaf Litter Albedo", 2D) = "white" {}
        _SoilCenter("Centre (ground m, xy)", Vector) = (0, 0, 0, 0)
        _SoilRadius("Radius (m, 0 = off)", Float) = 0
        _SoilEdge("Edge Width (m)", Float) = 0.6
        _SoilEdgeNoise("Edge Irregularity (m)", Float) = 0.5
        _SoilSize("Soil Texture Size (m)", Float) = 2
        _SoilTint("Soil Tint", Color) = (1, 1, 1, 1)
        _LitterAmount("Leaf Litter", Range(0, 1)) = 0.5
        _GrassInSoil("Grass Fades Into Soil", Range(0, 3)) = 1

        [Header(Large Scale)]
        _PatchAmount("Patches", Range(0, 1)) = 0.3
        _PatchScale("Patch Size (m)", Float) = 18
        _PatchColor("Light Patch Colour", Color) = (0.62, 0.66, 0.30, 1)
        _Sheen("Velvet Sheen", Range(0, 1)) = 0.25
        _SheenPower("Sheen Falloff", Range(0.5, 8)) = 3
        _SheenColor("Sheen Colour", Color) = (0.45, 0.58, 0.32, 1)

        [Header(Distance)]
        _FadeStart("Shells Fade From (m)", Float) = 10
        _FadeEnd("Shells Gone At (m)", Float) = 30
        [Toggle] _GroundOnly("Ground Only (no shells)", Float) = 0

        [HideInInspector] _ShellHeight("Shell", Float) = 0

        // HDRP state, as the Lit material inspector would set it for an alpha-tested opaque surface without SSR
        [HideInInspector] _CullMode("__cullmode", Float) = 2.0
        [HideInInspector] _StencilRef("_StencilRef", Int) = 0
        [HideInInspector] _StencilWriteMask("_StencilWriteMask", Int) = 6
        [HideInInspector] _StencilRefDepth("_StencilRefDepth", Int) = 0
        [HideInInspector] _StencilWriteMaskDepth("_StencilWriteMaskDepth", Int) = 8
        [HideInInspector] _StencilRefGBuffer("_StencilRefGBuffer", Int) = 2
        [HideInInspector] _StencilWriteMaskGBuffer("_StencilWriteMaskGBuffer", Int) = 14
        [HideInInspector] _ZTestGBuffer("_ZTestGBuffer", Int) = 3
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
    #include "HillGrassProperties.hlsl"
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
            #define SHADERPASS SHADERPASS_DEPTH_ONLY
            #define SCENESELECTIONPASS
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/PickingSpaceTransforms.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Material.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/Lit.hlsl"
            #include "HillGrassPass.hlsl"
            #include "HillGrassData.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/ShaderPassDepthOnly.hlsl"
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma editor_sync_compilation
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
            #define SHADERPASS SHADERPASS_SHADOWS
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Material.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/Lit.hlsl"
            #include "HillGrassPass.hlsl"
            #include "HillGrassData.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/ShaderPassDepthOnly.hlsl"
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }

        Pass
        {
            Name "GBuffer"
            Tags { "LightMode" = "GBuffer" }
            Cull [_CullMode]
            ZTest [_ZTestGBuffer]
            ZWrite Off      // depth is complete after the prepass: hidden shells are rejected before shading
            Stencil
            {
                WriteMask [_StencilWriteMaskGBuffer]
                Ref [_StencilRefGBuffer]
                Comp Always
                Pass Replace
            }

            HLSLPROGRAM
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
            #include "HillGrassPass.hlsl"
            #include "HillGrassData.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/ShaderPassGBuffer.hlsl"
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
            #pragma multi_compile _ WRITE_NORMAL_BUFFER
            #pragma multi_compile_fragment _ WRITE_MSAA_DEPTH
            #define SHADERPASS SHADERPASS_DEPTH_ONLY
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Material.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/Lit.hlsl"
            #include "HillGrassPass.hlsl"
            #include "HillGrassData.hlsl"
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
            ZWrite Off
            Cull [_CullMode]

            HLSLPROGRAM
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
            #include "HillGrassPass.hlsl"
            #include "HillGrassData.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/ShaderPassForward.hlsl"
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }
    }

    FallBack "Hidden/HDRP/FallbackError"
}
