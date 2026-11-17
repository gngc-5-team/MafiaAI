// 빛 번짐 · 빛줄기 · 안개 · 빗줄기용 가산 합성(조명 영향 없음). 스프라이트·파티클 둘 다 쓴다.
// _Color를 HDR로 1보다 크게 주면 블룸이 걸린다.
Shader "MafiaAI/Manor/Additive"
{
    Properties
    {
        [MainTexture] _MainTex ("Texture", 2D) = "white" {}
        [HDR] _Color ("Tint", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" "PreviewType"="Plane" }
        Blend SrcAlpha One
        ZWrite Off
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            // _MainTex_ST를 일부러 둔다: 2D SRP 배처가 꺼져야 스프라이트 색(알파)·뒤집기가 정점에 구워져 이 셰이더로 들어온다.
            // (배처 경로에선 색이 unity_SpriteColor로 따로 오는데, 입자는 그 값을 받지 않는다 — 같은 재질을 스프라이트·입자가 함께 쓴다)
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Color;
            CBUFFER_END

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color * _Color;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half4 t = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                return t * i.color;
            }
            ENDHLSL
        }
    }
}
