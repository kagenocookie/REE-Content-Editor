using System.Numerics;
using System.Reflection;
using ContentEditor.BackgroundTasks;
using ContentEditor.Core;
using ContentPatcher;
using ReeLib;
using ReeLib.Chain;
using ReeLib.Chain2;
using ReeLib.Common;
using ReeLib.Mesh;

namespace ContentEditor.App.ImguiHandling.Chain;

public class ChainEditor(ContentWorkspace env, FileHandle file, ContentEditor.App.Chain? component = null)
    : ChainEditorBase(env, file, component), IWorkspaceContainer, IObjectUIHandler
{
    public override string HandlerName => $"{AppIcons.SI_FileType_CHAIN} Chain";

    public ChainFile File => Handle.GetFile<ChainFile>();

    protected override bool IsRevertable => context.Changed;

    protected override void DrawFileContents()
    {
        if (HandleCache()) {
            foreach (var group in File.Groups) {
                if (string.IsNullOrEmpty(group.name)) {
                    group.name = Utils.HashedBoneNames.GetValueOrDefault(group.terminalNameHash) ?? "";
                }
            }
            foreach (var coll in File.Collisions) {
                coll.UpdateJointNames();
            }
            foreach (var coll in File.ChainLinks) {
                coll.UpdateJointNames();
            }
        }

        var isEmpty = context.children.Count == 0;
        if (context.children.Count == 0) {
            context.AddChild("Data", File, new ChainFileImguiHandler());
        }
        context.ShowChildrenUI();
    }

    void IObjectUIHandler.OnIMGUI(UIContext container)
    {
        this.OnIMGUI();
    }
}

public class Chain2Editor(ContentWorkspace env, FileHandle file, ContentEditor.App.Chain? component = null)
    : ChainEditorBase(env, file, component), IWorkspaceContainer, IObjectUIHandler
{
    public override string HandlerName => $"{AppIcons.SI_FileType_CHAIN2} Chain2";

    public Chain2File File => Handle.GetFile<Chain2File>();

    protected override bool IsRevertable => context.Changed;

    protected override void DrawFileContents()
    {
        if (HandleCache()) {
            foreach (var group in File.Groups) {
                if (string.IsNullOrEmpty(group.name)) {
                    group.name = Utils.HashedBoneNames.GetValueOrDefault(group.terminalNameHash) ?? "";
                }
            }
            foreach (var coll in File.Collisions) {
                coll.UpdateJointNames();
            }
            foreach (var coll in File.ChainLinks) {
                coll.UpdateJointNames();
            }
        }

        var isEmpty = context.children.Count == 0;
        if (context.children.Count == 0) {
            context.AddChild("Data", File, new Chain2FileImguiHandler());
        }
        context.ShowChildrenUI();
    }

    void IObjectUIHandler.OnIMGUI(UIContext container)
    {
        this.OnIMGUI();
    }
}

public abstract class ChainEditorBase(ContentWorkspace env, FileHandle file, ContentEditor.App.Chain? component)
    : FileEditor(file), IBoneReferenceHolder
{
    private Dictionary<uint, string>? _hashes;
    internal Dictionary<uint, string>? HashesCache => _hashes;

    public ContentWorkspace Workspace { get; } = env;

    public App.Chain? ChainTarget { get; } = component;

    private bool _requestedCache;

    static ChainEditorBase()
    {
    }

    public IEnumerable<MeshBone> GetBones() => ChainTarget?.GameObject.GetComponent<MeshComponent>()?.GetBones() ?? [];

    public MeshBone? FindBoneByHash(uint hash)
    {
        return ChainTarget?.GameObject.GetComponent<MeshComponent>()?.FindBoneByHash(hash);
    }

    public bool TryGetBoneTransform(uint hash, out Matrix4x4 matrix)
    {
        var comp = ChainTarget?.GameObject.GetComponent<MeshComponent>();
        if (comp == null) {
            matrix = Matrix4x4.Identity;
            return false;
        }

        return comp.TryGetBoneTransform(hash, out matrix);
    }

    protected bool HandleCache()
    {
        if (_hashes == null) {
            var cachePath = MeshBoneHashCacheTask.GetCachePath(Workspace.Game);
            if (!_requestedCache) {
                _requestedCache = true;
                if (!System.IO.File.Exists(cachePath)) {
                    // OK
                } else if (!cachePath.TryDeserializeJsonFile<Dictionary<uint, string>>(out var db, out var error)) {
                    Logger.Warn("Could not load previous mmtr parameter cache from path " + cachePath + ":\n" + error);
                } else {
                    _hashes = db;
                }

                if (_hashes == null) {
                    MainLoop.Instance.BackgroundTasks.Queue(new MeshBoneHashCacheTask(Workspace.Env));
                }
            } else {
                if (!MainLoop.Instance.BackgroundTasks.HasPendingTask<MeshBoneHashCacheTask>() &&
                    System.IO.File.Exists(cachePath) &&
                    cachePath.TryDeserializeJsonFile<Dictionary<uint, string>>(out var db, out var error)) {
                    _hashes = db;
                }
            }
            if (_hashes != null) {
                foreach (var (hash, name) in _hashes) {
                    Utils.HashedBoneNames.TryAdd(hash, name);
                }
                return true;
            }
        }

        return false;
    }
}


