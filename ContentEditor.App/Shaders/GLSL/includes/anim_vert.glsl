#ifdef ENABLE_SKINNING
vec4 finalPosition = vec4(0.0f);
vec3 finalNorm = vec3(0.0f);

#ifdef USE_6_WEIGHTS
int boneIDs[6] = int[6](
    int((vBoneIDs1 >> 0) & 0x3FFu), int((vBoneIDs1 >> 10) & 0x3FFu), int((vBoneIDs1 >> 20) & 0x3FFu),
    int((vBoneIDs2 >> 0) & 0x3FFu), int((vBoneIDs2 >> 10) & 0x3FFu), int((vBoneIDs2 >> 20) & 0x3FFu)
);

// note: in some files, weights 7/8 > 0, force normalize the values with this
float wScale = 1 + vWeights2.z + vWeights2.w;
float weights[6] = float[6](
    vWeights1.x * wScale, vWeights1.y * wScale, vWeights1.z * wScale,
    vWeights1.w * wScale, vWeights2.x * wScale, vWeights2.y * wScale
);

#else
int boneIDs[8] = int[8](
    vBoneIDs1.x, vBoneIDs1.y, vBoneIDs1.z, vBoneIDs1.w,
    vBoneIDs2.x, vBoneIDs2.y, vBoneIDs2.z, vBoneIDs2.w
);
float weights[8] = float[8](
    vWeights1.x, vWeights1.y, vWeights1.z, vWeights1.w,
    vWeights2.x, vWeights2.y, vWeights2.z, vWeights2.w
);
#endif

for (int i = 0; i < MAX_BONE_PER_VERTEX; i++)
{
    int id = boneIDs[i];
    float w = weights[i];

    if (w == 0.0 || id >= MAX_BONES) continue;

    mat4 boneMat = boneMatrices[id];
    vec4 localPosition = boneMat * vec4(vPos, 1.0f);
    finalPosition += localPosition * w;
    finalNorm += (mat3(boneMat) * vNorm) * w;
}

finalNorm = normalize(finalNorm);
#else

vec4 finalPosition = vec4(vPos, 1.0);
vec3 finalNorm = vNorm;

#endif
