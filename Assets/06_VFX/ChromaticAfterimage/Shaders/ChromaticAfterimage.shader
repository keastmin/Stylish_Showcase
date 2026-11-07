Shader "ProjectR/VFX/Chromatic Afterimage"
{
    Properties
    {
        _BaseMap("Original albedo", 2D) = "white" {}
        _BaseColor("Original color", Color) = (1,1,1,1)
        _EmissionMap("Original emission", 2D) = "white" {}
        [HDR] _EmissionColor("Original emission color", Color) = (0,0,0,0)
        _Opacity("Transparency", Range(0,1)) = 0.62
        _Fade("Remaining life", Range(0,1)) = 1
        _NoiseScale("Dissolve noise scale", Float) = 24
        _Seed("Snapshot seed", Float) = 0
        [HDR] _EdgeColor("Dissolve edge", Color) = (0.12,0.65,1.8,1)
        _EdgeStrength("Edge brightness", Range(0,2)) = 0.3
        _Cutoff("Original alpha cutoff", Range(0,1)) = 0.001
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent+12" }
        Pass
        {
            Name "ColoredDither"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest Less
            Cull Back
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_EmissionMap); SAMPLER(sampler_EmissionMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST, _BaseColor, _EmissionColor, _EdgeColor;
                float _Opacity, _Fade, _NoiseScale, _Seed, _EdgeStrength, _Cutoff;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS:SV_POSITION; float3 positionOS:TEXCOORD0; float3 normalWS:TEXCOORD1; float3 positionWS:TEXCOORD2; float2 uv:TEXCOORD3; UNITY_VERTEX_OUTPUT_STEREO };
            Varyings Vert(Attributes input)
            {
                Varyings o; UNITY_SETUP_INSTANCE_ID(input); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionOS=input.positionOS.xyz;
                o.positionWS=TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS=TransformWorldToHClip(o.positionWS);
                o.normalWS=TransformObjectToWorldNormal(input.normalOS);
                o.uv=TRANSFORM_TEX(input.uv,_BaseMap);
                return o;
            }
            float Hash(float3 p)
            {
                p=frac(p*0.1031); p+=dot(p,p.yzx+33.33); return frac((p.x+p.y)*p.z);
            }
            float Noise(float3 p)
            {
                float3 i=floor(p), f=frac(p); f=f*f*(3-2*f);
                return lerp(lerp(lerp(Hash(i),Hash(i+float3(1,0,0)),f.x),lerp(Hash(i+float3(0,1,0)),Hash(i+float3(1,1,0)),f.x),f.y),
                            lerp(lerp(Hash(i+float3(0,0,1)),Hash(i+float3(1,0,1)),f.x),lerp(Hash(i+float3(0,1,1)),Hash(i+1),f.x),f.y),f.z);
            }
            half4 Frag(Varyings i):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                half4 original=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv)*_BaseColor;
                clip(original.a-_Cutoff);
                float noise=Noise(i.positionOS*_NoiseScale+_Seed*13.7)*0.72+Hash(floor(i.positionOS*_NoiseScale*4)+_Seed)*0.28;
                float dissolve=lerp(0.02,1.02,pow(1-saturate(_Fade),1.35));
                clip(noise-dissolve);
                // A stable, fine ordered dither keeps the captured silhouette readable.
                const float bayer[16]={0,8,2,10,12,4,14,6,3,11,1,9,15,7,13,5};
                uint2 pixel=(uint2)i.positionCS.xy;
                float threshold=(bayer[(pixel.x&3)+((pixel.y&3)*4)]+0.5)/16.0;
                clip(lerp(0.22,0.86,saturate(_Fade))-threshold);
                half3 n=normalize(i.normalWS);
                Light light=GetMainLight();
                half3 illumination=max(half3(0.42,0.42,0.42),SampleSH(n))+light.color*saturate(dot(n,light.direction))*0.55;
                half3 rgb=original.rgb*illumination;
                rgb+=SAMPLE_TEXTURE2D(_EmissionMap,sampler_EmissionMap,i.uv).rgb*_EmissionColor.rgb;
                float edge=(1-smoothstep(0.0,0.075,noise-dissolve))*(1-_Fade);
                float fresnel=pow(1-saturate(dot(n,normalize(GetWorldSpaceViewDir(i.positionWS)))),4);
                rgb+=_EdgeColor.rgb*_EdgeStrength*(edge+fresnel*0.18);
                return half4(rgb,original.a*_Opacity*lerp(0.32,1,saturate(_Fade)));
            }
            ENDHLSL
        }
    }
}