[ObjectImguiHandler(typeof(Chain2File), Stateless = false)]
public class Chain2FileImguiHandler : IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        if (context.children.Count == 0) {
            var file = context.Get<Chain2File>();
            context.AddChild("Header", file, getter: f => f!.Header).AddDefaultHandler();
            context.AddChild("Groups", file, getter: f => f!.Groups).AddDefaultHandler();
            context.AddChild("Collisions", file, getter: f => f!.Collisions).AddDefaultHandler();
            context.AddChild("Chain Links", file, getter: f => f!.ChainLinks).AddDefaultHandler();
            context.AddChild("Settings", file, new ListHandlerTyped<ChainSetting>() { CanCreateRemoveElements = true, AllowContextMenu = true }, getter: f => f!.Settings).AddDefaultHandler();
            context.AddChild("Wind Settings", file, getter: f => f!.WindSettings).AddDefaultHandler();
            context.AddChild("Extra Data", file, getter: f => f!.ExtraData).AddDefaultHandler();
        }
        context.ShowChildrenUI();
    }
}


[ObjectImguiHandler(typeof(ChainFile), Stateless = false)]
public class ChainFileImguiHandler : IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        if (context.children.Count == 0) {
            var file = context.Get<ChainFile>();
            context.AddChild("Header", file, getter: f => f!.Header).AddDefaultHandler();
            context.AddChild("Hit Flags", file, getter: f => f!.HitFlags).AddDefaultHandler();
            context.AddChild("Groups", file, getter: f => f!.Groups).AddDefaultHandler();
            context.AddChild("Collisions", file, getter: f => f!.Collisions).AddDefaultHandler();
            context.AddChild("Chain Links", file, getter: f => f!.ChainLinks).AddDefaultHandler();
            context.AddChild("Settings", file, new ListHandlerTyped<Chain2Setting>() { CanCreateRemoveElements = true, AllowContextMenu = true }, getter: f => f!.Settings).AddDefaultHandler();
            context.AddChild("Wind Settings", file, getter: f => f!.WindSettings).AddDefaultHandler();
            context.AddChild("Extra Data", file, getter: f => f!.ExtraData).AddDefaultHandler();
        }
        context.ShowChildrenUI();
    }
}

