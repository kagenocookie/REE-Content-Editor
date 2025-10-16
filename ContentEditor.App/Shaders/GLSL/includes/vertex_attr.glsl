layout (location = 0) in vec3 vPos;
layout (location = 1) in vec2 vUv;
layout (location = 2) in vec3 vNorm;
layout (location = 3) in int vVertID;
layout (location = 4) in vec3 vTan;
layout (location = 5) in ivec4 vBoneIDs;
layout (location = 6) in vec4 vWeights;
#ifdef ENABLE_INSTANCING
layout (location = 7) in mat4 instancedMatrix;
#endif
