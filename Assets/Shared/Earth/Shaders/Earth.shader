// Earth (Assets/Shared/Earth): an HDRP Lit standard surface (sun, shadows, glint, GI) on HDRP's own Lit passes, with
// a hand-written surface (EarthData.hlsl): day map, clouds, ocean / land / cloud smoothness, city lights on the night
// side and an atmosphere rim. The globe and the sun direction come from EarthGlobe.cs. Opaque only, no SSR, no decals.
Shader "Soledad/Earth"
{
    Properties
    {
        [Header(Ground)]
        [NoScaleOffset] _DayMap("Day Map (no clouds)", 2D) = "gray" {}
        _DayTint("Day Tint", Color) = (1, 1, 1, 1)
        [NoScaleOffset] _WaterMask("Water Mask (1 = sea)", 2D) = "black" {}
        _LandSmoothness("Land Smoothness", Range(0, 1)) = 0.25
        _OceanSmoothness("Ocean Smoothness", Range(0, 1)) = 0.78

        [Header(Clouds)]
        [NoScaleOffset] _CloudMap("Cloud Map", 2D) = "black" {}
        _CloudColor("Cloud Colour", Color) = (0.96, 0.97, 1, 1)
        _CloudOpacity("Cloud Opacity", Range(0, 2)) = 1
        _CloudSmoothness("Cloud Smoothness", Range(0, 1)) = 0.05

        [Header(City Lights (night side))]
        [NoScaleOffset] _NightMap("Night Lights Map", 2D) = "black" {}
        _CityLightsColor("Lights Colour", Color) = (1, 0.78, 0.5, 1)
        _CityLightsIntensity("Lights Intensity", Float) = 3
        _CityLightsBlackLevel("Map Black Level", Range(0, 0.5)) = 0.1
        _CityLightsUnderClouds("Hidden By Clouds", Range(0, 1)) = 0.8
        _TerminatorStart("Lights Start (sun height)", Range(-0.5, 0.5)) = 0.05
        _TerminatorEnd("Lights Full (sun height)", Range(-0.5, 0.5)) = -0.12
        _CityLightsExposureWeight("Emission Exposure Weight (0 = ignore exposure)", Range(0, 1)) = 0

        [Header(Atmosphere Rim (day side))]
        _AtmosphereColor("Atmosphere Colour", Color) = (0.35, 0.6, 1, 1)
        _AtmosphereIntensity("Atmosphere Intensity", Float) = 0.6
        _AtmospherePower("Atmosphere Thinness", Range(1, 16)) = 5

        [Header(Set by EarthGlobe)]
        _CloudOffset("Cloud Drift (turns)", Float) = 0
        _EarthSunDirection("Sun Direction (world)", Vector) = (0, 0, 1, 0)

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
    #include "EarthProperties.hlsl"
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
            #include "EarthPass.hlsl"
            #include "EarthData.hlsl"
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
            #include "EarthPass.hlsl"
            #include "EarthData.hlsl"
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
            #include "EarthPass.hlsl"
            #include "EarthData.hlsl"
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
            #include "EarthPass.hlsl"
            #include "EarthData.hlsl"
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
            #include "EarthPass.hlsl"
            #include "EarthData.hlsl"
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
            #include "EarthPass.hlsl"
            #include "EarthData.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/ShaderPassForward.hlsl"
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }
    }

    FallBack "Hidden/HDRP/FallbackError"
}
