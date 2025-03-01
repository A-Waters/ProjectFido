Shader "Unlit/playerShader"
{
    Properties
    {
        _MainTex ("Base (RGB)", 2D) = "white" { }
        _ScrollSpeed ("Scroll Speed", Float) = 0.1
        _Tiling ("Tiling", Vector) = (1, 1, 0, 0)
        _IsOwned ("Is Owned", Float) = 0.0 // 0 means not owned, 1 means owned
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            // Declare properties
            float _ScrollSpeed;
            float4 _Tiling;
            float _IsOwned; // Whether the object is owned or not
            sampler2D _MainTex;
            float4 _MainTex_ST;

            // Vertex shader
            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv * _Tiling.xy;  // Apply tiling to UVs
                return o;
            }

            // Fragment shader
            half4 frag(v2f i) : SV_Target
            {
                // Time-based texture scrolling
                float time = _Time.y * _ScrollSpeed;
                i.uv.x += time;

                // Scroll the texture with wrapping (clamp between 0 and 1)
                half4 texColor = tex2D(_MainTex, i.uv);

                // Declare the color variable outside the condition
                half4 color;

                if (_IsOwned > 0.5) // Check if owned
                {
                    // Generate a random color based on time for pseudo-randomness
                    color = half4(sin(_Time.y * 0.1), cos(_Time.y * 0.1), sin(_Time.y * 0.2), 1.0);
                } 
                else 
                {
                    // Color is blue if not owned
                    color = half4(0.0, 0.0, 1.0, 1.0);
                }

                return texColor * color;  // Multiply the texture with the color
            }
            ENDCG
        }
    }
    FallBack "Diffuse"
}
