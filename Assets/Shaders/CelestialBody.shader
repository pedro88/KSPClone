Shader "KSPClone/CelestialBody"
{
    Properties
    {
        _Color    ("Color", Color) = (1,1,1,1)
        _Emission ("Emission", Color) = (0,0,0,0)
        // Sun-direction in object space, used by the Moon shader pass to dim the
        // unlit hemisphere. (1,0,0) = fully lit, (-1,0,0) = fully dark.
        _SunDir   ("Sun Dir (object space)", Vector) = (1,0,0,0)
        _LitStrength ("Lit Strength", Range(0,1)) = 0.5
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Cull Off
            ZWrite Off
            ZTest Always
            Blend SrcAlpha OneMinusSrcAlpha

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Color;
            fixed4 _Emission;
            float4 _SunDir;
            float  _LitStrength;

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; };
            struct v2f     { float4 pos : SV_POSITION; float3 n : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                // World-space normal: needed because the shader param is in world space.
                o.n = normalize(mul((float3x3)unity_ObjectToWorld, v.normal));
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // Day/night dimming: world-space normal dotted with world-space
                // sun direction (set by the renderer). _LitStrength controls
                // how dark the night side goes (0 = pitch black, 1 = same as day).
                float lit = saturate(dot(normalize(i.n), normalize(_SunDir.xyz)));
                float day = lerp(1.0 - _LitStrength, 1.0, lit);
                fixed4 col = _Color * day + _Emission;
                col.a = _Color.a;
                return col;
            }
            ENDCG
        }
    }
}