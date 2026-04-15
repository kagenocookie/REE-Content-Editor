#ifdef USE_6_WEIGHTS
const int MAX_BONE_PER_VERTEX = 6;
#else
const int MAX_BONE_PER_VERTEX = 8;
#endif

const int MAX_BONES = 255;
uniform mat4 boneMatrices[MAX_BONES];
