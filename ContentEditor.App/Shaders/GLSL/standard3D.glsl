#ifdef VERTEX_PROGRAM

layout (location = 0) in vec3 vPos;
layout (location = 1) in vec2 vUv;

#include "includes/global.glsl";

uniform mat4 uModel;

#ifdef ENABLE_SKINNING
const int MAX_BONES = 250;
const int MAX_BONE_INFLUENCE = 4;
uniform mat4 finalBonesMatrices[MAX_BONES];
#endif

out vec2 fUv;

void main()
{
#ifdef ENABLE_SKINNING
#include "includes/skinned_mesh.glsl";
#endif
    gl_Position = uProjectionView * uModel * vec4(vPos, 1.0);
    fUv = vUv;
}
#endif

#ifdef FRAGMENT_PROGRAM

in vec2 fUv;

uniform sampler2D _MainTexture;

layout(location = 0) out vec4 FragColor;

void main()
{
    FragColor = texture(_MainTexture, fUv);
}

#endif
