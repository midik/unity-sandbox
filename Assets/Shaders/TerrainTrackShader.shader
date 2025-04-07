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
        Tags { "RenderType"="Opaque" }
        LOD 200

        CGPROGRAM
        // Physically based Standard lighting model, and enable shadows on all light types
        #pragma surface surf Standard fullforwardshadows

        // Use shader model 3.0 for better performance
        #pragma target 3.0

        sampler2D _MainTex;
        sampler2D _TrackTex;

        struct Input
        {
            float2 uv_MainTex;
            float2 uv_TrackTex;
            float4 color : COLOR;  // Vertex colors from mesh
        };

        half _Glossiness;
        half _Metallic;

        // Add instancing support for this shader
        #pragma instancing_options assumeuniformscaling
        UNITY_INSTANCING_BUFFER_START(Props)
        UNITY_INSTANCING_BUFFER_END(Props)

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            // Sample the terrain texture
            fixed4 terrainColor = tex2D(_MainTex, IN.uv_MainTex);
            
            // Sample the track texture
            fixed4 trackColor = tex2D(_TrackTex, IN.uv_TrackTex);
            
            // Use the red channel of vertex color as the blend factor
            float trackBlend = IN.color.r;
            
            // Blend between terrain and track textures
            fixed4 finalColor = lerp(terrainColor, trackColor, trackBlend);
            
            o.Albedo = finalColor.rgb;
            o.Metallic = _Metallic;
            o.Smoothness = _Glossiness;
            o.Alpha = finalColor.a;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
