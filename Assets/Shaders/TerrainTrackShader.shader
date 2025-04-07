Shader "Custom/TerrainTrackShader"
{
    Properties
    {
        _MainTex ("Terrain Texture", 2D) = "white" {}
        _TrackTex ("Track Texture", 2D) = "white" {}
        _Glossiness ("Smoothness", Range(0,1)) = 0.5
        _Metallic ("Metallic", Range(0,1)) = 0.0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 200

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                float3 normalWS : NORMAL;
                float3 positionWS : TEXCOORD1;
            };

            TEXTURE2D(_MainTex);
            TEXTURE2D(_TrackTex);
            SAMPLER(sampler_MainTex);
            SAMPLER(sampler_TrackTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _TrackTex_ST;
                half _Glossiness;
                half _Metallic;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;

                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS);

                output.positionCS = vertexInput.positionCS;
                output.positionWS = vertexInput.positionWS;
                output.normalWS = normalInput.normalWS;
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.color = input.color;
                
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // Sample textures
                half4 terrainColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                half4 trackColor = SAMPLE_TEXTURE2D(_TrackTex, sampler_TrackTex, input.uv);

                // Blend using vertex color red channel
                half4 albedo = lerp(terrainColor, trackColor, input.color.r);

                // Get main light
                Light mainLight = GetMainLight();

                // Simple lighting calculation
                half3 normalWS = normalize(input.normalWS);
                half NdotL = saturate(dot(normalWS, mainLight.direction));
                half3 ambient = SampleSH(normalWS);
                
                half3 finalColor = (ambient + mainLight.color * NdotL) * albedo.rgb;

                return half4(finalColor, 1);
            }
            ENDHLSL
        }
    }
}