// ************************************************************************************************
// Created by dmorock on the base of the sejton shader.
// ************************************************************************************************

Shader "Custom/DFFPlainLOD4"
{
	Properties
	{
		_MainTex("Grass Texture", 2D) = "white" {}
		_MaskTexture("Mask Texture", 2D) = "white" {}
	}

		SubShader
		{
			Tags { "Queue" = "Geometry" "RenderType" = "Opaque" }

			// base pass
			Pass
			{
				//Tags { "LightMode" = "ForwardBase"}
				Tags { "LightMode" = "Deferred"}

				CGPROGRAM
					#pragma vertex vertexShader
					#pragma fragment fragmentShader
					#pragma geometry geometryShader

					#pragma multi_compile_fwdbase
					#pragma multi_compile_fog

					#pragma fragmentoption ARB_precision_hint_fastest
					// deffered lighting on
					#pragma multi_compile _ UNITY_HDR_ON

					#include "UnityCG.cginc"
					#include "AutoLight.cginc"

					struct VS_INPUT
					{
						float4 position : POSITION;
						float4 uv_Noise : TEXCOORD0;
						fixed sizeFactor : TEXCOORD1;
					};

			struct GS_INPUT
			{
				float4 worldPosition : TEXCOORD0;
				fixed2 parameters : TEXCOORD1;	// .x = noiseValue, .y = sizeFactor
				float3 color : COLOR0;
			};

			struct FS_INPUT
			{
				float4 pos	: SV_POSITION;		// has to be called this way because of unity MACRO for light
				float2 uv_MainTexture : TEXCOORD0;
				float4 tint : COLOR0;
				LIGHTING_COORDS(1, 2)
					UNITY_FOG_COORDS(3)
			};

			uniform sampler2D _MainTex, _MaskTexture;

			// for billboard

			uniform float4 _LightColor0;

			struct Point {
				float3			vertex;
				float2			size;
				float3			color;
			};

			StructuredBuffer<Point> points;

			// Vertex Shader ------------------------------------------------
			GS_INPUT vertexShader(VS_INPUT vIn, uint id : SV_VertexID, uint inst : SV_InstanceID)
			{
				GS_INPUT vOut;

				float4 vertex_position = float4(points[id].vertex, 1.0f);
				vOut.worldPosition = mul(unity_ObjectToWorld, vertex_position);
				vOut.parameters = points[id].size;
				vOut.color = points[id].color;

				return vOut;
			}


					// Geometry Shader -----------------------------------------------------
					[maxvertexcount(4)]
					void geometryShader(point GS_INPUT p[1], inout TriangleStream<FS_INPUT> triStream)
					{
						// billboard's size and color
						float2 size = p[0].parameters;//lerp(_MinSize, _MaxSize, noiseValue * sizeFactor);
						float4 tint;
						tint.xyz = p[0].color.xyz;//lerp(_HealthyColor, _DryColor, noiseValue);
						tint.a = 1.0f;

						// create billboard
						float4 v[4];
						v[0] = float4(p[0].worldPosition.x + size.x, p[0].worldPosition.y + size.y, p[0].worldPosition.z + size.x, 1.0f);
						v[1] = float4(p[0].worldPosition.x + size.x, p[0].worldPosition.y + size.y, p[0].worldPosition.z - size.x, 1.0f);
						v[2] = float4(p[0].worldPosition.x - size.x, p[0].worldPosition.y + size.y, p[0].worldPosition.z + size.x, 1.0f);
						v[3] = float4(p[0].worldPosition.x - size.x, p[0].worldPosition.y + size.y, p[0].worldPosition.z - size.x, 1.0f);

						// matrix to transfer vertices from world to screen space
						float4x4 vpMatrix = mul(mul(UNITY_MATRIX_VP, unity_ObjectToWorld), unity_WorldToObject);//UnityObjectToClipPos(unity_WorldToObject);

						FS_INPUT fIn;

						fIn.pos = mul(vpMatrix, v[0]);
						fIn.uv_MainTexture = float2(1.0f, 0.0f);
						fIn.tint = tint;
						TRANSFER_VERTEX_TO_FRAGMENT(fIn);
						UNITY_TRANSFER_FOG(fIn, fIn.pos);

						triStream.Append(fIn);

						fIn.pos = mul(vpMatrix, v[1]);
						fIn.uv_MainTexture = float2(1.0f, 1.0f);
						fIn.tint = tint;
						TRANSFER_VERTEX_TO_FRAGMENT(fIn);
						UNITY_TRANSFER_FOG(fIn, fIn.pos);

						triStream.Append(fIn);

						fIn.pos = mul(vpMatrix, v[2]);
						fIn.uv_MainTexture = float2(0.0f, 0.0f);
						fIn.tint = tint;
						TRANSFER_VERTEX_TO_FRAGMENT(fIn);
						UNITY_TRANSFER_FOG(fIn, fIn.pos);

						triStream.Append(fIn);

						fIn.pos = mul(vpMatrix, v[3]);
						fIn.uv_MainTexture = float2(0.0f, 1.0f);
						fIn.tint = tint;
						TRANSFER_VERTEX_TO_FRAGMENT(fIn);
						UNITY_TRANSFER_FOG(fIn, fIn.pos);

						triStream.Append(fIn);
					}

					// Fragment Shader -----------------------------------------------
					void fragmentShader(FS_INPUT fIn,
						out half4 outDiffuse : SV_Target0,           // RT0: diffuse color (rgb), occlusion (a)
						out half4 outSpecSmoothness : SV_Target1,    // RT1: spec color (rgb), smoothness (a)
						out half4 outNormal : SV_Target2,            // RT2: normal (rgb), --unused, very low precision-- (a)
						out half4 outEmission : SV_Target3)           // RT3: emission (rgb), --unused-- (a))// : COLOR
					{
						fixed4 color = tex2D(_MainTex, fIn.uv_MainTexture)*fIn.tint;

						float3 lightDirection = normalize(_WorldSpaceLightPos0.xyz);
						float atten = LIGHT_ATTENUATION(fIn);

						float3 ambient = UNITY_LIGHTMODEL_AMBIENT.xyz;
						float3 normal = float3(0, 1, 0);
						float3 lambert = float(max(0.0, dot(normal, lightDirection)));
						float3 lighting = (ambient + lambert * atten) * _LightColor0.rgb;

						color = fixed4(color.rgb * lighting, 1.0f);

						outDiffuse = color;
						outSpecSmoothness = half4(0.117, 0.113, 0.114, 0.1857f);
						outNormal = fixed4(normal, 1);
#ifndef UNITY_HDR_ON
						outDiffuse.rgb = exp2(-outDiffuse.rgb);
#endif
						// write "unlit" color to emission buffer, which ends up being the frame buffer
						outEmission = half4(outDiffuse.rgb*_LightColor0, 1);

						UNITY_APPLY_FOG(fIn.fogCoord, color);

						//return color;
					}
					/*
					// Fragment Shader -----------------------------------------------
					float4 fragmentShader(FS_INPUT fIn) : COLOR
					{
						fixed4 color = tex2D(_MainTex, fIn.uv_MainTexture) * fIn.tint;
						fixed4 amask = tex2D(_MaskTexture, fIn.uv_MainTexture);
						if (amask.r < 0.5f)
							discard;

						float3 lightDirection = normalize(_WorldSpaceLightPos0.xyz);
						float atten = LIGHT_ATTENUATION(fIn);

						float3 ambient = UNITY_LIGHTMODEL_AMBIENT.xyz;
						float3 normal = float3(0,1,0);
						float3 lambert = float(max(0.0,dot(normal,lightDirection)));
						float3 lighting = (ambient + lambert * atten) * _LightColor0.rgb;

						color = fixed4(color.rgb * lighting, amask.r);

						UNITY_APPLY_FOG(fIn.fogCoord, color);

						return color;
					}*/

				ENDCG
			}
		}

			FallBack "Diffuse"
}
