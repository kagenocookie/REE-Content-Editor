vec4 animatedPosition = vec4(0.0f);

for (int i = 0; i < MAX_BONE_PER_VERTEX; i++)
{
    if (boneIds[i] == -1) continue;
    if (boneIds[i] >= MAX_BONES) {
        animatedPosition = vec4(pos,1.0f);
        break;
    }

    vec4 localPosition = finalBonesMatrices[boneIds[i]] * vec4(pos,1.0f);
    animatedPosition += localPosition * weights[i];
    vec3 localNormal = mat3(finalBonesMatrices[boneIds[i]]) * norm;
}
