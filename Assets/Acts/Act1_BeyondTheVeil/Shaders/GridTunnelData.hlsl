// Surface of Soledad/GridTunnel: anti-aliased grid lines and lit "windows" in the cells.
// uv0 = (column on the wall, row along the tunnel) in cells, before deformation, so the grid follows the noise.
// uv1.x = wall index (0 floor, 1 right, 2 ceiling, 3 left).

#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/BuiltinUtilities.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/MaterialUtilities.hlsl"

// Coverage of the two line families: x = rails (constant column), y = rings (constant row)
float2 GT_GridLines(float2 uv, float lineWidth)
{
    // "Pristine grid" (Ben Golus): keeps lines thin and stable down to the vanishing point
    float2 uvDeriv = max(fwidth(uv), 1e-5);
    float2 drawWidth = clamp(lineWidth, uvDeriv, 0.5);
    float2 lineAA = uvDeriv * 1.5;
    float2 gridUV = 1.0 - abs(frac(uv) * 2.0 - 1.0);
    float2 grid2 = smoothstep(drawWidth + lineAA, drawWidth - lineAA, gridUV);
    grid2 *= saturate(lineWidth / drawWidth);
    grid2 = lerp(grid2, lineWidth, saturate(uvDeriv * 2.0 - 1.0));
    return grid2;
}

// Order of a line in its family, 0 to 1: u is its natural place (rails: around the tunnel from the middle of the
// floor; rings: distance from the camera over the reveal length)
float GT_LineOrder(float u, float order, uint key)
{
    if (order < 0.5) return u;                                  // Chase
    if (order < 1.5) return 1.0 - u;                            // Reverse
    if (order < 2.5) return GT_Hash3(uint3(key, 0x68e31da4u, 7u)).x;   // Random
    return min(u, 1.0 - u) * 2.0;                               // Mirror: both ends first
}

// Length (m) a line has grown to, from its family's ages, its delay and the growth speed
float GT_LineLength(float4 grow, float id, float maxLength)
{
    float delay = grow.w * id;
    float speed = grow.z > 0.0 ? grow.z : 1e6;                  // 0 = appear at once
    if (grow.y < 0.0)
        return clamp((grow.x - delay) * speed, 0.0, maxLength);
    float atHide = clamp((grow.x - grow.y - delay) * speed, 0.0, maxLength);
    return max(0.0, atHide - max(0.0, grow.y - delay) * speed);
}

// Light effect on a line, like Blux effects: the phase runs with time, offset by the line's order
float GT_LineEffect(float4 fx, float id, float minimum, uint key)
{
    int type = (int)round(fx.y);
    if (type == 0) return 1.0;
    float phase = _ShowTime * fx.z - fx.w * id;
    float e;
    if (type == 1) e = 0.5 + 0.5 * sin(phase * 6.2831853);                          // Sine
    else if (type == 2) e = saturate(GT_GradientNoise(float3(phase * 2.0, (key % 997u) * 0.37, 0.5)) * 0.7 + 0.5); // Noise
    else if (type == 3) e = frac(phase) < 0.5 ? 1.0 : 0.0;                            // Pulse
    else e = 1.0 - frac(phase);                                                       // Saw
    return lerp(minimum, 1.0, e);
}

float GT_Window(float2 uv)
{
    float2 uvDeriv = max(fwidth(uv), 1e-5);
    float2 f = frac(uv);
    float2 edge = min(f, 1.0 - f);
    float2 w = saturate((edge - 0.04) / max(0.01, uvDeriv));
    return w.x * w.y;
}

// Random blink level of a cell, 0 to 1 (already multiplied by its intensity)
float GT_RandomLevel(uint2 key)
{
    if (_RandomDensity <= 0.0)
        return 0.0;

    uint seed = (uint)(int)_RandomSeed;
    float3 h = GT_Hash3(uint3(key, seed));

    if (_RandomRate <= 0.0)
    {
        // Static pattern: raising the density only adds cells
        return (h.z < _RandomDensity) ? _RandomIntensity : 0.0;
    }

    float rate = _RandomRate * max(0.0, 1.0 + _RandomRateJitter * (h.y * 2.0 - 1.0));
    float t = _BlinkTime * rate + h.x;
    float tick = floor(t);
    float local = t - tick;
    float3 ht = GT_Hash3(uint3(key.x, key.y ^ 0x5bd1e995u, seed + (uint)tick * 0x9E3779B9u));
    if (ht.x >= _RandomDensity)
        return 0.0;

    float half_ = _RandomFade * 0.5;
    float env = (half_ > 0.0) ? smoothstep(0.0, half_, local) * smoothstep(1.0, 1.0 - half_, local) : 1.0;
    return env * _RandomIntensity;
}

