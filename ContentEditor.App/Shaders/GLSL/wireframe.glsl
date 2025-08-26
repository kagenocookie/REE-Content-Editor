#ifdef VERTEX_PROGRAM

#include "includes/vertex_attr.glsl";

#include "includes/global.glsl";

uniform mat4 uModel;

out vec3 barys;

void main()
{
    gl_Position = uProjectionView * uModel * vec4(vPos, 1.0);
    switch (int(vVertID) % 3) {
        case 0: barys = vec3(1.0, 0.0, 0.0); break;
        case 1: barys = vec3(0.0, 1.0, 0.0); break;
        case 2: barys = vec3(0.0, 0.0, 1.0); break;
    }
}
#endif

#ifdef FRAGMENT_PROGRAM

in vec3 barys;

uniform sampler2D _MainTexture;

uniform vec4 _MainColor;
uniform vec4 _WireColor;

const float wire_width = 0.2;
const float wire_smoothness = 0.01;

layout(location = 0) out vec4 FragColor;

void main()
{
	vec3 deltas = fwidth(barys);
	vec3 barys_s = smoothstep(deltas * wire_width - wire_smoothness, deltas * wire_width + wire_smoothness, barys);
	float wires = min(barys_s.x, min(barys_s.y, barys_s.z));
	FragColor = vec4(mix(_WireColor.rgb, _MainColor.rgb, wires), mix(_WireColor.a, _MainColor.a, wires));
}

#endif
