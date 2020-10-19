// ************************************************************************************************
// Created by dmorock on the base of the sejton shader.
// ************************************************************************************************

Shader "Custom/DFFBillboardLOD3"
{
	Properties
	{
		_MainTex("Grass Texture", 2D) = "white" {}
		//_NoiseTexture("Noise Texture", 2D) = "white" {}
		_MaxCameraDistance("Max Camera Distance", float) = 250
		_Transition("Transition", float) = 30
		_Cutoff("Alpha Cutoff", Range(0,1)) = 0.1
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
						LIGHTING_COORDS(1,2)
						UNITY_FOG_COORDS(3)
					};

					uniform sampler2D _MainTex;// , _NoiseTexture;

					// for billboard
					uniform fixed _Cutoff;
					uniform float _MaxCameraDistance;
					uniform float _Transition;

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
						vOut.parameters.y = vOut.parameters.y * 2.0f;
						vOut.color = points[id].color;

						return vOut;
					}

					// Geometry Shader -----------------------------------------------------
					[maxvertexcount(4)]
					void geometryShader(point GS_INPUT p[1], inout TriangleStream<FS_INPUT> triStream)
					{
						// cutout trough a transition area
						float cameraDistance = length(_WorldSpaceCameraPos - p[0].worldPosition);

						// discard billboards that are too far away
						if (cameraDistance > _MaxCameraDistance)
							return;

						float t = (cameraDistance - (_MaxCameraDistance - _Transition)) / _Transition;
						float alpha = clamp(1, 0, lerp(1.0, 0.0, t));

						// billboard's normal is a projection plane's normal
						float3 viewDirection = UNITY_MATRIX_IT_MV[2].xyz;
						viewDirection.y = 0;
						viewDirection = normalize(viewDirection);

						float3 up = float3(0, 1, 0);
						float3 right = normalize(cross(up, viewDirection));

						// billboard's size and color
						float2 size = p[0].parameters;//lerp(_MinSize, _MaxSize, noiseValue * sizeFactor);
						//float halfSize = size.x;
						float4 tint;
						tint.xyz = p[0].color.xyz;//lerp(_HealthyColor, _DryColor, noiseValue);
						tint.a = alpha;

						// create billboard
						float4 v[4];
						v[0] = float4(p[0].worldPosition + size.x * right, 1.0f);
						v[1] = float4(p[0].worldPosition + size.x * right + size.y * up, 1.0f);
						v[2] = float4(p[0].worldPosition - size.x * right, 1.0f);
						v[3] = float4(p[0].worldPosition - size.x * right + size.y * up, 1.0f);

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
						if (color.a < _Cutoff)
							discard;

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
					/*// Fragment Shader -----------------------------------------------
					float4 fragmentShader(FS_INPUT fIn) : COLOR
					{
						fixed4 color = tex2D(_MainTex, fIn.uv_MainTexture) * fIn.tint;
						if (color.a < _Cutoff)
							discard;

						float3 lightDirection = normalize(_WorldSpaceLightPos0.xyz);
						float atten = LIGHT_ATTENUATION(fIn);

						float3 ambient = UNITY_LIGHTMODEL_AMBIENT.xyz;
						float3 normal = float3(0,1,0);
						float3 lambert = float(max(0.0,dot(normal,lightDirection)));
						float3 lighting = (ambient + lambert * atten) * _LightColor0.rgb;

						color = fixed4(color.rgb * lighting, 1.0f);
									
						UNITY_APPLY_FOG(fIn.fogCoord, color);

						return color;
					}*/

				ENDCG
			}
		}

			FallBack "Diffuse"
}
