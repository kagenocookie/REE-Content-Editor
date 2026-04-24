using System.Numerics;
using ContentEditor.Core;
using ReeLib.Common;
using ReeLib.Mesh;

namespace ContentEditor.App.ImguiHandling;

public class BoneNameHandler(Func<UIContext, uint>? hashGetter = null, Action<UIContext, uint>? hashSetter = null) : IObjectUIHandler
{
    public unsafe void OnIMGUI(UIContext context)
    {
        ImGui.PushID(context.label);
        var bones = context.FindHandlerInParents<IBoneReferenceHolder>();
        if (bones != null && bones.GetBones().Any()) {
            var width = ImGui.CalcItemWidth();
            var forceRefreshList = ImGui.Button($"{AppIcons.SI_Update}");
            ImguiHelpers.Tooltip("Refresh list"u8);
            var spacing = ImGui.GetStyle().ItemSpacing.X;
            width -= ImGui.CalcTextSize($"{AppIcons.SI_Update}").X + ImGui.GetStyle().FramePadding.X * 2 + spacing;

            ImGui.SameLine();
            var names = context.GetStateArray<string>();
            if (names == null || forceRefreshList) {
                names = bones.GetBones().Select(bone => bone.name).ToArray();
                context.SetStateArray<string>(names);
            }

            if (names.Length == 0) {
                ImGui.SetNextItemWidth(width - spacing);
            } else {
                ImGui.SetNextItemWidth(width / 2 - spacing);
                var name = context.Get<string>();
                if (ImguiHelpers.FilterableCombo("##combo", names, names, ref name, ref context.Filter)) {
                    UndoRedo.RecordSet(context, name, mergeMode: UndoRedoMergeMode.NeverMerge);
                    if (hashSetter != null) {
                        UndoRedo.AttachCallbackToLastAction(UndoRedo.CallbackType.Both, () => {
                            hashSetter.Invoke(context, MurMur3HashUtils.GetHash(context.Get<string>()));
                        });
                    }
                }

                ImGui.SameLine();
                ImGui.SetNextItemWidth(width / 2);
            }
        }

        StringFieldHandler.Instance.OnIMGUI(context);
        if (hashGetter != null && hashSetter != null) {
            var hash = hashGetter.Invoke(context);
            var oldHash = hash;
            if (ImGui.InputScalar($"{context.label} Hash", ImGuiDataType.U32, &hash)) {
                UndoRedo.RecordSet(context, bones?.FindBoneByHash(hash)?.name ?? "");
                if (hashSetter != null) {
                    var newHash = hash;
                    UndoRedo.AttachCallbackToLastAction(UndoRedo.CallbackType.Do, () => {
                        hashSetter.Invoke(context, newHash);
                    });
                    UndoRedo.AttachCallbackToLastAction(UndoRedo.CallbackType.Undo, () => {
                        hashSetter.Invoke(context, oldHash);
                    });
                }
            }
        }
        ImGui.PopID();
    }
}
public class BoneHashHandler : IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        ImGui.PushID(context.label);
        var bones = context.GetWindowHandler() as IBoneReferenceHolder;
        if (bones != null && bones.GetBones().Any()) {
            var width = ImGui.CalcItemWidth();
            var forceRefreshList = ImGui.Button($"{AppIcons.SI_Update}");
            ImguiHelpers.Tooltip("Refresh list"u8);
            var spacing = ImGui.GetStyle().ItemSpacing.X;
            width -= ImGui.CalcTextSize($"{AppIcons.SI_Update}").X + ImGui.GetStyle().FramePadding.X * 2 + spacing;

            ImGui.SameLine();
            var names = context.GetStateArray<string>();
            if (names == null || forceRefreshList) {
                names = bones.GetBones().Select(bone => bone.name).ToArray();
                context.SetStateArray<string>(names);
            }

            if (names.Length == 0) {
                ImGui.SetNextItemWidth(width - spacing);
            } else {
                ImGui.SetNextItemWidth(width / 2 - spacing);
                var hash = context.Get<uint>();
                var name = names.FirstOrDefault(n => MurMur3HashUtils.GetHash(n) == hash);
                if (ImguiHelpers.FilterableCombo("##combo", names, names, ref name, ref context.Filter)) {
                    UndoRedo.RecordSet(context, MurMur3HashUtils.GetHash(name ?? ""), mergeMode: UndoRedoMergeMode.NeverMerge);
                }

                ImGui.SameLine();
                ImGui.SetNextItemWidth(width / 2);
            }
        }

        NumericFieldHandler<uint>.UIntInstance.OnIMGUI(context);
        ImGui.PopID();
    }
}

public interface IBoneReferenceHolder
{
    IEnumerable<MeshBone> GetBones();
    MeshBone? FindBoneByHash(uint hash);
    MeshBone? FindBoneByName(string name) => FindBoneByHash(MurMur3HashUtils.GetHash(name));
    bool TryGetBoneTransform(uint hash, out Matrix4x4 matrix);
    bool TryGetBoneTransform(string name, out Matrix4x4 matrix) => TryGetBoneTransform(MurMur3HashUtils.GetHash(name), out matrix);
}

public interface IBoneReferenceHolderComponent : IBoneReferenceHolder
{
    MeshComponent? MeshComponent { get; }

    IEnumerable<MeshBone> IBoneReferenceHolder.GetBones() => MeshComponent?.GameObject.GetComponent<MeshComponent>()?.GetBones() ?? [];

    MeshBone? IBoneReferenceHolder.FindBoneByHash(uint hash)
    {
        return MeshComponent?.GameObject.GetComponent<MeshComponent>()?.FindBoneByHash(hash);
    }

    bool IBoneReferenceHolder.TryGetBoneTransform(uint hash, out Matrix4x4 matrix)
    {
        var comp = MeshComponent?.GameObject.GetComponent<MeshComponent>();
        if (comp == null) {
            matrix = Matrix4x4.Identity;
            return false;
        }

        return comp.TryGetBoneTransform(hash, out matrix);
    }
}
