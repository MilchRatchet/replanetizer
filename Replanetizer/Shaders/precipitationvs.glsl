#version 330 core

layout(location = 0) in vec2 vertexPos;
layout(location = 1) in vec2 vertexUV;
layout(location = 2) in vec3 instancePosition;
layout(location = 3) in vec2 instanceSize;

out vec2 UV;
out float fogBlend;

uniform mat4 worldToView;
uniform vec3 right;
uniform vec3 worldDown;
uniform int useFog;
uniform vec4 fogParams;

void main() {
    vec3 worldPosition = instancePosition + right * vertexPos.x * instanceSize.x + worldDown * vertexPos.y * instanceSize.y;
    gl_Position = worldToView * vec4(worldPosition, 1.0f);
    UV = vertexUV;

    fogBlend = 0.0f;
    if (useFog == 1) {
        float depth = gl_Position.w - fogParams.x;
        depth = clamp(depth * fogParams.y, 0.0f, 1.0f);
        fogBlend = fogParams.z + depth * fogParams.w;
    }
}
