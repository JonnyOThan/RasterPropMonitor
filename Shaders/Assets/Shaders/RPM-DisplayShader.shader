﻿// Upgrade NOTE: replaced 'mul(UNITY_MATRIX_MVP,*)' with 'UnityObjectToClipPos(*)'


Shader "RPM/DisplayShader"
{
	Properties
	{
		_MainTex ("Texture", 2D) = "white" {}
		_Opacity("_Opacity", Range(0,1) ) = 1
		_Color ("_Color", Color) = (1,1,1,1)
	}

	SubShader {

		Tags { "RenderType"="Overlay" "Queue" = "Transparent" }

		// Premultiplied Alpha shader for rendering/coloring textures.

		Lighting Off
		Blend One OneMinusSrcAlpha
		Cull Back
		Fog { Mode Off }
		ZWrite Off
		ZTest Always

		Pass {
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma target 3.0

			#include "UnityCG.cginc"

			struct appdata_t {
				float4 vertex : POSITION;
				float2 texcoord : TEXCOORD0;
			};

			struct v2f_displayshader {
				float4 vertex : SV_POSITION;
				float2 texcoord : TEXCOORD0;
			};

			UNITY_DECLARE_TEX2D(_MainTex);

			uniform float4 _MainTex_ST;
			uniform float4 _Color;
			uniform float _Opacity;

			v2f_displayshader vert (appdata_t v)
			{
				v2f_displayshader o;
				o.vertex = UnityObjectToClipPos(v.vertex);
				o.texcoord = TRANSFORM_TEX(v.texcoord,_MainTex);
				return o;
			}

			float4 frag (v2f_displayshader i) : COLOR
			{
				float4 diffuse = UNITY_SAMPLE_TEX2D(_MainTex, i.texcoord);
				diffuse.a *= _Color.a * _Opacity;
				diffuse.rgb = (diffuse.rgb * _Color.rgb) * diffuse.a;
				return diffuse;
			}
			ENDCG
		}
	}

	Fallback off
}
