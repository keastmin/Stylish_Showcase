Shader "ProjectR/VFX/Afterimage Preview Floor"
{
    Properties { _BaseColor("Floor", Color)=(0.021,0.034,0.05,1) }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial) float4 _BaseColor; CBUFFER_END
            struct V { float4 p:SV_POSITION; float3 world:TEXCOORD0; };
            V Vert(float4 p:POSITION) { V o; o.world=TransformObjectToWorld(p.xyz); o.p=TransformWorldToHClip(o.world); return o; }
            half4 Frag(V i):SV_Target
            {
                float2 coord=i.world.xz*2;
                float2 g=abs(frac(coord-0.5)-0.5)/max(fwidth(coord),0.001);
                float grid=1-saturate(min(g.x,g.y));
                float falloff=exp(-length(i.world.xz)*0.12);
                return half4(_BaseColor.rgb+grid*float3(0.028,0.061,0.08)*falloff,1);
            }
            ENDHLSL
        }
    }
}
