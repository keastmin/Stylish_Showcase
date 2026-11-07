// E-specific adaptation of Easy Impact Frames. Uses the original particle custom
// streams, noise textures and LUT animation, with independent screen/core sizing.
Shader "ProjectR/Skill Impact Frame"
{
    Properties
    {
        _NoiseTexture("Noise", 2D) = "white" {}
        _LUTCoreMask("Core LUT", 2D) = "white" {}
        _LUTColor("Color LUT", 2D) = "white" {}
        _RadialDistortionAmount("Radial Distortion", Float) = 0.01
        _RadialDistortionCheapContrastLerp("Distortion Contrast", Float) = 0
        _RadialMaskStrength("Radial Mask Strength", Float) = 1
        _RadialMask("Radial Mask", Float) = 0
        _Size("Noise Size", Vector) = (1,0.01,0,0)
        _Speed("Noise Speed", Vector) = (0.1,-0.1,0,0)
        _Boost("Boost", Float) = 10
        _ContrastErosion("Contrast Erosion", Float) = 0
        _ContrastErosionSmoothness("Contrast Smoothness", Float) = 0.1
        _QuantizeUVRadius("Angular Quantization", Float) = 2048
        _QuantizedRadiusLerp("Quantization Blend", Float) = 0
        _LUTCoreAmplitude("Core Amplitude", Float) = 5
        _LUTCoreOffset("Core Offset", Float) = 0
        _LUTCoreSpeed("Core Speed", Float) = 0
        _LUTCoreNoiseSize("Core Noise Size", Vector) = (1,0.01,0,0)
        _LUTCoreNoiseSpeed("Core Noise Speed", Vector) = (3.333,10,0,0)
        _LUTCoreDistortAmount("Core Distortion", Float) = 0.05
        _LUTCoreQuantizeUVRadius("Core Angular Quantization", Float) = 2048
        _LUTCoreColor("Core Color", Color) = (0,0,0,0)
        _LUTCoreColorInverted("Inverted Core Color", Color) = (1,1,1,0)
        _LUTCoreCanInvert("Core Inversion", Float) = 1
        _LUTCoreLerp("Core Blend", Float) = 1
        _LUTCoreOverParticleColorLerp("Core Over Color", Float) = 1
        _LUTColorAmplitude("Color LUT Amplitude", Float) = 1
        _LUTColorOffset("Color LUT Offset", Float) = 0
        _LUTColorSpeed("Color LUT Speed", Float) = 0
        _LUTColorLerp("Color LUT Blend", Float) = 0
        _MaskRadius("Mask Radius", Float) = 0
        _MaskHardness("Mask Hardness", Float) = 1
        _MaskErosion("Mask Erosion", Float) = 0
        _MaskErosionSmoothness("Mask Smoothness", Float) = 0.333
        [HideInInspector] _CoreUVScale("Core UV Scale", Float) = 0.3
        [HideInInspector] _PlayerProtection("Player Center and Radius", Vector) = (0.5,0.5,0.08,0.14)
        [HideInInspector] _EffectTime("Timeline Time", Float) = 0
        _Src("Source Blend", Float) = 5
        _Dst("Destination Blend", Float) = 10
        _Cull("Cull", Float) = 2
        _ZWrite("Z Write", Float) = 0
        _ZTest("Z Test", Float) = 8
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
        Pass
        {
            Name "SkillImpact"
            Tags { "LightMode"="UniversalForwardOnly" }
            Blend [_Src] [_Dst], One OneMinusSrcAlpha
            Cull [_Cull]
            ZWrite [_ZWrite]
            ZTest [_ZTest]
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            TEXTURE2D(_NoiseTexture); SAMPLER(sampler_NoiseTexture);
            TEXTURE2D(_LUTCoreMask); SAMPLER(sampler_LUTCoreMask);
            TEXTURE2D(_LUTColor); SAMPLER(sampler_LUTColor);
            CBUFFER_START(UnityPerMaterial)
                float4 _Size, _Speed, _LUTCoreNoiseSize, _LUTCoreNoiseSpeed;
                float4 _LUTCoreColor, _LUTCoreColorInverted, _PlayerProtection;
                float _RadialDistortionAmount, _RadialDistortionCheapContrastLerp;
                float _RadialMaskStrength, _RadialMask, _Boost;
                float _ContrastErosion, _ContrastErosionSmoothness;
                float _QuantizeUVRadius, _QuantizedRadiusLerp;
                float _LUTCoreAmplitude, _LUTCoreOffset, _LUTCoreSpeed;
                float _LUTCoreDistortAmount, _LUTCoreQuantizeUVRadius;
                float _LUTCoreCanInvert, _LUTCoreLerp, _LUTCoreOverParticleColorLerp;
                float _LUTColorAmplitude, _LUTColorOffset, _LUTColorSpeed, _LUTColorLerp;
                float _MaskRadius, _MaskHardness, _MaskErosion, _MaskErosionSmoothness;
                float _CoreUVScale, _EffectTime;
            CBUFFER_END
            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color : COLOR;
                float4 uv : TEXCOORD0;
                float4 custom : TEXCOORD1;
                float4 extra : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color : COLOR;
                float4 uv : TEXCOORD0;
                float4 custom : TEXCOORD1;
                float4 extra : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color;
                output.uv = input.uv;
                output.custom = input.custom;
                output.extra = input.extra;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);
                float2 radialUV = input.uv.xy * 2 - 1;
                float radius = length(radialUV);
                float angle = frac(atan2(radialUV.x, radialUV.y) / TWO_PI);
                float2 polar = float2(angle, radius);
                float2 quantized = float2(floor(angle * _QuantizeUVRadius) / _QuantizeUVRadius, radius);
                float2 noiseUV = lerp(polar, quantized, _QuantizedRadiusLerp);
                float2 dx = ddx(input.uv.xy), dy = ddy(input.uv.xy);
                float radialMask = saturate(1 - radius);

                // UV + Custom1XYZW + Custom2XYZW use Unity's packed particle layout.
                float intensity = input.uv.z, desaturate = input.uv.w;
                float contrast = input.custom.x, invert = input.custom.y;
                float boost = input.custom.z, size = input.custom.w;
                float2 pan = _EffectTime * _Speed.xy + noiseUV * float2(_Size.x * size, _Size.y);
                float2 noise = SAMPLE_TEXTURE2D_GRAD(_NoiseTexture, sampler_NoiseTexture, pan, dx, dy).rg * 2 - 1;
                float2 distortion = noise * radialMask * _RadialDistortionAmount * intensity;
                float cheapMask = 0.5 * (1 - saturate(radialMask * _RadialMaskStrength - _RadialMask));
                distortion *= lerp(1, cheapMask, _RadialDistortionCheapContrastLerp);

                // An ellipse in screen space protects the current pose and weapon.
                // Guard both sample endpoints so the strong warp cannot drag the body outwards.
                float2 protectionRadius = max(_PlayerProtection.zw, 0.0001);
                float outside = smoothstep(1, 1.35, length((screenUV - _PlayerProtection.xy) / protectionRadius));
                float sourceOutside = smoothstep(1, 1.35, length((screenUV + distortion - _PlayerProtection.xy) / protectionRadius));
                distortion *= outside * sourceOutside;
                float3 scene = SampleSceneColor(saturate(screenUV + distortion));
                // The source multiplies RGBA by boost before its contrast matrix,
                // so the contrast offset is boosted too (not just scene RGB).
                float3 sceneContrast = saturate(((scene - 0.5) * contrast + 0.5) * (_Boost * boost));
                sceneContrast = lerp(sceneContrast, 1 - sceneContrast, invert);
                float luminance = dot(sceneContrast, float3(0.299, 0.587, 0.114));
                float3 mono = lerp(sceneContrast, luminance.xxx, desaturate);
                float3 color = smoothstep(_ContrastErosion, _ContrastErosion + _ContrastErosionSmoothness, mono);
                float mapped = smoothstep(_ContrastErosion, _ContrastErosion + _ContrastErosionSmoothness, luminance);
                float2 colorUV = (_EffectTime * _LUTColorSpeed + mapped * _LUTColorAmplitude + _LUTColorOffset).xx;
                color = lerp(color, SAMPLE_TEXTURE2D(_LUTColor, sampler_LUTColor, colorUV).rgb, _LUTColorLerp);

                // Decode the exact player pixels installed by PlayerImpactInput.
                // This avoids applying a different tonal curve to the surrounding floor.
                float3 unwarped = SampleSceneColor(screenUV);
                float marker = min(unwarped.r, unwarped.b) - unwarped.g;
                float bodyMask = smoothstep(0.3, 0.43, marker)
                    * (1 - smoothstep(0.03, 0.1, abs(unwarped.r - unwarped.b)));
                float bodyTone = saturate((unwarped.r - 0.45) * 2);
                bodyTone = smoothstep(0.2, 0.8, bodyTone);
                // Retain a readable silhouette at gameplay camera distance while
                // preserving a range of surface shades inside either polarity.
                bodyTone = lerp(0.06, 0.42, bodyTone);
                bodyTone = lerp(1 - bodyTone, bodyTone, invert);
                color = lerp(color, bodyTone.xxx, bodyMask);

                // Only the core's radial coordinate changes size. Coverage and warp stay independent.
                float coreRadius = radius * _CoreUVScale;
                float2 corePolar = float2(angle, coreRadius);
                float2 coreQuantized = float2(floor(angle * _LUTCoreQuantizeUVRadius) / _LUTCoreQuantizeUVRadius, coreRadius);
                float2 coreNoiseUV = _EffectTime * _LUTCoreNoiseSpeed.xy
                    + lerp(corePolar, coreQuantized, _QuantizedRadiusLerp) * float2(_LUTCoreNoiseSize.x * size, _LUTCoreNoiseSize.y);
                float coreNoise = SAMPLE_TEXTURE2D_GRAD(_NoiseTexture, sampler_NoiseTexture, coreNoiseUV, dx, dy).r;
                float corePan = _EffectTime * _LUTCoreSpeed + 1 - coreRadius * _LUTCoreAmplitude + _LUTCoreOffset + input.extra.x;
                float2 coreUV = (corePan + saturate(coreNoise * saturate(1 - coreRadius) * _LUTCoreDistortAmount)).xx;
                float core = SAMPLE_TEXTURE2D_GRAD(_LUTCoreMask, sampler_LUTCoreMask, coreUV, dx, dy).r * (1 - bodyMask);
                float3 coreColor = lerp(_LUTCoreColor.rgb,
                    lerp(_LUTCoreColorInverted.rgb, _LUTCoreColor.rgb, invert), _LUTCoreCanInvert);
                color = lerp(color, coreColor, core * _LUTCoreLerp) * input.color.rgb;
                color = lerp(color, coreColor, core * _LUTCoreOverParticleColorLerp);
                float mask = saturate(1 - saturate((radius - _MaskRadius) * _MaskHardness));
                float alpha = input.color.a * smoothstep(_MaskErosion, _MaskErosion + _MaskErosionSmoothness, mask);
                // Do not reveal the encoded opaque input through the particle's alpha.
                alpha = lerp(alpha, 1, bodyMask);
                return half4(color, saturate(alpha));
            }
            ENDHLSL
        }
    }
}
