// Screen-space volumetric light shafts ("crepuscular rays") — classic GPU Gems-style radial
// scattering: build a mask of unoccluded, bright sky pixels from the depth buffer, then repeatedly
// sample that mask toward the sun's screen position with decaying weight so light "streaks" out
// from behind whatever occludes it (trees, rooftops, terrain). Runs as a URP Full Screen Pass
// Renderer Feature — GodRaySunTracker.cs updates _SunScreenPos/_SunVisibility every frame.
Shader "Darclite/GodRays"
{
    Properties
    {
        _SunScreenPos("Sun Screen Position", Vector) = (0.5, 0.5, 0, 0)
        _SunVisibility("Sun Visibility", Range(0, 1)) = 1
        _RayColor("Ray Color", Color) = (1, 0.92, 0.75, 1)
        _RayIntensity("Ray Intensity", Range(0, 1.5)) = 0.4
        _RayDecay("Ray Decay", Range(0.8, 0.995)) = 0.96
        _RayWeight("Ray Weight", Range(0, 0.3)) = 0.06
        _RayDensity("Ray Density", Range(0.1, 2)) = 1
        _RayContrast("Ray Separation (contrast)", Range(1, 6)) = 3
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off
        Cull Off

        Pass
        {
            Name "GodRays"

            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            #pragma vertex Vert
            #pragma fragment Frag

            #define NUM_SAMPLES 24

            float4 _SunScreenPos;
            float _SunVisibility;
            float4 _RayColor;
            float _RayIntensity;
            float _RayDecay;
            float _RayWeight;
            float _RayDensity;
            float _RayContrast;

            // Returns a bounded [0,1] "can this pixel see bright sky" value — deliberately NOT the
            // raw HDR scene color. This pass runs before tonemapping, where sky/sun pixels can be
            // far brighter than 1.0; accumulating that raw brightness over 24 samples has no
            // ceiling and can blow the whole screen out to white. Saturating per-tap keeps the
            // final ray color fully controlled by _RayColor/_RayIntensity regardless of how bright
            // the HDR sky actually renders.
            half SampleSkyMask(float2 uv)
            {
                float rawDepth = SampleSceneDepth(uv);
                float linear01 = Linear01Depth(rawDepth, _ZBufferParams);
                half skyMask = step(0.9999, linear01);
                half3 color = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, 0).rgb;
                half brightness = saturate(dot(color, half3(0.333h, 0.333h, 0.333h)));
                return skyMask * brightness;
            }

            float4 Frag(Varyings input) : SV_Target0
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord.xy;

                half3 sceneColor = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, 0).rgb;

                if (_SunVisibility <= 0.0001)
                {
                    return half4(sceneColor, 1);
                }

                float2 sunUV = _SunScreenPos.xy;
                float2 delta = (uv - sunUV) * (_RayDensity / NUM_SAMPLES);

                float2 sampleUV = uv;
                half accumulated = 0;
                half illuminationDecay = 1.0h;

                [unroll]
                for (int i = 0; i < NUM_SAMPLES; i++)
                {
                    sampleUV -= delta;
                    accumulated += SampleSkyMask(sampleUV) * illuminationDecay * _RayWeight;
                    illuminationDecay *= _RayDecay;
                }

                // A raw linear accumulation lights up almost anywhere with even partial sky along
                // the sample path, which reads as one smooth wash rather than distinct rays. Raising
                // it to a power punches up paths that are mostly clear and suppresses the partial/
                // hazy in-between ones, which is what actually separates it into visible shafts.
                half separated = pow(saturate(accumulated), _RayContrast);

                // Hard ceiling — even in a worst case (e.g. every sample reads as sky), this can
                // never add more than _RayColor * _RayIntensity on top of the scene.
                half3 rays = separated * _RayColor.rgb * _RayIntensity * _SunVisibility;
                return half4(sceneColor + rays, 1);
            }
            ENDHLSL
        }
    }
}
