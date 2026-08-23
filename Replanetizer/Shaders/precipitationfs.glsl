#version 330 core

in vec2 UV;
in float fogBlend;

layout(location = 0) out vec4 color;

uniform vec4 fogColor;
uniform vec4 particleColor;

void main() {
    float edgeFade = smoothstep(0.0f, 0.25f, UV.x) * (1.0f - smoothstep(0.75f, 1.0f, UV.x));
    float endFade = 1.0f - smoothstep(0.75f, 1.0f, UV.y);
    float alpha = particleColor.a * edgeFade * endFade;
    if (alpha <= 0.001f)
        discard;

    color = vec4(particleColor.rgb, alpha);
    color.xyz = mix(color.xyz, fogColor.xyz, fogBlend);
}
