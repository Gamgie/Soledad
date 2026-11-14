// Cubic cloud of Act 2: a fake volumetric cloud on Unity's cube mesh (CloudCubeData.hlsl). HDRP Lit transparent
// forward pass (sun, sky light, shadows received, fog), a shadow caster (the cube core) and scene selection.
// Back faces are drawn so the cloud still shows when the camera is inside its hull.
Shader "Soledad/CloudCube"
{
    Properties
    {
        _CloudColor("Colour", Color) = (1, 1, 1, 1)
        _CloudDensity("Density (per m)", Range(0.001, 1)) = 0.06
        _CloudFluff("Fluff (m)", Range(0, 100)) = 9
        _CloudDent("Dents Into The Cube", Range(0, 1)) = 0.3
        _CloudNoiseSize("Puff Size (m)", Range(0.5, 200)) = 14
        _CloudScaleWithSize("Scale With Size", Range(0, 1)) = 1
        _CloudReferenceSize("Reference Cube Size (m)", Float) = 40
        _CloudSoftness("Edge Softness (m)", Range(0.1, 60)) = 4
        _CloudRoundness("Corner Radius (m)", Range(0, 100)) = 3
        _CloudSteps("Samples", Range(4, 96)) = 28
        _CloudScatter("Subsurface Glow", Range(0, 2)) = 0.35
        _CloudScatterPhase("Glow Focus Towards Sun", Range(0, 1)) = 0.6
        _CloudPuffShading("Puff Shading", Range(0, 1)) = 0.55
        _CloudShadeRounding("Soft Edge Shading", Range(0, 1)) = 0.7
        _CloudSmoothness("Smoothness", Range(0, 1)) = 0.05
        _CloudShadowCore("Shadow Size", Range(0.3, 1)) = 0.8
        [HideInInspector] _CloudSeed("Seed", Float) = 0

        [HideInInspector] _BlendMode("__blendmode", Float) = 0           // alpha (premultiplied by the shader)
        [HideInInspector] _CullMode("__cullmode", Float) = 2.0
        [HideInInspector] _ZClip("_ZClip", Float) = 1.0
    }

    HLSLINCLUDE
    #pragma target 4.5
    #pragma only_renderers d3d11 playstation xboxone xboxseries vulkan metal switch switch2

    #define _SURFACE_TYPE_TRANSPARENT
    #define _ENABLE_FOG_ON_TRANSPARENT
    #define _DISABLE_SSR
    #define _DISABLE_SSR_TRANSPARENT
    #define PREFER_HALF 0

    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/FragInputs.hlsl"
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/ShaderPass.cs.hlsl"
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/LitProperties.hlsl"
    #include "CloudCubeProperties.hlsl"
    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "HDLitShader" "Queue" = "Transparent" }

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
            #include "CloudCubePass.hlsl"
            #include "CloudCubeData.hlsl"
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
            Cull Back
            ZClip [_ZClip]
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma multi_compile_instancing
            #define SHADERPASS SHADERPASS_SHADOWS
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Material.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/Lit.hlsl"
            #include "CloudCubePass.hlsl"
            #include "CloudCubeData.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/ShaderPassDepthOnly.hlsl"
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "Forward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Front

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
            #include "CloudCubePass.hlsl"
            #include "CloudCubeData.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/ShaderPassForward.hlsl"
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }
    }

    FallBack "Hidden/HDRP/FallbackError"
}
