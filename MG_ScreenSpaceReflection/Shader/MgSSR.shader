// Upgrade NOTE: commented out 'float3 _WorldSpaceCameraPos', a built-in variable

Shader "Hidden/mg_ssr_shader"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        // No culling or depth
        Cull Off ZWrite Off ZTest Never
        Tags { "RenderType"="Opaque" "RenderPipeline" = "UniversalPipeline"}
        //reflected color and mask only
        Pass
        {
            Name "Linear SSR"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment frag
            #pragma enable_d3d11_debug_symbols
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            Texture2D _MainTex;
            SamplerState sampler_MainTex;
            Texture2D _CameraDepthTexture;
            SamplerState sampler_CameraDepthTexture;
            Texture2D _CameraNormalsTexture;
            SamplerState sampler_CameraNormalsTexture;
            TEXTURE2D_X_HALF(_SsrThinGBuffer);

            int _numSteps;
            float _thickness;
            float _stepSize;
            #define epsilon 1.0e-4f
            float _edgeFade;
            
            inline float ScreenEdgeMask(float2 clipPos)
            {
                float yDif = 1 - abs(clipPos.y);
                float xDif = 1 - abs(clipPos.x);
                [flatten]
                if (yDif < 0 || xDif < 0)
                {
                    return 0;
                }
                float t1 = smoothstep(0, .2 * _edgeFade, yDif);
                float t2 = smoothstep(0, .1 * _edgeFade, xDif);
                return saturate(t2 * t1);
            }

            //dither noise
            float Dither8x8(float2 ScreenPosition, float c0)
            {
                const float dither[64] =
                {
                    0, 32, 8, 40, 2, 34, 10, 42,
                    48, 16, 56, 24, 50, 18, 58, 26,
                    12, 44, 4, 36, 14, 46, 6, 38,
                    60, 28, 52, 20, 62, 30, 54, 22,
                    3, 35, 11, 43, 1, 33, 9, 41,
                    51, 19, 59, 27, 49, 17, 57, 25,
                    15, 47, 7, 39, 13, 45, 5, 37,
                    63, 31, 55, 23, 61, 29, 53, 21
                };
            
                c0 *= 2;
                float2 uv = ScreenPosition.xy * _ScreenParams.xy;
            
                uint index = (uint(uv.x) % 8) * 8 + uint(uv.y) % 8;
            
                float limit = float(dither[index] + 1) / 64.0;
                return saturate(c0 - limit);
            }




            float3 frag(Varyings i) : SV_Target
            {
                // Get sub pixel jittering and apply to the shader. 
                float dither_x = Dither8x8(i.texcoord, 0.5);
                float dither_y = Dither8x8(i.texcoord.yx, 0.5);
                float _smoothness = SAMPLE_TEXTURE2D_X_LOD(_SsrThinGBuffer, sampler_PointClamp, i.texcoord, 0).g;
                float smooth = smoothstep(0.0, 1.2, _smoothness);
                dither_x *= 2;
                dither_x -= 1;
                dither_y *= 2;
                dither_y -= 1;
                const float jitter_x = lerp(dither_x * 0.05f, 0, smooth);
                const float jitter_y = lerp(dither_y * 0.05f, 0, smooth);
                i.texcoord += float2(jitter_x, jitter_y);

                float depth = SAMPLE_TEXTURE2D_X_LOD(_CameraDepthTexture, sampler_CameraDepthTexture, i.texcoord, 0).r;
                if (depth == 0) {
                    return float3(0, 0, 0);
                }
                float4 clipPos = float4(i.texcoord * 2 - 1, depth, 1.0);
                // Rebuild view space position.
                float4 viewPos = mul(UNITY_MATRIX_I_P, clipPos);
                viewPos /= viewPos.w;
                // The Y component is flipped during transforming from NDC to screen space. We need to convert it back.
                viewPos.y = -viewPos.y;
                // Rebuild world space position.
                float4 worldPos = mul(UNITY_MATRIX_I_V, viewPos);
                worldPos /= worldPos.w;
                // Get world space normal. 
                float3 worldNormal = normalize((SAMPLE_TEXTURE2D(_CameraNormalsTexture, sampler_CameraNormalsTexture, i.texcoord)));
                // Get reflected dir.
                float3 reflectDir = reflect(normalize(worldPos.xyz - _WorldSpaceCameraPos), worldNormal);
                float3 reflectDir_v = normalize(mul(UNITY_MATRIX_V, float4(reflectDir, 0.0))).xyz;
                // viewPos.z = -viewPos.z;
                // reflectDir_v.z = -reflectDir_v.z;
                // Ray marching, starting from viewPos along reflectDir. 
                float3 curPos_v = viewPos.xyz + epsilon * reflectDir_v;
                float hit = 0;
                float2 curScreenSpacePos;
                [loop]
                for(int step = 0; step < _numSteps; step++){
                    curPos_v += reflectDir_v * _stepSize;
                    // Get depth from the pixel corresponds to our cur position. 
                    float4 curPos_clip = mul(UNITY_MATRIX_P, float4(curPos_v.x,curPos_v.y * -1, curPos_v.z, 1.0));
                    curPos_clip /= curPos_clip.w;
                    float2 uv = curPos_clip.xy * 0.5 + 0.5;
                    curScreenSpacePos = uv; 
                    if (uv.x < 0 || uv.x > 1 || uv.y < 0 || uv.y > 1) {
                        break;
                    }
                    float sceneDepth = SAMPLE_TEXTURE2D(_CameraDepthTexture, sampler_CameraDepthTexture, uv).r;
                    sceneDepth = LinearEyeDepth(sceneDepth, _ZBufferParams);  
                    // if(step == 0){return float4(sceneDepth, sceneDepth, sceneDepth, 1);break;}
                    // if(step == 0){return float4(curPos_v.z - sceneDepth, curPos_v.z - sceneDepth, curPos_v.z - sceneDepth, 1);break;}
                    // If current depth is grater than scene depth, we hit something.
                    if(-curPos_v.z - sceneDepth >= 0 && -curPos_v.z - sceneDepth <= _thickness){
                        float3 worldNormal_point = normalize((SAMPLE_TEXTURE2D(_CameraNormalsTexture, sampler_CameraNormalsTexture, uv)));
                        float3 viewNormal_point = mul(UNITY_MATRIX_V, float4(worldNormal_point, 0.0)).xyz;
                        if(hit = dot(viewNormal_point, reflectDir_v) < 0.0){
                            hit = 1;
                            float edgeFade = ScreenEdgeMask(curPos_clip.xy);
                            hit *= edgeFade;
                            break;
                        }
                    }
                }
                return float3(curScreenSpacePos, hit);
            }
            ENDHLSL
        }
    
        //composite
        Pass
        {
            Name "SSR Composite"
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"           

            Texture2D _reflectedScreenSpaceUV;
            SamplerState sampler_reflectedScreenSpaceUV;
            Texture2D _MainTex;
            SamplerState sampler_MainTex;
            TEXTURE2D_X_HALF(_SsrThinGBuffer);

            float _reflectedIntensity;

            half4 frag(Varyings i) : SV_Target
            {
                float3 reflectedScreenSpaceUV = SAMPLE_TEXTURE2D(_reflectedScreenSpaceUV, sampler_reflectedScreenSpaceUV, i.texcoord).rgb;
                float mask = reflectedScreenSpaceUV.b;
                float4 col = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord);
                float4 col_reflected = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, reflectedScreenSpaceUV.rg);
                float reflectivity = SAMPLE_TEXTURE2D_X_LOD(_SsrThinGBuffer, sampler_PointClamp, i.texcoord, 0).r;
                float4 final_col = lerp(col, col_reflected, reflectivity * mask);
                final_col.a = _reflectedIntensity;
                return final_col;
            }
            ENDHLSL
        }
    }
}
