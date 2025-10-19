using System.Numerics;
using ReeLib.via;
using Silk.NET.Maths;
using Silk.NET.OpenGL;

namespace ContentEditor.App.Graphics;

public class AnimatedMeshHandle : MeshHandle
{
    public Matrix4X4<float>[] BoneMatrices = [];
    public Matrix4X4<float>[] DeformBoneMatrices = [];

    private GL GL { get; }

    internal AnimatedMeshHandle(GL gl, MeshResourceHandle mesh) : base(mesh)
    {
        GL = gl;
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

            // var builder = new ShapeBuilder();
            // foreach (var bone in Bones.Bones) {
            //     var parentTrans = bone.Parent?.globalTransform.ToSystem() ?? Matrix4x4.Identity;
            //     var boneTrans = bone.localTransform.ToSystem() * parentTrans;
            //     builder.Add(new Capsule() { p0 = parentTrans.Translation, p1 = boneTrans.Translation, r = 0.01f });
            //     builder.Add(new OBB() { Coord = boneTrans, Extent = new Vector3(0.025f) });
            // }
            // var shapeMesh = builder.Create(GL);
            // shapeMesh.MeshGroup = 255;
            // Handle.Meshes.Add(shapeMesh);
        }
    }

    public void BindBones(Material material)
    {
        if ((material.Shader.Flags & ShaderFlags.EnableSkinning) == 0) return;
        material.BindBoneMatrices(DeformBoneMatrices);
        // for (int i = 0; i < BoneMatrices.Length; ++i) {
        //     material.Shader.SetUniform($"boneMatrices[{i}]", BoneMatrices[i]);
        // }
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
