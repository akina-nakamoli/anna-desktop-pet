sampler2D sprite : register(s0);
float amount : register(c0);
float4 main(float2 uv : TEXCOORD0) : COLOR {
    float2 d = (uv - float2(.5,.135)) / float2(.19,.145);
    float mask = saturate(1 - dot(d,d));
    uv.x -= amount * .023 * mask * mask;
    return tex2D(sprite, uv);
}
