Shader "Custom/Bubble"
{
    Properties //着色器的输入   
    {
        [NoScaleOffset] _SDF("Texture", 2D) = "white" {}
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

    CBUFFER_START(UnityPerMaterial)
   
    CBUFFER_END

    TEXTURE2D(_SDF); //贴图采样    
    SAMPLER(sampler_SDF);

    struct Attributes //顶点着色器  
    {
        float4 positionOS: POSITION;
        float3 normalOS: TANGENT;
        half4 vertexColor: COLOR;
        float2 uv : TEXCOORD0;
    };

    struct Varyings //片元着色器  
    {
        float4 positionCS: SV_POSITION;
        float2 uv: TEXCOORD0;
        half4 vertexColor: COLOR;
    };

    Varyings vert(Attributes v)
    {
        Varyings o;
        o.positionCS = TransformObjectToHClip(v.positionOS);
        o.uv = v.uv;
        o.vertexColor = v.vertexColor;
        return o;
    }
    half4 frag(Varyings i) : SV_Target /* 注意在HLSL中，fixed4类型变成了half4类型*/
    {
        half4 OUT;
        half col =1- SAMPLE_TEXTURE2D(_SDF, sampler_SDF, i.uv).x;
        OUT = step(frac(_Time.y/10),col);
        return OUT;
    }
    ENDHLSL

    SubShader
    {
        Tags
        {
            "RenderType"="Opaque"
        }


        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            ENDHLSL
        }
    }
}