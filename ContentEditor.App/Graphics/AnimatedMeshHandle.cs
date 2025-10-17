using System.Numerics;
using ReeLib.via;
using Silk.NET.Maths;
using Silk.NET.OpenGL;

namespace ContentEditor.App.Graphics;

public class AnimatedMeshHandle : MeshHandle
{
    public Matrix4X4<float>[] BoneMatrices = [];

    private GL GL { get; }

    internal AnimatedMeshHandle(GL gl, MeshResourceHandle mesh) : base(mesh)
    {
        GL = gl;
    }

    public override void Update()
    {
        if (Bones == null) return;

        var delta = Time.Delta;
        if (BoneMatrices.Length == 0) {
            BoneMatrices = new Matrix4X4<float>[Bones.DeformBones.Count];
            // BoneMatrices = new Matrix4X4<float>[Bones.Bones.Count];
            for (int i = 0; i < BoneMatrices.Length; ++i) {
                BoneMatrices[i] = Matrix4X4<float>.Identity;
            }

            var plshipbone = new mat4(0, 1, 0, 0, -1, 0, 0, 0, 0, 0, 1, 0, 0, 1.758f, -0.004f, 1);
            var hipsys = plshipbone.ToSystem();
            var hippos = hipsys.Translation;

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
        material.BindBoneMatrices(BoneMatrices);
        // for (int i = 0; i < BoneMatrices.Length; ++i) {
        //     material.Shader.SetUniform($"boneMatrices[{i}]", BoneMatrices[i]);
        // }
    }

    public override void BindForRender(Material material)
    {
        BindBones(material);
    }

    public override string ToString() => $"[Animated mesh {Handle}]";
}
