// Detected-plane visual: a soft translucent glow with a world-anchored grid,
// a glowing rim and a pulse that travels outward from the plane's centre.
//
// Replaces the built-in legacy transparent shader that was rendering as
// magenta on device. Being a project shader rather than a built-in one, it is
// compiled with the project and cannot be stripped out of the build.
Shader "BuildYourRide/ARPlaneGlow"
{
    Properties
    {
        _ColorNear ("Colour (centre)", Color) = (0.35, 0.80, 1.00, 1)
        _ColorFar ("Colour (edge)", Color) = (0.55, 0.45, 1.00, 1)
        _RimColor ("Rim Colour", Color) = (0.70, 0.92, 1.00, 1)
        _PulseColor ("Pulse Colour", Color) = (0.80, 0.95, 1.00, 1)

        _Alpha ("Base Alpha", Range(0, 1)) = 0.10
        _RimAlpha ("Rim Alpha", Range(0, 1)) = 0.35
        _GridAlpha ("Grid Alpha", Range(0, 1)) = 0.16
        _PulseAlpha ("Pulse Alpha", Range(0, 1)) = 0.18

        _EdgeFeather ("Edge Feather (m)", Range(0.01, 1)) = 0.30
        _RimWidth ("Rim Width (m)", Range(0.01, 0.5)) = 0.07
        _GridSpacing ("Grid Spacing (m)", Range(0.02, 2)) = 0.25
        _GradientScale ("Gradient Scale (m)", Range(0.1, 8)) = 2.0

        _PulseSpeed ("Pulse Speed", Range(0, 3)) = 0.35
        _PulseFreq ("Pulse Spacing (1/m)", Range(0.05, 2)) = 0.30
        _PulseWidth ("Pulse Width", Range(0.02, 0.9)) = 0.35
        _BreatheSpeed ("Breathe Speed", Range(0, 6)) = 1.4
        _BreatheDepth ("Breathe Depth", Range(0, 0.6)) = 0.18
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
            "ForceNoShadowCasting" = "True"
        }

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            Lighting Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                // Distance from this vertex to the nearest boundary edge, in
                // metres, written by ARPlaneFeather. AR Foundation's generated
                // plane mesh has no such attribute of its own.
                float2 edge : TEXCOORD1;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float edgeDist : TEXCOORD1;
                float radial : TEXCOORD2;
            };

            fixed4 _ColorNear, _ColorFar, _RimColor, _PulseColor;
            float _Alpha, _RimAlpha, _GridAlpha, _PulseAlpha;
            float _EdgeFeather, _RimWidth, _GridSpacing, _GradientScale;
            float _PulseSpeed, _PulseFreq, _PulseWidth, _BreatheSpeed, _BreatheDepth;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.edgeDist = v.edge.x;
                // Plane-space distance from the plane's centre drives the
                // outward pulse and the centre-to-edge colour gradient.
                o.radial = length(v.vertex.xz);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // Fade out towards the boundary so planes blend into the room
                // instead of ending on a hard polygon edge.
                float feather = saturate(i.edgeDist / max(_EdgeFeather, 1e-4));
                feather = smoothstep(0.0, 1.0, feather);

                // A brighter band just inside the boundary reads as an outline.
                float rim = 1.0 - saturate(i.edgeDist / max(_RimWidth, 1e-4));
                rim = smoothstep(0.0, 1.0, rim) * feather;

                // Grid in session-space metres, so it stays locked to the room
                // as the plane grows rather than sliding around.
                float2 gridUV = i.uv / max(_GridSpacing, 1e-4);
                float2 gw = fwidth(gridUV);
                float2 gd = abs(frac(gridUV) - 0.5) / max(gw, 1e-5);
                float grid = 1.0 - saturate(min(gd.x, gd.y));

                // Soft ring travelling outward from the centre.
                float pulsePhase = frac(i.radial * _PulseFreq - _Time.y * _PulseSpeed);
                float pulse = smoothstep(1.0 - _PulseWidth, 1.0, pulsePhase);
                pulse *= pulse;

                float breathe = 1.0 - _BreatheDepth + _BreatheDepth * sin(_Time.y * _BreatheSpeed);

                float3 col = lerp(_ColorNear.rgb, _ColorFar.rgb,
                                  saturate(i.radial / max(_GradientScale, 1e-4)));
                col = lerp(col, _RimColor.rgb, rim);
                col += _PulseColor.rgb * pulse * 0.35;

                float a = _Alpha
                        + grid * _GridAlpha
                        + rim * _RimAlpha
                        + pulse * _PulseAlpha;

                a *= feather * breathe;

                return fixed4(col, saturate(a));
            }
            ENDCG
        }
    }

    Fallback Off
}
