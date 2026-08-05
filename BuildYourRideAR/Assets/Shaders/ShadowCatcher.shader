// Renders only the shadow that falls on it, as a soft dark blend over the
// camera feed. Used for the invisible ground quad under the car so the car
// grounds itself against the real floor instead of floating.
Shader "BuildYourRide/ShadowCatcher"
{
    Properties
    {
        _ShadowStrength ("Shadow Strength", Range(0, 1)) = 0.55
        _FadeRadius ("Fade Radius", Range(0.1, 0.5)) = 0.5
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent-100"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Tags { "LightMode" = "ForwardBase" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fwdbase
            #include "UnityCG.cginc"
            #include "AutoLight.cginc"

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                SHADOW_COORDS(1)
            };

            float _ShadowStrength;
            float _FadeRadius;

            v2f vert(appdata_full v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord.xy;
                TRANSFER_SHADOW(o)
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed atten = SHADOW_ATTENUATION(i);

                // Feather the quad edges so the shadow patch has no visible
                // rectangular border against the camera feed.
                float2 d = i.uv - 0.5;
                float edge = 1.0 - smoothstep(_FadeRadius * 0.55, _FadeRadius, length(d));

                fixed alpha = (1.0 - atten) * _ShadowStrength * edge;
                return fixed4(0, 0, 0, alpha);
            }
            ENDCG
        }
    }
}
