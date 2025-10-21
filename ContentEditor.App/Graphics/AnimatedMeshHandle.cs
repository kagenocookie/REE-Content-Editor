using Silk.NET.Maths;

namespace ContentEditor.App.Graphics;

public class AnimatedMeshHandle : MeshHandle
{
    public Matrix4X4<float>[] BoneMatrices = [];
    public Matrix4X4<float>[] DeformBoneMatrices = [];

    internal AnimatedMeshHandle(MeshResourceHandle mesh) : base(mesh)
    {
    }

    public override void Update()
    {
        if (Bones == null) return;

        var delta = Time.Delta;
        if (DeformBoneMatrices.Length == 0) {
            DeformBoneMatrices = new Matrix4X4<float>[Bones.DeformBones.Count];
            for (int i = 0; i < DeformBoneMatrices.Length; ++i) {
                DeformBoneMatrices[i] = Matrix4X4<float>.Identity;
            }
            BoneMatrices = new Matrix4X4<float>[Bones.Bones.Count];
            for (int i = 0; i < BoneMatrices.Length; ++i) {
                BoneMatrices[i] = Bones.Bones[i].globalTransform.ToGeneric();
            }
        }
    }

    public void BindBones(Material material)
    {
        if ((material.Shader.Flags & ShaderFlags.EnableSkinning) == 0) return;
        material.BindBoneMatrices(DeformBoneMatrices);
    }

    public bool TryGetBoneTransform(uint boneHash, out Matrix4X4<float> matrix)
    {
        var bone = Bones?.GetByHash(boneHash);
        if (bone != null && BoneMatrices.Length > bone.index) {
            matrix = BoneMatrices[bone.index];
            return true;
        }

        matrix = Matrix4X4<float>.Identity;
        return false;
    }

    public bool TryGetBoneTransform(string boneName, out Matrix4X4<float> matrix)
    {
        var bone = Bones?.GetByName(boneName);
        if (bone != null && BoneMatrices.Length > bone.index) {
            matrix = BoneMatrices[bone.index];
            return true;
        }

        matrix = Matrix4X4<float>.Identity;
        return false;
    }

    public override void BindForRender(Material material)
    {
        BindBones(material);
    }

    public override string ToString() => $"[Animated mesh {Handle}]";
}
