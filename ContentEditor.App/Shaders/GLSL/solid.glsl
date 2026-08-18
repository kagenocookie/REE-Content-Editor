#ifdef VERTEX_PROGRAM

#include "includes/vertex_attr.glsl";
#include "includes/global.glsl";

uniform mat4 uModel;

#ifdef ENABLE_SKINNING
#include "includes/anim_headers.glsl";
#endif

out vec3 fNorm;

void main()
{
#include "includes/anim_vert.glsl";
#include "includes/instance_transform.glsl";

    gl_Position = uProjectionView * transform * finalPosition;
    mat3 normalMatrix = transpose(inverse(mat3(transform)));
    fNorm = normalize(normalMatrix * finalNorm);
}
#endif

#ifdef FRAGMENT_PROGRAM

#include "includes/global.glsl";

in vec3 fNorm;
uniform vec4 _MainColor;

layout(location = 0) out vec4 FragColor;

void main()
{
    vec4 fwd = vec4(0.0, 0.0, 1.0, 0.0) * uView;
    float light = clamp(dot(fwd.xyz, normalize(fNorm)), 0.25, 1.0);
    FragColor = vec4(_MainColor.rgb * light, _MainColor.a);
}

#endif