void GetSurfaceAndBuiltinData(FragInputs input, float3 V, inout PositionInputs posInput, out SurfaceData surfaceData, out BuiltinData builtinData RAY_TRACING_OPTIONAL_PARAMETERS)
{
    float2 uv = input.texCoord0.xy;
    uv.y += _TunnelOffset.z;                    // fractional part of the row offset (keeps precision)
    int wall = (int)round(input.texCoord1.x);
    int column = (int)floor(uv.x);
    int row = (int)floor(uv.y) + (int)_TunnelOffset.y;

    // Grid lines, each family revealed and lit by its appearance settings
    float2 lines = GT_GridLines(uv, _LineWidth);
    float cellsW = _GridInfo.x, cellsH = _GridInfo.y, cell = _GridInfo.z;
    float perimeter = 2.0 * (cellsW + cellsH);
    float wallStart = wall == 0 ? 0.0 : wall == 1 ? cellsW : wall == 2 ? cellsW + cellsH : 2.0 * cellsW + cellsH;

    float railIndex = fmod(wallStart + round(uv.x), perimeter);
    uint railKey = (uint)railIndex;
    float railId = GT_LineOrder(frac((railIndex - cellsW * 0.5) / perimeter + 1.0), _RailsFx.x, railKey);
    float railLength = GT_LineLength(_RailsGrow, railId, _GridInfo.w + cell);
    float alongTunnel = abs(uv.y - _CameraRow) * cell;
    float rails = lines.x * saturate((railLength - alongTunnel) / (0.3 * cell)) * GT_LineEffect(_RailsFx, railId, _LinesFxMin.x, railKey);

    float ringRow = round(uv.y);
    uint ringKey = asuint((int)ringRow + (int)_TunnelOffset.y);
    float ringId = GT_LineOrder(saturate(abs(ringRow - _CameraRow) * cell / max(_GridInfo.w, 1e-3)), _RingsFx.x, ringKey);
    float ringLength = GT_LineLength(_RingsGrow, ringId, perimeter * 0.5 * cell + cell);
    float around = abs(wallStart + uv.x - cellsW * 0.5);
    around = min(around, perimeter - around) * cell;
    float rings = lines.y * saturate((ringLength - around) / (0.3 * cell)) * GT_LineEffect(_RingsFx, ringId, _LinesFxMin.y, ringKey);

    float grid = rails + rings - rails * rings;
    float window = GT_Window(uv);

    bool solo = _SoloCell.w > 0.5 && wall == (int)round(_SoloCell.x) && column == (int)round(_SoloCell.y) && row == (int)round(_SoloCell.z);
    uint2 key = uint2((uint)column | ((uint)wall << 16), asuint(row));
    float randomLevel = solo ? 0.0 : GT_RandomLevel(key);
    float soloLevel = solo ? _SoloLevel : 0.0;

    float dist = length(GetPrimaryCameraPosition() - input.positionRWS);
    float fade = 1.0 - smoothstep(_FadeStart, max(_FadeEnd, _FadeStart + 1e-3), dist);

    float3 lit = _LineColor.rgb * grid + window * (_WindowColor.rgb * randomLevel + _SoloColor.rgb * soloLevel);
    float3 emissive = _BaseColor.rgb + lit * fade;

    surfaceData.color = 0.0;
    surfaceData.normalWS = 0.0;
#if defined(_ENABLE_SHADOW_MATTE) && (SHADERPASS == SHADERPASS_PATH_TRACING)
    surfaceData.shadowTint = 0.0;
#endif

    ZERO_BUILTIN_INITIALIZE(builtinData);
    builtinData.opacity = 1.0;
#if defined(DEBUG_DISPLAY)
    builtinData.renderingLayers = GetMeshRenderingLayerMask();
#endif
    // Fixed screen brightness: cancel the camera exposure
    builtinData.emissiveColor = emissive * GetInverseCurrentExposureMultiplier();

    ApplyDebugToBuiltinData(builtinData);
}
