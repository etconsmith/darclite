Shader "Darclite/AshenLit"
{
    Properties
    {
        _BaseMap("Base Map", 2D) = "white" {}
        _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        _Desaturation("Desaturation", Range(0, 1)) = 0.6
        _ShadowColor("Shadow Tint", Color) = (0.55, 0.55, 0.68, 1)
        _WrapAmount("Diffuse Wrap (softness)", Range(0, 1)) = 0.5
        _RimColor("Rim Color", Color) = (1, 1, 0.95, 1)
        _RimPower("Rim Power", Range(0.1, 8)) = 3
        _RimStrength("Rim Strength", Range(0, 1)) = 0.25
        _FlashColor("Flash Color", Color) = (1, 0, 0, 1)
        _FlashAmount("Flash Amount", Range(0, 1)) = 0

        [Space(10)]
        _Metallic("Metallic", Range(0, 1)) = 0
        _Smoothness("Smoothness", Range(0, 1)) = 0.15
        _SpecColor("Specular Color", Color) = (0.2, 0.2, 0.2, 1)
        [Normal] _BumpMap("Normal Map", 2D) = "bump" {}
        _BumpScale("Normal Scale", Range(0, 2)) = 1
        _OcclusionMap("Occlusion Map", 2D) = "white" {}
        _OcclusionStrength("Occlusion Strength", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _FORWARD_PLUS
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                half4 tangentWS : TEXCOORD3;
                float4 shadowCoord : TEXCOORD4;
                float fogFactor : TEXCOORD5;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BumpMap);
            SAMPLER(sampler_BumpMap);
            TEXTURE2D(_OcclusionMap);
            SAMPLER(sampler_OcclusionMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float _Desaturation;
                float4 _ShadowColor;
                float _WrapAmount;
                float4 _RimColor;
                float _RimPower;
                float _RimStrength;
                float4 _FlashColor;
                float _FlashAmount;
                float _Metallic;
                float _Smoothness;
                float4 _SpecColor;
                float _BumpScale;
                float _OcclusionStrength;
            CBUFFER_END

            // Soft wrap-around diffuse instead of a hard toon band — this is the core of the
            // "sculptural"/Ashen look: light rolls off in a smooth, forgiving gradient rather than
            // a stark lit/shadow split, so forms read through soft shading instead of flat regions
            // separated by a sharp line. Specular is kept deliberately faint — these are meant to
            // read as matte, clay-like surfaces, not shiny plastic.
            half3 ShadeLight(Light light, float3 normalWS, float3 viewDir, half3 diffuseAlbedo, half3 specTint, half3 ambient)
            {
                float NdotL = dot(normalWS, light.direction);
                float wrap = saturate((NdotL + _WrapAmount) / (1.0 + _WrapAmount));
                float attenuation = light.distanceAttenuation * light.shadowAttenuation;

                half3 shadowTerm = _ShadowColor.rgb * diffuseAlbedo * ambient;
                half3 litTerm = diffuseAlbedo * light.color * light.distanceAttenuation;
                half3 diffuse = lerp(shadowTerm, litTerm, wrap * attenuation);

                half3 halfDir = normalize(light.direction + viewDir);
                half NdotH = saturate(dot(normalWS, halfDir));
                half specPower = exp2(6.0h * _Smoothness + 1.0h);
                half spec = pow(NdotH, specPower) * _Smoothness * 0.5h;

                return diffuse + spec * specTint * light.color * wrap * attenuation;
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs positions = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs normals = GetVertexNormalInputs(IN.normalOS, IN.tangentOS);

                OUT.positionHCS = positions.positionCS;
                OUT.positionWS = positions.positionWS;
                OUT.normalWS = normals.normalWS;
                OUT.tangentWS = half4(normals.tangentWS, IN.tangentOS.w * GetOddNegativeScale());
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.shadowCoord = GetShadowCoord(positions);
                OUT.fogFactor = ComputeFogFactor(positions.positionCS.z);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 normalWS = normalize(IN.normalWS);
                float3 tangentWS = normalize(IN.tangentWS.xyz);
                float3 bitangentWS = cross(normalWS, tangentWS) * IN.tangentWS.w;
                float3x3 tangentToWorld = float3x3(tangentWS, bitangentWS, normalWS);

                half3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, IN.uv), _BumpScale);
                normalWS = normalize(mul(normalTS, tangentToWorld));

                half occlusion = lerp(1.0h, SAMPLE_TEXTURE2D(_OcclusionMap, sampler_OcclusionMap, IN.uv).g, _OcclusionStrength);

                half4 texColor = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv) * _BaseColor;

                // Push toward a muted, near-monochrome palette — Ashen's sculptural look is built
                // on desaturated forms with lighting doing the work, not colorful surface detail.
                half luminance = dot(texColor.rgb, half3(0.2126h, 0.7152h, 0.0722h));
                half3 mutedAlbedo = lerp(texColor.rgb, luminance.xxx, _Desaturation);

                half3 diffuseAlbedo = mutedAlbedo * (1.0h - _Metallic);
                half3 specTint = lerp(_SpecColor.rgb, mutedAlbedo, _Metallic);
                half3 ambient = max(SampleSH(normalWS), 0.55h) * occlusion;

                float3 viewDir = normalize(GetWorldSpaceViewDir(IN.positionWS));

                Light mainLight = GetMainLight(IN.shadowCoord);
                half3 litColor = ShadeLight(mainLight, normalWS, viewDir, diffuseAlbedo, specTint, ambient);

            #if defined(_ADDITIONAL_LIGHTS)
                uint additionalLightsCount = GetAdditionalLightsCount();
                for (uint lightIndex = 0u; lightIndex < additionalLightsCount; lightIndex++)
                {
                    Light light = GetAdditionalLight(lightIndex, IN.positionWS);
                    litColor += ShadeLight(light, normalWS, viewDir, diffuseAlbedo, specTint, 0.0h);
                }
            #endif

                float rim = 1.0 - saturate(dot(viewDir, normalWS));
                rim = pow(rim, _RimPower) * _RimStrength;

                half3 finalColor = litColor + rim * _RimColor.rgb;
                finalColor = lerp(finalColor, _FlashColor.rgb, _FlashAmount);
                finalColor = MixFog(finalColor, IN.fogFactor);

                return half4(finalColor, texColor.a);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(IN.normalOS);

                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, _LightDirection));
                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, positionCS.w * UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, positionCS.w * UNITY_NEAR_CLIP_VALUE);
                #endif

                OUT.positionHCS = positionCS;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }
}
