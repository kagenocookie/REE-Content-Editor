#ifdef VERTEX_PROGRAM

#include "includes/vertex_attr.glsl";
#include "includes/global.glsl";

uniform mat4 uModel;

out vec3 worldPos;
out vec4 color;

void main()
{
    vec4 wpos = (uModel * vec4(vPos, 1.0));
    worldPos = wpos.xyz;
    gl_Position = uProjectionView * wpos;
    color = vColor;
}
#endif

#ifdef FRAGMENT_PROGRAM

#include "includes/global.glsl";

uniform vec4 _MainColor;
uniform float _FadeMaxDistance;
in vec3 worldPos;
in vec4 color;

layout(location = 0) out vec4 FragColor;

void main()
{
    float colorMult = 1.0;
    if (_FadeMaxDistance < 1000000) {
        colorMult = clamp(1 - length(uCameraPosition - worldPos) / _FadeMaxDistance, 0, 1);
    }
    FragColor = _MainColor * colorMult * color;
}

#endif
