// 흐르는 배경 겹(구름·안개·바다·유리창 빗물)용 — 조명 영향 없음, 알파 블렌드(또는 가산), 텍스처가 반복되며 흐른다.
// _Scroll(x,y): 초당 UV 이동량. _Color를 HDR로 키우면 블룸.
Shader "MafiaAI/Manor/Scroll"
{
    Properties
    {
        [MainTexture] _MainTex ("Texture", 2D) = "white" {}
        [HDR] _Color ("Tint", Color) = (1,1,1,1)
        _Scroll ("Scroll (uv/sec)", Vector) = (0.01, 0, 0, 0)
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 10
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Blend [_SrcBlend] [_DstBlend]
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
                float4 _Scroll;
            CBUFFER_END

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv * _MainTex_ST.xy + _MainTex_ST.zw + _Scroll.xy * _Time.y;
                o.color = v.color * _Color;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                return SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv) * i.color;
            }
            ENDHLSL
        }
    }
}
