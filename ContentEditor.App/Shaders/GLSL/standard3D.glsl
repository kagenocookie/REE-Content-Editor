#ifdef VERTEX_PROGRAM

#include "includes/vertex_attr.glsl";

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
#include "includes/anim_vert.glsl";
#endif
    gl_Position = uProjectionView * uModel * vec4(vPos, 1.0);
    fUv = vUv;
}
#endif

#ifdef FRAGMENT_PROGRAM

in vec2 fUv;

uniform sampler2D _MainTexture;
uniform vec4 _MainColor;

layout(location = 0) out vec4 FragColor;

void main()
{
    FragColor = texture(_MainTexture, fUv);
}

#endif
