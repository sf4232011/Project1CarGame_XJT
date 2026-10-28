Shader "BumperCars/VFX/Stylized Liquid"
{
    Properties
    {
        _BaseColor ("Liquid Color", Color) = (0.26, 0.78, 0.39, 1)
        _RimColor ("Liquid Edge", Color) = (0.06, 0.29, 0.17, 1)
        _HighlightColor ("Wet Highlight", Color) = (0.79, 1, 0.72, 1)
        _Opacity ("Opacity", Range(0, 1)) = 1
        [Enum(Drop,0,Puddle,1,Mist,2)] _Shape ("Shape", Float) = 0
        _Phase ("Variation", Float) = 0
        _Progress ("Dissolve", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
        }

        Pass
        {
            Name "LiquidForward"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _RimColor;
                half4 _HighlightColor;
                float _Opacity;
                float _Shape;
                float _Phase;
                float _Progress;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                half fogFactor : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.color = input.color;
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            float LiquidNoise(float2 p)
            {
                return sin(p.x * 3.7 + sin(p.y * 4.1)) *
                       sin(p.y * 3.2 + sin(p.x * 2.8));
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 p = input.uv * 2.0 - 1.0;
                float radius = length(p);
                float phase = _Phase * 6.2831853;
                float progress = saturate(_Progress);
                float remaining = 1.0 - progress;
                half alpha;
                half3 color;

                if (_Shape < 0.5)
                {
                    // A shaded round liquid bead; particle stretching supplies motion.
                    float edgeWidth = max(fwidth(radius) * 1.2, 0.018);
                    float boundary = 0.94 * lerp(1.0, 0.62, progress * progress);
                    alpha = 1.0 - smoothstep(boundary - edgeWidth, boundary, radius);
                    float sphereZ = sqrt(saturate(1.0 - radius * radius));
                    float3 normal = normalize(float3(p, sphereZ));
                    float light = saturate(dot(normal, normalize(float3(-0.45, 0.58, 0.9))));
                    float body = smoothstep(0.05, 0.92, sphereZ);
                    color = lerp(_RimColor.rgb, _BaseColor.rgb, body * (0.62 + light * 0.38));

                    float2 highlightUv = (p - float2(-0.30, 0.36)) / float2(0.23, 0.16);
                    float highlight = 1.0 - smoothstep(0.25, 1.0, dot(highlightUv, highlightUv));
                    float smallGlint = 1.0 - smoothstep(0.0, 0.09, length(p - float2(-0.06, 0.59)));
                    float bottomBounce = exp(-dot((p - float2(0.28, -0.52)) * float2(3.0, 5.0),
                                                  (p - float2(0.28, -0.52)) * float2(3.0, 5.0)));
                    color = lerp(color, _HighlightColor.rgb, highlight * 0.88 + smallGlint * 0.62);
                    color += _HighlightColor.rgb * bottomBounce * 0.12;
                    alpha *= lerp(0.82, 1.0, sphereZ);
                }
                else if (_Shape < 1.5)
                {
                    // All lobes stay inside the quad so adjacent trail marks overlap cleanly.
                    float angle = atan2(p.y, p.x);
                    float lobe = 0.76 + 0.075 * sin(angle * 5.0 + phase)
                                      + 0.045 * sin(angle * 8.0 - phase * 0.7)
                                      + 0.025 * sin(angle * 13.0 + phase * 1.3);
                    float boundary = lobe * lerp(1.0, 0.52, progress * progress);
                    float edgeDistance = boundary - radius;
                    float edgeWidth = max(fwidth(edgeDistance) * 1.3, 0.014);
                    alpha = smoothstep(0.0, edgeWidth, edgeDistance);
                    float body = smoothstep(0.015, 0.16, edgeDistance);
                    float flow = LiquidNoise(p * 2.2 + float2(phase, _Time.y * 0.16));
                    color = lerp(_RimColor.rgb, _BaseColor.rgb, body * (0.84 + flow * 0.10));

                    // Broken wet reflections, deliberately subtle rather than a luminous ring.
                    float rim = exp(-abs(edgeDistance - 0.045) * 72.0);
                    float facingLight = smoothstep(-0.15, 0.68, dot(normalize(p + 0.0001), float2(-0.65, 0.76)));
                    float brokenRim = 0.55 + 0.45 * sin(angle * 7.0 + phase);
                    color = lerp(color, _HighlightColor.rgb, rim * facingLight * brokenRim * 0.65);

                    float2 sheenUv = (p - float2(-0.18, 0.23)) / float2(0.42, 0.13);
                    float sheen = exp(-dot(sheenUv, sheenUv) * 2.0);
                    float ripplePhase = radius * 34.0 - _Time.y * 1.6 + phase;
                    float ripple = pow(saturate(sin(ripplePhase)), 16.0) *
                                   smoothstep(0.08, 0.24, radius) * (1.0 - smoothstep(0.38, 0.7, radius));
                    color = lerp(color, _HighlightColor.rgb, sheen * 0.34 + ripple * facingLight * 0.08);
                    alpha *= 0.94;
                }
                else
                {
                    // Compact billowing ink, without a smoke-texture or depth-texture dependency.
                    float noise = LiquidNoise(p * 2.4 + phase + _Time.y * 0.12);
                    float cloudRadius = radius + noise * 0.11;
                    alpha = (1.0 - smoothstep(0.20, 0.98, cloudRadius)) * 0.76;
                    color = lerp(_BaseColor.rgb, _RimColor.rgb, saturate(radius * 0.72 + noise * 0.16));
                    color = lerp(color, _HighlightColor.rgb, saturate((-p.x + p.y) * 0.28) * 0.10);
                }

                alpha *= _BaseColor.a * input.color.a * _Opacity * remaining;
                color *= input.color.rgb;
                color = MixFog(color, input.fogFactor);
                clip(alpha - 0.002);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