[ObjectImguiHandler(typeof(Chain2Group), Stateless = false)]
[ObjectImguiHandler(typeof(ChainGroup), Stateless = false)]
public class ChainGroupHandler : IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        var group = context.Get<ChainGroupBase>();
        if (context.children.Count == 0) {

            var boneHandler1 = new BoneNameHandler(c => c.parent!.Get<ChainGroupBase>().terminalNameHash, (c, v) => c.parent!.Get<ChainGroupBase>().terminalNameHash = v);
            context.AddChild("Terminal Node", group, boneHandler1, c => c!.name, (c, v) => c.name = v ?? "");

            var settingList = new InstancePickerHandler<ChainSettingBase>(false, (c, force) => {
                var editor = c.FindHandlerInParents<ChainEditorBase>();
                return (editor as ChainEditor)?.File.Settings as IEnumerable<ChainSettingBase> ?? (editor as Chain2Editor)?.File.Settings ?? [];
            });
            var windList = new InstancePickerHandler<WindSetting>(true, (c, force) => {
                var editor = c.FindHandlerInParents<ChainEditorBase>();
                return (editor as ChainEditor)?.File.WindSettings as IEnumerable<WindSetting> ?? (editor as Chain2Editor)?.File.WindSettings ?? [];
            });

            if (group is ChainGroup group1) {
                context.AddChild("Settings", group, settingList, c => group1.Settings, (c, v) => group1.Settings = v);
                context.AddChild("Wind Settings", group, windList, c => group.WindSettings, (c, v) => group.WindSettings = v);
                context.AddChild("Nodes", group, getter: c => group1.ChainNodes).AddDefaultHandler();
            } else if (group is ChainGroup group2) {
                context.AddChild("Settings", group, settingList, c => group2.Settings, (c, v) => group2.Settings = v);
                context.AddChild("Wind Settings", group, windList, c => group.WindSettings, (c, v) => group.WindSettings = v);
                context.AddChild("Nodes", group, getter: c => group2.ChainNodes).AddDefaultHandler();
            }

            WindowHandlerFactory.SetupObjectUIContext(context, group.GetType(), false, orderFunc: (member) => {
                // kinda hacky but still more convenient than manually whitelisting all 30 fields
                var fieldType = (member as FieldInfo)?.FieldType ?? (member as PropertyInfo)?.PropertyType;
                if (fieldType == null) return 99;
                if (fieldType.IsAssignableTo(typeof(ChainSettingBase))) return -1;
                if (fieldType == typeof(WindSetting)) return -1;
                if (fieldType.IsAssignableTo(typeof(IEnumerable<ChainNodeBase>))) return -1;
                switch (member.Name) {
                    case nameof(ChainGroup.name):
                    case nameof(ChainGroup.terminalNameHash): return -1;
                    case nameof(Chain2Group.ChainSubGroups): return 3;
                    case nameof(ChainGroup.clspFlags1): return 5;
                    case nameof(ChainGroup.clspFlags2): return 6;
                    default: return 99;
                }
            });
        }

        var show = ImguiHelpers.TreeNodeSuffix(context.label, group.ToString());
        if (ImGui.BeginPopupContextItem(context.label)) {
            if (ImGui.Selectable("Copy")) {
                VirtualClipboard.CopyToClipboard(group.Clone());
            }
            ChainGroupBase? newGroup = null;
            if (group is ChainGroup) {
                if (VirtualClipboard.TryGetFromClipboard<ChainGroup>(out var clip)) {
                    newGroup = clip;
                }
            } else if (group is Chain2Group) {
                if (VirtualClipboard.TryGetFromClipboard<Chain2Group>(out var clip)) {
                    newGroup = clip;
                }
            }
            if (newGroup != null && ImGui.Selectable("Paste (replace)")) {
                var clone = (ChainGroupBase)newGroup.Clone();
                if (clone is ChainGroup group1) {
                    var file = context.FindHandlerInParents<ChainEditor>()?.File;
                    if (file != null) {
                        if (group1.Settings != null && !file.Settings.Contains(group1.Settings)) {
                            group1.Settings = group1.Settings.DeepCloneGeneric();
                            file.Settings.Add(group1.Settings);
                            group1.Settings.EnsureUniqueId(file.Settings);
                        }
                        if (group1.WindSettings != null && !file.WindSettings.Contains(group1.WindSettings)) {
                            group1.WindSettings = group1.WindSettings.DeepCloneGeneric();
                            file.WindSettings.Add(group1.WindSettings);
                            group1.WindSettings.EnsureUniqueId(file.WindSettings);
                        }
                    }
                } else if (clone is Chain2Group group2) {
                    var file = context.FindHandlerInParents<Chain2Editor>()?.File;
                    if (file != null) {
                        if (group2.Settings != null && !file.Settings.Contains(group2.Settings)) {
                            group2.Settings = group2.Settings.DeepCloneGeneric();
                            file.Settings.Add(group2.Settings);
                            group2.Settings.EnsureUniqueId(file.Settings);
                        }
                        if (group2.WindSettings != null && !file.WindSettings.Contains(group2.WindSettings)) {
                            group2.WindSettings = group2.WindSettings.DeepCloneGeneric();
                            file.WindSettings.Add(group2.WindSettings);
                            group2.WindSettings.EnsureUniqueId(file.WindSettings);
                        }
                    }
                }
                UndoRedo.RecordSet(context, clone);
                context.ClearChildren();
            }
            ImGui.EndPopup();
        }

        if (show) {
            context.ShowChildrenUI();
            ImGui.TreePop();
        }
    }
}

