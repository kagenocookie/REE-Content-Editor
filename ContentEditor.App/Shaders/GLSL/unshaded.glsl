#ifdef VERTEX_PROGRAM

#include "includes/vertex_attr.glsl";

#include "includes/global.glsl";

uniform mat4 uModel;

out vec2 fUv;

void main()
{
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
