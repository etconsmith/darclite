Shader "Darclite/RimGlowShell"
{
    Properties
    {
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _RimPower ("Rim Power", Float) = 2.5
        _Intensity ("Intensity", Float) = 0
    }

    // Invisible face-on, blooms into a soft edge-light at grazing angles — meant to sit as a
    // slightly-inflated proxy shell around a limb, so the character's own silhouette appears to
    // radiate light instead of only nearby particles doing so. _Intensity is driven entirely by
    // script (LiteConcentrationAura) rather than a fixed value, so it can fade in/out with the rest
    // of the effect.
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend One One
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
            };

            float4 _Color;
            float _RimPower;
            float _Intensity;

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionHCS = TransformWorldToHClip(OUT.positionWS);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                return OUT;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                float3 viewDir = normalize(_WorldSpaceCameraPos - IN.positionWS);
                float rim = pow(saturate(1.0 - dot(normalize(IN.normalWS), viewDir)), _RimPower);
                float amount = rim * _Intensity;
                return float4(_Color.rgb * amount, amount);
            }
            ENDHLSL
        }
    }
}
