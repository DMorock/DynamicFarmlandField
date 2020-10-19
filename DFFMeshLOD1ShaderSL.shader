// ************************************************************************************************
// Created by dmorock
// ************************************************************************************************
Shader "Custom/DFFMeshLOD1ShaderSimpleLit"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}		
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200

        Pass
        {
			Tags { "LightMode" = "Deferred" }
		
			CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            // make fog work
            #pragma multi_compile_fog
			// deffered lighting on
			#pragma multi_compile _ UNITY_HDR_ON
			// instancing on
			#pragma multi_compile_instancing

			#include "UnityCG.cginc"
			#include "UnityLightingCommon.cginc" // for _LightColor0

            struct v2f
            {
				float4 pos : SV_POSITION;
				float2 uv : TEXCOORD0;
				half3 worldNormal: TEXCOORD1;
				//UNITY_FOG_COORDS(1)
				UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;

            v2f vert (appdata_full v)
            {
                v2f o;
				UNITY_SETUP_INSTANCE_ID(v);
				UNITY_TRANSFER_INSTANCE_ID(v, o);
				o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.texcoord, _MainTex);
				o.worldNormal = UnityObjectToWorldNormal(v.normal);
                UNITY_TRANSFER_FOG(o,o.vertex);
                return o;
            }
			
			UNITY_INSTANCING_BUFFER_START(Props)
				UNITY_DEFINE_INSTANCED_PROP(half3, _Color)
			UNITY_INSTANCING_BUFFER_END(Props)
            void frag (v2f i,
				out half4 outDiffuse : SV_Target0,           // RT0: diffuse color (rgb), occlusion (a)
				out half4 outSpecSmoothness : SV_Target1,    // RT1: spec color (rgb), smoothness (a)
				out half4 outNormal : SV_Target2,            // RT2: normal (rgb), --unused, very low precision-- (a)
				out half4 outEmission : SV_Target3           // RT3: emission (rgb), --unused-- (a)
			)// : SV_Target
            {
				UNITY_SETUP_INSTANCE_ID(i);
				fixed4 col = tex2D(_MainTex, i.uv);
				outDiffuse.rgb = tex2D(_MainTex, i.uv).rgb*UNITY_ACCESS_INSTANCED_PROP(Props, _Color).rgb;
				outDiffuse.a = col.a;
                
				outSpecSmoothness = half4(0.117, 0.113, 0.114, 0.1857f);
				outNormal = half4 (i.worldNormal, 1.0f);
				
				// support for deferred HDR
#ifndef UNITY_HDR_ON
				outDiffuse.rgb = exp2(-outDiffuse.rgb);
#endif
				// write "unlit" color to emission buffer, which ends up being the frame buffer
				outEmission = half4(outDiffuse.rgb*_LightColor0, 1);
				// apply fog
                UNITY_APPLY_FOG(i.fogCoord, outDiffuse);
                //return col;
            }
            ENDCG
        }
	
		// shadow casting support
		UsePass "Legacy Shaders/VertexLit/SHADOWCASTER"
    }
}
