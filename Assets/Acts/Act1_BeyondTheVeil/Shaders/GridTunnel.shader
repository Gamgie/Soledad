// Grid tunnel of Act 1 (Beyond The Veil): a dense box tunnel, deformed by a fractal noise in the vertex
// stage, drawn as thin grid lines with lit "windows" that blink. Unlit, built on HDRP's own passes
// (depth prepass, forward unlit, scene selection) so depth, fog, exposure, bloom and the dome cameras work.
// Driven by GridTunnel.cs.
Shader "Soledad/GridTunnel"
{
    Properties
    {
        [Header(Look)]
        _BaseColor("Background", Color) = (0, 0, 0, 1)
        [HDR] _LineColor("Line Color", Color) = (1, 1, 1, 1)
        _LineWidth("Line Width (cells)", Range(0, 0.5)) = 0.02
        [HDR] _WindowColor("Window Color", Color) = (1, 0.85, 0.6, 1)
        [HDR] _SoloColor("Solo Window Color", Color) = (1, 0.85, 0.6, 1)
        _FadeStart("Fade Start (m)", Float) = 60
        _FadeEnd("Fade End (m)", Float) = 180

        [Header(Random Blink)]
        _RandomDensity("Density", Range(0, 1)) = 0
        _RandomRate("Rate (ticks per s)", Float) = 1
        _RandomRateJitter("Rate Jitter", Range(0, 1)) = 0.5
        _RandomFade("Fade", Range(0, 1)) = 0.3
        _RandomIntensity("Intensity", Float) = 1
        _RandomSeed("Seed", Float) = 0

        [Header(Solo Window)]
        _SoloCell("Cell (wall, column, row, enabled)", Vector) = (0, 0, 0, 0)
        _SoloLevel("Level", Range(0, 1)) = 1

        [Header(Time and Offsets)]
        _BlinkTime("Blink Time", Float) = 0
        _NoiseTime("Noise Time", Float) = 0
        _TunnelOffset("Tunnel Offset (z, row int, row frac)", Vector) = (0, 0, 0, 0)

        [Header(Line Appearance)]
        _ShowTime("Show Time", Float) = 0
        _CameraRow("Camera Row", Float) = 0
        _GridInfo("Grid (width, height, cell size, rail reveal)", Vector) = (12, 12, 1, 180)
        _RailsGrow("Rails Grow (reveal, stagger)", Vector) = (1, 0.5, 0, 0)
        _RailsFx("Rails Effect (order, type, speed, spread)", Vector) = (0, 0, 0.5, 1)
        _RingsGrow("Rings Grow (reveal, stagger)", Vector) = (1, 0.5, 0, 0)
        _RingsFx("Rings Effect (order, type, speed, spread)", Vector) = (0, 0, 0.5, 1)
        _LinesFxMin("Effect Minimum (rails, rings)", Vector) = (0, 0, 0, 0)

        [Header(Noise)]
        _NoiseAmplitude("Amplitude (m)", Float) = 0
        _NoisePeriod("Period (m)", Float) = 8
        _NoiseOctaves("Harmonics", Range(1, 8)) = 3
        _NoiseRoughness("Roughness", Range(0, 1)) = 0.5
        _NoiseLacunarity("Spread", Range(1, 4)) = 2
        _NoiseStretch("Stretch Along Tunnel", Float) = 1
        _NoiseRadial("Radial", Range(0, 1)) = 1
        _NoiseFlow("Flow Direction", Vector) = (0.13, 0.07, 0.21, 0)
        _NoiseSeed("Seed", Float) = 0

        // Off by default: the camera is always inside, back faces cost nothing visible
        [Enum(UnityEngine.Rendering.CullMode)] _CullMode("Cull Mode", Float) = 0
    }

    HLSLINCLUDE
    #pragma target 4.5
    #pragma only_renderers d3d11 playstation xboxone xboxseries vulkan metal switch switch2

    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/FragInputs.hlsl"
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/ShaderPass.cs.hlsl"
    #include "GridTunnelProperties.hlsl"
    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "HDUnlitShader" "Queue" = "Geometry" }

        Pass
        {
            Name "SceneSelectionPass"
            Tags { "LightMode" = "SceneSelectionPass" }
            Cull Off
            ZWrite On

            HLSLPROGRAM
            #pragma multi_compile_instancing
            #define SHADERPASS SHADERPASS_DEPTH_ONLY
            #define SCENESELECTIONPASS
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/PickingSpaceTransforms.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Material.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Unlit/Unlit.hlsl"
            #include "GridTunnelPass.hlsl"
            #include "GridTunnelData.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/ShaderPassDepthOnly.hlsl"
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma editor_sync_compilation
            ENDHLSL
        }

        Pass
        {
            Name "DepthForwardOnly"
            Tags { "LightMode" = "DepthForwardOnly" }
            Stencil
            {
                WriteMask 8
                Ref 0
                Comp Always
                Pass Replace
            }
            Cull [_CullMode]
            ZWrite On

            HLSLPROGRAM
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ WRITE_MSAA_DEPTH
            #define SHADERPASS SHADERPASS_DEPTH_ONLY
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Material.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Unlit/Unlit.hlsl"
            #include "GridTunnelPass.hlsl"
            #include "GridTunnelData.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/ShaderPassDepthOnly.hlsl"
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }

        Pass
        {
            Name "ForwardOnly"
            Tags { "LightMode" = "ForwardOnly" }
            Blend One Zero
            Blend 1 SrcAlpha OneMinusSrcAlpha
            ZWrite On
            ZTest LEqual
            Stencil
            {
                WriteMask 3
                Ref 0
                Comp Always
                Pass Replace
            }
            Cull [_CullMode]

            HLSLPROGRAM
            #pragma multi_compile_instancing
            #pragma multi_compile _ DEBUG_DISPLAY
            #ifdef DEBUG_DISPLAY
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Debug/DebugDisplay.hlsl"
            #endif
            #define SHADERPASS SHADERPASS_FORWARD_UNLIT
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Material.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Unlit/Unlit.hlsl"
            #include "GridTunnelPass.hlsl"
            #include "GridTunnelData.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/ShaderPassForwardUnlit.hlsl"
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }
    }

    FallBack "Hidden/HDRP/FallbackError"
}