[ObjectImguiHandler(typeof(ChainSetting))]
internal class ChainSettingHandler : LazyPlainObjectHandler<ChainSetting>
{
    protected override bool DoTreeNode(UIContext context, object instance)
    {
        var show = base.DoTreeNode(context, instance);
        if (ImGui.BeginPopupContextItem(context.label)) {
            if (ImGui.Selectable("Copy")) {
                VirtualClipboard.CopyToClipboard(context.Get<ChainSetting>().Clone());
            }
            if (VirtualClipboard.TryGetFromClipboard<ChainSetting>(out var copy) && ImGui.Selectable("Paste (replace)")) {
                var clone = (ChainSetting)copy.Clone();
                var file = context.FindHandlerInParents<ChainEditor>()?.File;
                if (clone.WindSettings != null && file != null && !file.WindSettings.Contains(clone.WindSettings)) {
                    clone.WindSettings = clone.WindSettings.DeepCloneGeneric();
                    file.WindSettings.Add(clone.WindSettings);
                    clone.WindSettings.EnsureUniqueId(file.WindSettings);
                }
                clone.EnsureUniqueId(file?.Settings ?? []);
                UndoRedo.RecordSet(context, clone);
                UndoRedo.AttachClearChildren(UndoRedo.CallbackType.Both, context);
            }
            ImGui.EndPopup();
        }
        return show;
    }
}

[ObjectImguiHandler(typeof(Chain2Setting))]
internal class Chain2SettingHandler : LazyPlainObjectHandler<Chain2Setting>
{
    protected override bool DoTreeNode(UIContext context, object instance)
    {
        var show = base.DoTreeNode(context, instance);
        if (ImGui.BeginPopupContextItem(context.label)) {
            if (ImGui.Selectable("Copy")) {
                var src = context.Get<Chain2Setting>();
                var deepClone = src.DeepCloneGeneric();
                // ensure we maintain the original wind settings instance so we can deduplicate correctly on paste
                deepClone.WindSettings = src.WindSettings;
                VirtualClipboard.CopyToClipboard(deepClone);
            }
            if (VirtualClipboard.TryGetFromClipboard<Chain2Setting>(out var copy) && ImGui.Selectable("Paste (replace)")) {
                var clone = (Chain2Setting)copy.Clone();
                clone.WindSettings = copy.WindSettings;
                var file = context.FindHandlerInParents<Chain2Editor>()?.File;
                if (clone.WindSettings != null && file != null && !file.WindSettings.Contains(clone.WindSettings)) {
                    clone.WindSettings = clone.WindSettings.DeepCloneGeneric();
                    file.WindSettings.Add(clone.WindSettings);
                    clone.WindSettings.EnsureUniqueId(file.WindSettings);
                }
                clone.EnsureUniqueId(file?.Settings ?? []);
                UndoRedo.RecordSet(context, clone);
                UndoRedo.AttachClearChildren(UndoRedo.CallbackType.Both, context);
            }
            ImGui.EndPopup();
        }
        return show;
    }
}

[ObjectImguiHandler(typeof(WindSetting))]
internal class WindSettingHandler : LazyPlainObjectHandler<WindSetting>
{
    protected override bool DoTreeNode(UIContext context, object instance)
    {
        var show = base.DoTreeNode(context, instance);
        if (ImGui.BeginPopupContextItem(context.label)) {
            if (ImGui.Selectable("Copy")) {
                VirtualClipboard.CopyToClipboard(context.Get<WindSetting>().Clone());
            }
            if (VirtualClipboard.TryGetFromClipboard<WindSetting>(out var copy) && ImGui.Selectable("Paste (replace)")) {
                var clone = (WindSetting)copy.Clone();
                var settings = context.FindHandlerInParents<ChainEditor>()?.File.WindSettings
                    ?? context.FindHandlerInParents<Chain2Editor>()?.File.WindSettings;

                if (settings != null && !settings.Contains(clone)) {
                    clone.EnsureUniqueId(settings);
                }

                UndoRedo.RecordSet(context, clone);
                UndoRedo.AttachClearChildren(UndoRedo.CallbackType.Both, context);
            }
            ImGui.EndPopup();
        }
        return show;
    }
}

