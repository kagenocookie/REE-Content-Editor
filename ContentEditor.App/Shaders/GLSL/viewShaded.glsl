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
out vec3 fNorm;

void main()
{
#ifdef ENABLE_SKINNING
#include "includes/skinned_mesh.glsl";
#endif
    gl_Position = uProjectionView * uModel * vec4(vPos, 1.0);
    fUv = vUv;
    // fNorm = gl_Normal;
    mat3 normalMatrix = transpose(inverse(mat3(uModel)));
    fNorm = normalize(normalMatrix * vNorm);
}
#endif

#ifdef FRAGMENT_PROGRAM

#include "includes/global.glsl";

in vec2 fUv;
in vec3 fNorm;

uniform sampler2D _MainTexture;
uniform vec4 _MainColor;

layout(location = 0) out vec4 FragColor;

void main()
{
    vec4 fwd = vec4(0.0, 0.0, 1.0, 0.0) * uView;
    vec3 normal = normalize(fNorm);

    FragColor = texture(_MainTexture, fUv) * clamp(dot(fwd.xyz, normal), 0.25, 1.0);
}

#endif
