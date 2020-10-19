Shader "Custom/DFFMeshLOD1Surface"
{
    Properties
    {
        _MainTex ("Albedo (RGB)", 2D) = "white" {}
    }
		SubShader{
		Tags { "LightMode" = "Deferred" }
		Tags { "RenderType" = "Opaque" }
			LOD 200
			CGPROGRAM

			//#pragma multi_compile_instancing	
			#pragma surface surf Standard fullforwardshadows
			//#pragma vertex vert
			#pragma target 3.0
			//#include "UnityCG.cginc"

			sampler2D _MainTex;
			struct Input {
				float2 uv_MainTex;
				float4 vertex : SV_POSITION;			
				UNITY_VERTEX_INPUT_INSTANCE_ID // necessary only if you want to access instanced properties in fragment Shader.
			};			
			UNITY_INSTANCING_BUFFER_START(Props)
				UNITY_DEFINE_INSTANCED_PROP(fixed4, _Color)
			UNITY_INSTANCING_BUFFER_END(Props)
			void surf(Input IN, inout SurfaceOutputStandard o){
				//UNITY_SETUP_INSTANCE_ID(IN);
				//fixed4 c;// = IN.color.rgb;//UNITY_ACCESS_INSTANCED_PROP(Props, _ColorAlbedo); //tex2D(_MainTex, IN.uv_MainTex)*UNITY_ACCESS_INSTANCED_PROP(Props, _ColorAlbedo);
				o.Albedo = UNITY_ACCESS_INSTANCED_PROP(Props, _Color)*tex2D(_MainTex, IN.uv_MainTex);//IN.color.rgb;//_ColorAlbedo.rgb;// half3(0, 1, 0);//IN.color;//  *c.rgb;
				o.Metallic = 0.5f;
				o.Smoothness = 0.5f;
				o.Alpha = 1;// c.a;				
			}
        ENDCG
    }
    FallBack "Diffuse"
}
//https://forum.unity.com/threads/drawmeshinstanced-option-to-provide-per-instance-texture-via-material-property-block.557020/