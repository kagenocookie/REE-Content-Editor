#ifdef ENABLE_SKINNING
vec4 finalPosition = vec4(0.0f);
vec3 finalNorm = vec3(0.0f);

for (int i = 0; i < MAX_BONE_PER_VERTEX; i++)
{
    if (vBoneIDs[i] == -1) continue;
    if (vBoneIDs[i] >= MAX_BONES) {
        finalPosition = vec4(vPos, 1.0f);
        break;
    }

    mat4 boneMat = boneMatrices[vBoneIDs[i]];
    vec4 localPosition = boneMat * vec4(vPos, 1.0f);
    finalPosition += localPosition * vWeights[i];
    finalNorm += (mat3(boneMat) * vNorm) * vWeights[i];
}

finalNorm = normalize(finalNorm);
#else

vec4 finalPosition = vec4(vPos, 1.0);
vec3 finalNorm = vNorm;

#endif
