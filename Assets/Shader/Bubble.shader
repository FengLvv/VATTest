Shader "Custom/Bubble"
{
    Properties //着色器的输入   
    {
        [Header(Blast)]
        [NoScaleOffset] _SDF("SDF", 2D) = "white" {}
        _DisplayFrame("PlayFrame",Float) = 0
        _AllFrames("AllFrames",Float) = 0

        [Space]
        [Header(FlowNormal)]
        _FlowMap("FlowMap", 2D) = "white" {}
        _FlowMapStrength("FlowMapStrength", Float) = 1
        _FlowMppSpeed("FlowMppSpeed", Float) = 1

        [Normal] _NormalMap("NormalMap", 2D) = "bump" {}
        _NormalStrength("NormalStrength", Range(0,2)) = 1


        [Header(Spectrogram)]
        _spectrum("Spectrum", 2D) = "white" {}
        _MinThickNess("MinThickNess", Range(0,1)) = 0.1
        _MaxThickNess("MaxThickNess", Range(0,1)) = 1
        _ThickNessWarp("ThickNessWarp", Range(0,1)) = 1
        _SpectrogramStrength("SpectrogramStrength", Range(0,10)) = 1

        [Space]
        [Header(Transparency)]
        _FresnelPartial("FresnelPartial", Range(0,1)) = 1
        _TransparencyAdjust("TransparencyAdjust", Range(-1,1)) = 1
        _TransparencyScale("TransparencyScale", Range(0,5)) = 1




        [Space]
        [Header(CubeMap)]
        _CubeMap("CubeMap", CUBE) = "white" {}
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

    CBUFFER_START(UnityPerMaterial)
        float4 _FlowMap_ST;
        float4 _NormalMap_ST;
        float _FlowMapStrength;
        float _FlowMppSpeed;
        float _TimeSpeed;
        float _TimeOffset;
        float _DisplayFrame;
        float _NormalStrength;
        float _ThickNessWarp;
        float _MinThickNess;
        float _MaxThickNess;
        float _AllFrames;
        float _TransparencyScale;
        float _TransparencyAdjust;
        float _FresnelPartial;
        float _SpectrogramStrength;
    CBUFFER_END

    TEXTURE2D(_SDF); //贴图采样    
    SAMPLER(sampler_SDF);
    TEXTURE2D(_FlowMap); //贴图采样    
    SAMPLER(sampler_FlowMap);
    TEXTURE2D(_NormalMap); //贴图采样
    SAMPLER(sampler_NormalMap);
    TEXTURE2D(_spectrum); //贴图采样
    SAMPLER(sampler_spectrum);
    TEXTURECUBE(_CubeMap); //贴图采样
    SAMPLER(sampler_CubeMap);

    struct Attributes //顶点着色器  
    {
        float4 positionOS: POSITION;
        float3 normalOS: NORMAL;
        float4 tangent : TANGENT;
        float4 vertexColor: COLOR;
        float2 uv : TEXCOORD0;
    };

    struct Varyings //片元着色器  
    {
        float4 positionCS: SV_POSITION;
        float3 positionWS: TEXCOORD4;
        float2 uv: TEXCOORD0;
        float3 normalWS : TEXCOORD1;
        float3 tangentWS : TEXCOORD2;
        float3 bitangentWS : TEXCOORD3;
        float thickNess : TEXCOORD5;
        half4 vertexColor: COLOR;
    };

    float Remap(float value, float inMin, float inMax, float outMin, float outMax)
    {
        return outMin + (value - inMin) * (outMax - outMin) / (inMax - inMin);
    }

    float Scale(float inVlue, float para)
    {
        if (para == 0)
        {
            return inVlue;
        }
        return (1 - 1 / (1 + para * inVlue)) * (1 + 1 / para);
    }

    Varyings vert(Attributes v)
    {
        Varyings o;
        VertexPositionInputs posInput = GetVertexPositionInputs(v.positionOS);
        o.positionCS = posInput.positionCS;
        o.positionWS = posInput.positionWS;

        VertexNormalInputs normalInput = GetVertexNormalInputs(v.normalOS, v.tangent);
        o.normalWS = normalInput.normalWS;
        o.tangentWS = normalInput.tangentWS;
        o.bitangentWS = normalInput.bitangentWS;


        o.thickNess = Remap(v.positionOS.y, -0.5, 0.5, _MaxThickNess, _MinThickNess);

        o.uv = v.uv;
        o.vertexColor = v.vertexColor;
        return o;
    }

    half4 frag(Varyings i) : SV_Target /* 注意在HLSL中，fixed4类型变成了half4类型*/
    {
        half4 OUT;
        float3 viewDirWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
        Light mainLight = GetMainLight();
        float3 lightDirWS = mainLight.direction;

        // 用 flowmap 扰动 normal，然后转到世界空间
        float2 flowMap = SAMPLE_TEXTURE2D(_FlowMap, sampler_FlowMap, (i.uv*_FlowMap_ST.xy)+ _FlowMap_ST.zw +float2( _Time.x*_FlowMppSpeed,0)).xy * 2 - 1;
        float3 normal1 = UnpackNormal(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, i.uv*_NormalMap_ST.xy+_NormalMap_ST.zw + flowMap*_FlowMapStrength*0.01*_SinTime.x)).xyz;
        float3 normal2 = UnpackNormal(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, i.uv*_NormalMap_ST.xy+_NormalMap_ST.zw + flowMap*_FlowMapStrength*0.01*_CosTime.x)).xyz;
        float3 normal = lerp(normal1, normal2, _SinTime.y * 0.5 + 0.5);
        float3x3 WS2TS = float3x3(i.tangentWS, i.bitangentWS, i.normalWS);
        float3 normalWS = NLerp(i.normalWS, mul(normal, WS2TS), _NormalStrength);

        // 使用光的入射角和厚度采样光谱图
        float fresnel = 1 - pow(dot(viewDirWS, normalWS), 1);
        // 入射光和薄膜的夹角 0-1
        float angle = acos(fresnel) * INV_PI * 2;

        float thickNess = lerp(i.thickNess, i.thickNess * normal.x, _ThickNessWarp); // 切线空间的法线x作为厚度扰动，乘上由上到下的厚度变化
        float3 spectrogram = SAMPLE_TEXTURE2D(_spectrum, sampler_spectrum, float2(thickNess,angle)).rgb*_SpectrogramStrength;

        // specular
        float3 reflectDirWS = reflect(-viewDirWS, normalWS);
        half3 specCol1 = SAMPLE_TEXTURECUBE_LOD(_CubeMap, sampler_CubeMap, reflectDirWS, 0).rgb;
        half3 specCol2 = SAMPLE_TEXTURECUBE_LOD(_CubeMap, sampler_CubeMap, float3(reflectDirWS.xy,reflectDirWS.z), 0).rgb;
        half3 specCol = lerp(specCol1, specCol2, thickNess);

     
        float luminance = Scale(Luminance(specCol.rgb), _TransparencyAdjust) * _TransparencyScale + 0.1;

        
        float transparency = lerp(saturate(luminance*luminance*luminance),saturate(luminance*luminance*luminance)*fresnel,  _FresnelPartial);
        float3 col = transparency * spectrogram+specCol;


        
        // SDF
        half sdf = 1 - SAMPLE_TEXTURE2D(_SDF, sampler_SDF, i.uv).x;
        half sdfThreshold = _DisplayFrame / _AllFrames;
        half mask = smoothstep(sdfThreshold - 0.02, sdfThreshold + 0.02, sdf);

 
        return float4(specCol,transparency*mask);
   
    }
    ENDHLSL

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
        }

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            ENDHLSL
        }
    }
}