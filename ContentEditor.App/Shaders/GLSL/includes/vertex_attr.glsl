layout (location = 0) in vec3 vPos;
layout (location = 1) in vec2 vUv;
layout (location = 2) in vec3 vNorm;
layout (location = 3) in int vVertID;
layout (location = 4) in vec3 vTan;
#ifdef USE_6_WEIGHTS
layout (location = 5) in uint vBoneIDs1;
layout (location = 6) in uint vBoneIDs2;
#else
layout (location = 5) in ivec4 vBoneIDs1;
layout (location = 6) in ivec4 vBoneIDs2;
#endif
layout (location = 7) in vec4 vWeights1;
layout (location = 8) in vec4 vWeights2;
layout (location = 9) in vec4 vColor;
#ifdef ENABLE_INSTANCING
layout (location = 10) in mat4 instancedMatrix;
#endif
