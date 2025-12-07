using System.Numerics;
using System.Reflection;
using ContentEditor.Core;
using ContentPatcher;
using ReeLib;
using ReeLib.Common;
using ReeLib.Mdf;
using ReeLib.Mesh;
using ReeLib.via;

namespace ContentEditor.App.ImguiHandling.Mesh;

[ObjectImguiHandler(typeof(MeshFile))]
public class MeshFileHandler : IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        if (context.children.Count == 0) {
            var instance = context.Get<MeshFile>();
            var ws = context.GetWorkspace();
            context.AddChild<MeshFile, Header>("Header", instance, getter: v => v!.Header).AddDefaultHandler();
            context.AddChild<MeshFile, MeshBoneHierarchy>("Bones", instance, getter: v => v!.BoneData).AddDefaultHandler<MeshBoneHierarchy>();
            context.AddChild<MeshFile, List<string>>("Materials", instance, getter: v => v!.MaterialNames).AddDefaultHandler();
            context.AddChild<MeshFile, MeshData>("Mesh", instance, getter: v => v!.MeshData).AddDefaultHandler<MeshData>();
            context.AddChild<MeshFile, ShadowMesh>("Shadow Mesh", instance, getter: v => v!.ShadowMesh).AddDefaultHandler<ShadowMesh>();
            context.AddChild<MeshFile, OccluderMesh>("Occluder Mesh", instance, getter: v => v!.OccluderMesh).AddDefaultHandler<OccluderMesh>();
            context.AddChild<MeshFile, MeshBuffer>("Data Buffer", instance, getter: v => v!.MeshBuffer).AddDefaultHandler<MeshBuffer>();
            context.AddChild<MeshFile, BlendShapeData>("Blend Shapes", instance, getter: v => v!.BlendShapes).AddDefaultHandler<BlendShapeData>();
            context.AddChild<MeshFile, NormalRecalcData>("Normal Recalc", instance, getter: v => v!.NormalRecalcData).AddDefaultHandler<NormalRecalcData>();
            context.AddChild<MeshFile, List<uint>>("Hashes", instance, getter: v => v!.Hashes).AddDefaultHandler<List<uint>>();
            context.AddChild<MeshFile, List<Vector3>>("FloatData", instance, getter: v => v!.FloatData).AddDefaultHandler<List<Vector3>>();
        }

        context.ShowChildrenUI();
    }
}

[ObjectImguiHandler(typeof(MeshBuffer))]
public class MeshBufferHandler : IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        if (context.children.Count == 0) {
            var instance = context.Get<MeshBuffer>();
            var ws = context.GetWorkspace();
            context.AddChild<MeshBuffer, int>("Vertices", instance, ReadOnlyWrapperHandler.Integer, getter: v => v!.Positions.Length);
            context.AddChild<MeshBuffer, int>("Indices", instance, ReadOnlyWrapperHandler.Integer, getter: v => v!.Faces?.Length ?? v.IntegerFaces!.Length);
            context.AddChild<MeshBuffer, bool>("Normals", instance, ReadOnlyWrapperHandler.Bool, getter: v => v!.Normals.Length > 0);
            context.AddChild<MeshBuffer, bool>("Tangents", instance, ReadOnlyWrapperHandler.Bool, getter: v => v!.Tangents.Length > 0);
            context.AddChild<MeshBuffer, bool>("UV0", instance, ReadOnlyWrapperHandler.Bool, getter: v => v!.UV0.Length > 0);
            context.AddChild<MeshBuffer, bool>("UV1", instance, ReadOnlyWrapperHandler.Bool, getter: v => v!.UV1.Length > 0);
            context.AddChild<MeshBuffer, bool>("Colors", instance, ReadOnlyWrapperHandler.Bool, getter: v => v!.Colors.Length > 0);
            context.AddChild<MeshBuffer, bool>("Weights", instance, ReadOnlyWrapperHandler.Bool, getter: v => v!.Weights.Length > 0);
            context.AddChild<MeshBuffer, bool>("Shapekey Weights", instance, ReadOnlyWrapperHandler.Bool, getter: v => v!.ShapeKeyWeights.Length > 0);
        }

        context.ShowChildrenNestedUI();
    }
}

[ObjectImguiHandler(typeof(Submesh))]
public class SubmeshHandler : IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        if (context.children.Count == 0) {
            var instance = context.Get<Submesh>();
            var ws = context.GetWorkspace();
            context.AddChild<Submesh, ushort>("Material Index", instance, getter: v => v!.materialIndex, setter: (v, i) => v.materialIndex = i).AddDefaultHandler();
            context.AddChild<Submesh, ushort>("Buffer Index", instance, ReadOnlyWrapperHandler.UShort, getter: v => v!.bufferIndex);
            context.AddChild<Submesh, int>("Vertices", instance, ReadOnlyWrapperHandler.Integer, getter: v => v!.vertCount);
            context.AddChild<Submesh, int>("Indices", instance, ReadOnlyWrapperHandler.Integer, getter: v => v!.indicesCount);
        }

        context.ShowChildrenNestedUI();
    }
}
