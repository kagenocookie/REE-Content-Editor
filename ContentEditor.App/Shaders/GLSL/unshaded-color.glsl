#ifdef VERTEX_PROGRAM

#include "includes/vertex_attr.glsl";

#include "includes/global.glsl";

uniform mat4 uModel;

void main()
{
    gl_Position = uProjectionView * uModel * vec4(vPos, 1.0);
}
#endif

#ifdef FRAGMENT_PROGRAM

uniform vec4 _MainColor;

layout(location = 0) out vec4 FragColor;

void main()
{
    FragColor = _MainColor;
}

#endif
