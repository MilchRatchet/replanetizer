﻿#version 330 core

// Input vertex data, different for all executions of this shader.
layout(location = 0) in vec3 vertexPosition_modelspace;
layout(location = 1) in vec3 vertexNormal;
layout(location = 2) in vec2 vertexUV;
layout(location = 3) in vec4 vertexRGBA;
layout(location = 4) in float vertexTerrainLight;

struct Light {
    vec4 color1;
    vec4 direction1;
    vec4 color2;
    vec4 direction2;
};

// Allocate as many as can appear in any level
#define ALLOCATED_LIGHTS 20
layout(std140) uniform lights {
    Light light[ALLOCATED_LIGHTS];
};

// Output data ; will be interpolated for each fragment.
out vec2 UV;
out vec4 DiffuseColor;
out vec4 BakedColor;

// Values that stay constant for the whole mesh.
uniform mat4 WorldToView;
uniform mat4 ModelToWorld;
uniform int levelObjectType;
uniform int lightIndex;

void main(){
    // Output position of the vertex, in clip space : MVP * position
    gl_Position = WorldToView * (ModelToWorld * vec4(vertexPosition_modelspace, 1.0f));

    vec3 normal = normalize((ModelToWorld * vec4(vertexNormal, 0.0f)).xyz);

    // UV of the vertex. No special space for this one.
    UV = vertexUV;

    DiffuseColor = vec4(0.0f,0.0f,0.0f,1.0f);

    if (levelObjectType == 1 || levelObjectType == 3) {
        BakedColor = vertexRGBA;
    }

    int index = lightIndex;

    if (levelObjectType == 1) {
        index = min(ALLOCATED_LIGHTS - 1, int(vertexTerrainLight));
    }

    Light l = light[index];

    DiffuseColor += vec4(max(0.0f,-dot(l.direction1.xyz,normal)) * l.color1.xyz,1.0f);
    DiffuseColor += vec4(max(0.0f,-dot(l.direction2.xyz,normal)) * l.color2.xyz,1.0f);
}