#ifdef VERTEX_PROGRAM

#include "includes/vertex_attr.glsl";

#include "includes/global.glsl";

uniform mat4 uModel;

#ifdef ENABLE_SKINNING
#include "includes/anim_headers.glsl";
#endif

out vec2 fUv;
out vec3 fNorm;

void main()
{
#include "includes/anim_vert.glsl";

    gl_Position = uProjectionView * uModel * finalPosition;
    fUv = vUv;
    mat3 normalMatrix = transpose(inverse(mat3(uModel)));
    fNorm = normalize(normalMatrix * finalNorm);
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
