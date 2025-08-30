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

uniform vec4 _OuterColor;
uniform vec4 _InnerColor;

const float wire_width = 0.2;
const float wire_smoothness = 0.01;
const float alpha_cutoff = 0.35;

layout(location = 0) out vec4 FragColor;

void main()
{
    vec2 adjustedUv = vec2(0.5 - abs(0.5 - fUv.x), 0.5 - abs(0.5 - fUv.y)) * 2.0;
	vec2 deltas = fwidth(adjustedUv);
	vec2 barys_s = smoothstep(deltas * wire_width - wire_smoothness, deltas * wire_width + wire_smoothness, adjustedUv);
	float wires = min(barys_s.x, barys_s.y);
    float alpha = step(alpha_cutoff, 1.0 - wires);
    if (alpha < 0.5) discard;

	FragColor = vec4(mix(_InnerColor.rgb, _OuterColor.rgb, wires), alpha);
}

#endif