[ObjectImguiHandler(typeof(ChainLink))]
internal class ChainLinkHandler : IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        var instance = context.Get<ChainLink>();
        if (AppImguiHelpers.CopyableTreeNode<ChainLink>(context)) {
            if (context.children.Count == 0) {
                var boneHandler1 = new BoneNameHandler(c => c.parent!.Get<ChainLink>().terminalNodeNameHashA, (c, v) => c.parent!.Get<ChainLink>().terminalNodeNameHashA = v);
                var boneHandler2 = new BoneNameHandler(c => c.parent!.Get<ChainLink>().terminalNodeNameHashB, (c, v) => c.parent!.Get<ChainLink>().terminalNodeNameHashB = v);
                context.AddChild("Node 1", instance, boneHandler1, c => c!.nodeName1, (c, v) => c.nodeName1 = v);
                context.AddChild("Node 2", instance, boneHandler2, c => c!.nodeName2, (c, v) => c.nodeName2 = v);
                WindowHandlerFactory.SetupObjectUIContext(context, typeof(ChainLink), orderFunc: (f) => {
                    if (f.Name == nameof(ChainLink.terminalNodeNameHashA) ||
                        f.Name == nameof(ChainLink.terminalNodeNameHashB) ||
                        f.Name == nameof(ChainLink.nodeName1) ||
                        f.Name == nameof(ChainLink.nodeName2)) {
                        return -1;
                    }
                    return 0;
                });
            }
            context.ShowChildrenUI();
            ImGui.TreePop();
        }
    }
}


[ObjectImguiHandler(typeof(ReeLib.Chain.CollisionData))]
[ObjectImguiHandler(typeof(ReeLib.Chain2.CollisionData))]
internal class CollisionDataHandler : IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        var instance = context.Get<CollisionDataBase>();
        var show = false;
        if (instance is ReeLib.Chain.CollisionData) {
            show = AppImguiHelpers.CopyableTreeNode<ReeLib.Chain.CollisionData>(context);
        } else {
            show = AppImguiHelpers.CopyableTreeNode<ReeLib.Chain2.CollisionData>(context);
        }
        if (show) {
            if (context.children.Count == 0) {
                var boneHandler1 = new BoneNameHandler(c => c.parent!.Get<CollisionDataBase>().jointNameHash, (c, v) => c.parent!.Get<CollisionDataBase>().jointNameHash = v);
                var boneHandler2 = new BoneNameHandler(c => c.parent!.Get<CollisionDataBase>().pairJointNameHash, (c, v) => c.parent!.Get<CollisionDataBase>().pairJointNameHash = v);
                context.AddChild("Node 1", instance, boneHandler1, c => c!.jointName, (c, v) => c.jointName = v);
                context.AddChild("Node 2", instance, boneHandler2, c => c!.pairJointName, (c, v) => c.pairJointName = v);
                WindowHandlerFactory.SetupObjectUIContext(context, instance.GetType(), orderFunc: (f) => {
                    if (f.Name == nameof(CollisionDataBase.jointNameHash) ||
                        f.Name == nameof(CollisionDataBase.pairJointNameHash) ||
                        f.Name == nameof(CollisionDataBase.jointName) ||
                        f.Name == nameof(CollisionDataBase.pairJointName)) {
                        return -1;
                    }
                    return 0;
                });
            }
            context.ShowChildrenUI();
            ImGui.TreePop();
        }
    }
}


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
            width -= ImGui.CalcTextSize($"{AppIcons.SI_Update}").X + ImGui.GetStyle().FramePadding.X * 2;

            ImGui.SameLine();
            var names = context.GetStateArray<string>();
            if (names == null || forceRefreshList) {
                names = bones.GetBones().Select(bone => bone.name).ToArray();
                context.SetStateArray<string>(names);
            }

            if (names.Length == 0) {
                ImGui.SetNextItemWidth(width - ImGui.GetStyle().ItemSpacing.X);
            } else {
                ImGui.SetNextItemWidth(width / 2 - ImGui.GetStyle().ItemSpacing.X);
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
                ImGui.SetNextItemWidth(width / 2 - ImGui.GetStyle().ItemSpacing.X);
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
            width -= ImGui.CalcTextSize($"{AppIcons.SI_Update}").X + ImGui.GetStyle().FramePadding.X * 2;

            ImGui.SameLine();
            var names = context.GetStateArray<string>();
            if (names == null || forceRefreshList) {
                names = bones.GetBones().Select(bone => bone.name).ToArray();
                context.SetStateArray<string>(names);
            }

            if (names.Length == 0) {
                ImGui.SetNextItemWidth(width - ImGui.GetStyle().ItemSpacing.X);
            } else {
                ImGui.SetNextItemWidth(width / 2 - ImGui.GetStyle().ItemSpacing.X);
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
