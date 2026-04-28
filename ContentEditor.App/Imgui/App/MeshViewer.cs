using System.Numerics;
using System.Text.Json;
using ContentEditor.App.FileLoaders;
using ContentEditor.App.Graphics;
using ContentEditor.App.ImguiHandling;
using ContentEditor.App.ImguiHandling.Mesh;
using ContentEditor.App.Widgets;
using ContentEditor.App.Windowing;
using ContentEditor.Core;
using ContentEditor.Editor;
using ContentPatcher;
using ReeLib;
using ReeLib.Common;
using ReeLib.via;

namespace ContentEditor.App;

public class MeshViewer : FileEditor, IDisposable, IFocusableFileHandleReferenceHolder
{
    public override bool HasUnsavedChanges => Handle.Modified;

    public override string HandlerName => $"Mesh Viewer";

    IRectWindow? IFileHandleReferenceHolder.Parent => data.ParentWindow;

    public ContentWorkspace Workspace { get; }
    private Scene? scene;

    public Scene? Scene => scene;

    private const float TopMargin = 64;


    public Animator? PrimaryAnimator => meshContexts.FirstOrDefault()?.Animator;
    public IEnumerable<Animator> Animators => meshContexts.Select(m => m.Animator!).Where(a => a != null);
    private float playbackSpeed = 1.0f;
    private static readonly float[] PlaybackSpeeds =
    {
        0.05f,
        0.25f,
        0.5f,
        0.75f,
        1.0f,
        1.25f,
        1.5f,
        2.0f
    };

    private string exportTemplate;

    private readonly MeshCollection? collection;
    public CommonMeshResource? Mesh => meshContexts.FirstOrDefault()?.MeshFile;

    private WindowData data = null!;

    private bool isDragging;
    private const float pitchLimit = MathF.PI / 2 - 0.01f;

    private bool showAnimationsMenu = false;
    private bool isSynced;

    private bool exportAnimations = true;
    private bool exportFbxskel = true;
    private bool exportFullCollection = true;
    private bool exportCurrentAnimationOnly;
    private bool exportInProgress;
    private bool exportLods = false;
    private bool exportOcclusion = true;

    private string? lastImportSourcePath;

    private bool showSkeleton;
    private GizmoShapeBuilder? _skeletonBuilder;
    private MeshHandle? gizmoMeshHandle;

    private bool showImportSettings;

    private List<MeshViewerContext> meshContexts = new();
    internal IReadOnlyList<MeshViewerContext> MeshContexts => meshContexts.AsReadOnly();
    private MeshViewerContext? _animationListContext;

    protected override void Reset()
    {
        base.Reset();
        ChangeMainMesh();
    }

    public MeshViewer(ContentWorkspace workspace, FileHandle file, MeshCollection? collection = null) : base(file)
    {
        windowFlags = ImGuiWindowFlags.MenuBar;
        Workspace = workspace;
        this.collection = collection;
        exportTemplate = (file.Resource as CommonMeshResource)?.NativeMesh.CurrentVersionConfig ?? MeshFile.GetGameVersionConfigs(workspace.Game).First();
    }

    public void ChangeMainMesh(string newMesh)
    {
        if (Workspace.ResourceManager.TryResolveGameFile(newMesh, out var newFile) && newFile != Handle) {
            ChangeMainMesh(newFile);
        }
    }

    public void ChangeMainMesh(FileHandle newHandle)
    {
        Handle.References.Remove(this);
        Handle = newHandle;
        ChangeMainMesh();
    }

    private MeshViewerContext CreateAdditionalMesh(FileHandle mesh)
    {
        var go = new GameObject($"_preview_{meshContexts.Count}", Workspace.Env, scene?.RootFolder, scene);
        scene?.Add(go);
        var ctx = new MeshViewerContext(this, context.AddChild(go.Name, go), mesh);
        meshContexts.Add(ctx);
        return ctx;
    }

    private void ChangeMainMesh(bool resetMdf = true)
    {
        var ctx = meshContexts.FirstOrDefault() ?? CreateAdditionalMesh(Handle);
        if (!Handle.References.Contains(this)) Handle.References.Add(this);
        ctx.ChangeMesh(resetMdf);
        ctx.UI.RemoveChild(ctx.UI.GetChild<MeshFileHandler>());

        var mesh = ctx.MeshFile;

        if (mesh.HasAnimations && string.IsNullOrEmpty(ctx.animationSourceFile)) {
            ctx.animationSourceFile = Handle.Filepath;
        }
    }

    private void CenterCameraToSceneObject()
    {
        var go = meshContexts.FirstOrDefault()?.GameObject;
        if (go == null || scene == null) return;

        scene.ActiveCamera.LookAt(go, true);
        scene.ActiveCamera.OrthoSize = go.GetWorldSpaceBounds().Size.Length() * 0.7f;
    }

    protected override void DrawFileContents() => throw new NotImplementedException();

    public override void OnIMGUI()
    {
        if (scene == null) {
            scene = EditorWindow.CurrentWindow!.SceneManager.CreateScene(Handle, true);
            scene.Type = SceneType.Independent;
            scene.Root.Controller.Keyboard = EditorWindow.CurrentWindow.LastKeyboard;
            scene.Root.Controller.MoveSpeed = AppConfig.Settings.MeshViewer.MoveSpeed;
            scene.OwnRenderContext.AddDefaultSceneGizmos();
            scene.AddWidget<SceneVisibilitySettings>();
        }
        data ??= context.Get<WindowData>();

        var mainCtx = meshContexts.FirstOrDefault();
        if (mainCtx == null) {
            ChangeMainMesh();
            mainCtx = meshContexts.First();
            scene.ActiveCamera.ProjectionMode = AppConfig.Settings.MeshViewer.DefaultProjection;
            CenterCameraToSceneObject();
            if (collection != null) LoadCollection(collection);
        }

        var meshComponent = mainCtx.Component;
        var mesh = mainCtx.MeshFile;

        if (mesh == null) {
            ImGui.Text("No mesh selected");
            return;
        }

        if (showSkeleton && meshContexts.Count > 0) {
            // TODO render fbxskel instead when available
            RenderSkeleton(mainCtx);
        }

        Vector2? embeddedMenuPos = null;
        if (!ShowMenu(mainCtx) && !isSynced) {
            embeddedMenuPos = ImGui.GetCursorPos();
        }
        var expectedSize = ImGui.GetWindowSize() - ImGui.GetCursorPos() - ImGui.GetStyle().WindowPadding;
        expectedSize.X = Math.Max(expectedSize.X, 4);
        expectedSize.Y = Math.Max(expectedSize.Y, 4);
        var nativeSize = data.ParentWindow.Size;
        float meshSize = meshComponent.LocalBounds.Size.Length();
        scene.ActiveCamera.FarPlane = meshSize + 100.0f;
        scene.RenderContext.SetRenderToTexture(expectedSize);

        if (scene.RenderContext.RenderTargetTextureHandle == 0) return;

        var c = ImGui.GetCursorPos();
        var cc = ImGui.GetCursorScreenPos();
        scene.OwnRenderContext.ViewportOffset = cc;
        AppImguiHelpers.Image(scene.RenderContext.RenderTargetTextureHandle, expectedSize, new System.Numerics.Vector2(0, 1), new System.Numerics.Vector2(1, 0));
        if (embeddedMenuPos != null) {
            ImGui.SetCursorPos(embeddedMenuPos.Value);
            ShowEmbeddedMenu(meshComponent);
        }
        scene.RenderUI();
        ImGui.SetCursorPos(c);
        ImGui.InvisibleButton("##image", expectedSize, ImGuiButtonFlags.MouseButtonLeft | ImGuiButtonFlags.MouseButtonRight);
        // need to store the click/hover events for after so we can handle clicks on the empty area below the info window same as a mesh image click event
        var meshClick = ImGui.IsItemClicked(ImGuiMouseButton.Right) || ImGui.IsItemClicked(ImGuiMouseButton.Left);
        var hoveredMesh = ImGui.IsItemHovered();

        if (isSynced) {
            isSynced = false;
        }

        if (showAnimationsMenu) {
            ImGui.SetCursorPos(new Vector2(20, TopMargin));
            ImGui.PushStyleColor(ImGuiCol.ChildBg, 0);
            ImGui.BeginChild("OverlayControlsContainer", new Vector2(500, ImGui.GetContentRegionAvail().Y - ImGui.GetStyle().WindowPadding.Y), ImGuiChildFlags.AutoResizeX | ImGuiChildFlags.AlwaysAutoResize, ImGuiWindowFlags.NoMove);
            ImGui.PushStyleColor(ImGuiCol.ChildBg, ImguiHelpers.GetColor(ImGuiCol.WindowBg) with { W = 0.5f });
            ImGui.BeginChild("OverlayControls", new Vector2(500, AppConfig.Instance.UseFullscreenAnimPlayback ? ImGui.GetContentRegionAvail().Y - 80 : 0), ImGuiChildFlags.AutoResizeY | ImGuiChildFlags.AlwaysAutoResize | ImGuiChildFlags.AlwaysUseWindowPadding | ImGuiChildFlags.Borders);

            ImGui.SameLine();
            ShowRootAnimationList();

            ImGui.PopStyleColor(2);
            ImGui.EndChild();
            hoveredMesh = hoveredMesh || ImGui.IsWindowHovered();
            ImGui.EndChild();
        }

        // 3D view controls
        meshClick = meshClick || ImGui.IsItemClicked(ImGuiMouseButton.Right) || ImGui.IsItemClicked(ImGuiMouseButton.Left);
        if (!isSynced) ShowPlaybackControls(meshComponent);

        if (meshClick) {
            if (!isDragging) {
                isDragging = true;
            }
        } else if (isDragging && !ImGui.IsMouseDown(ImGuiMouseButton.Left) && !ImGui.IsMouseDown(ImGuiMouseButton.Right)) {
            isDragging = false;
        }

        if (isDragging || hoveredMesh) {
            AppImguiHelpers.RedirectMouseInputToScene(scene, hoveredMesh);
        }
    }

    private void ShowEmbeddedMenu(MeshComponent meshComponent)
    {
        ImGui.SetCursorPos(ImGui.GetCursorPos() + new Vector2(6, 6));
        if (ImGui.Button($"{AppIcons.SI_GenericCamera}")) ImGui.OpenPopup("CameraSettings");
        if (ImGui.BeginPopup("CameraSettings")) {
            scene!.Controller.ShowCameraControls();
            ImGui.EndPopup();
        }
    }

    private bool ShowMenu(MeshViewerContext mainCtx)
    {
        if (ImGui.BeginMenuBar()) {
            if (ImGui.MenuItem($"{AppIcons.SI_GenericCamera} Controls")) ImGui.OpenPopup("CameraSettings");
            if (scene != null && ImGui.BeginPopup("CameraSettings")) {
                scene.Controller.ShowCameraControls();
                if (scene.ActiveCamera.ProjectionMode != AppConfig.Settings.MeshViewer.DefaultProjection) {
                    AppConfig.Settings.MeshViewer.DefaultProjection = scene.ActiveCamera.ProjectionMode;
                    AppConfig.Settings.Save();
                }
                if (Math.Abs(scene.Controller.MoveSpeed - AppConfig.Settings.MeshViewer.MoveSpeed) > 0.001f) {
                    AppConfig.Settings.MeshViewer.MoveSpeed = scene.Controller.MoveSpeed;
                    AppConfig.Settings.Save();
                }
                ImGui.EndPopup();
            }

            if (!isSynced && mainCtx.GameObject != null) {
                if (ImGui.MenuItem($"{AppIcons.SI_GenericInfo} Mesh Info")) ImGui.OpenPopup("MeshInfo");
                ImguiHelpers.VerticalSeparator();
                if (ImGui.MenuItem($"{AppIcons.SI_SceneGameObject4} Mesh Collection")) ImGui.OpenPopup("MeshList");
                if (ImGui.MenuItem($"{AppIcons.SI_MeshViewerMeshGroup} Mesh Groups")) ImGui.OpenPopup("MeshGroups");
                if (ImGui.MenuItem($"{AppIcons.SI_FileType_MDF} Material")) ImGui.OpenPopup("Material");
                var mdfErrors = mainCtx.GetMdfErrors();
                if (mdfErrors != null) {
                    using var _ = ImguiHelpers.OverrideStyleCol(ImGuiCol.Text, Colors.Warning);
                    ImGui.MenuItem($"{AppIcons.SI_GenericWarning}##mdf");
                    ImguiHelpers.Tooltip(mdfErrors);
                }
                ImguiHelpers.VerticalSeparator();
                if (ImGui.BeginMenu($"{AppIcons.SI_FileType_RCOL} RCOL")) {
                    var rcolEdit = Scene!.Root.SetEditMode(mainCtx.GameObject.GetOrAddComponent<RequestSetColliderComponent>());
                    rcolEdit?.DrawMainUI();
                    ImGui.EndMenu();
                }
                if (ImGui.BeginMenu($"{AppIcons.SI_MeshViewerChain} Chain")) {
                    mainCtx.GameObject.GetOrAddComponent<Chain>();
                    if (Workspace.Env.ComponentAvailable<CollisionShapePreset>()) {
                        mainCtx.GameObject.GetOrAddComponent<CollisionShapePreset>();
                    }
                    var edit = Scene!.Root.SetEditMode(mainCtx.GameObject.GetOrAddComponent<Chain>());
                    edit?.DrawMainUI();
                    ImGui.EndMenu();
                }
                if (ImGui.MenuItem($"{AppIcons.SI_Animation} Animations")) showAnimationsMenu = !showAnimationsMenu;
                if (showAnimationsMenu) ImguiHelpers.HighlightMenuItem($"{AppIcons.SI_Animation} Animations");
                var animWarns = GetAnimErrors();
                if (animWarns != null) {
                    using var _ = ImguiHelpers.OverrideStyleCol(ImGuiCol.Text, Colors.Warning);
                    ImGui.MenuItem($"{AppIcons.SI_GenericWarning}##anim");
                    ImguiHelpers.Tooltip(animWarns);
                }
                ImguiHelpers.VerticalSeparator();
                if (ImGui.MenuItem($"{AppIcons.SI_GenericIO} Import / Export")) ImGui.OpenPopup("Export");

                if (ImGui.BeginPopup("MeshInfo")) {
                    ShowMeshInfo(mainCtx, true);
                    ImGui.EndPopup();
                }
                if (ImGui.BeginPopup("MeshList")) {
                    ShowMeshCollections();
                    ImGui.EndPopup();
                }
                if (ImGui.BeginPopup("Material")) {
                    mainCtx.ShowMaterialSettings();
                    ImGui.EndPopup();
                }
                if (ImGui.BeginPopup("MeshGroups")) {
                    ShowMeshGroupSettings(mainCtx.Component);
                    ImGui.EndPopup();
                }
                if (ImGui.BeginPopup("Export")) {
                    ShowImportExportMenu();
                    ImGui.EndPopup();
                }
                if (ImGui.BeginPopup("Animations")) {
                    ShowAnimationMenu(mainCtx);
                    ImGui.EndPopup();
                }
            }
            ImGui.EndMenuBar();
            foreach (var c in meshContexts) c.UpdateMaterial();
            return true;
        } else {
            foreach (var c in meshContexts) c.UpdateMaterial();
            return false;
        }
    }

    private void RemoveSubmesh(MeshViewerContext ctx)
    {
        meshContexts.Remove(ctx);
        context.RemoveChild(ctx.UI);
        (scene?.RootFolder as INodeObject<GameObject>)?.RemoveChild(ctx.GameObject);
        ctx.GameObject.Dispose();
        if (_animationListContext == ctx) {
            _animationListContext = meshContexts.FirstOrDefault();
        }
    }

    private void EnsureAnimationsInit()
    {
        foreach (var c in meshContexts) {
            var anim = c.Animator ?? c.SetupAnimator();
            anim.owner ??= meshContexts[0].Animator;
        }
    }

    private void ShowMeshCollections()
    {
        int groupId = 0;
        EnsureAnimationsInit();
        foreach (var ctxGroup in meshContexts.GroupBy(c => c.Animator?.owner)) {
            if (groupId == 1) {
                ImGui.Separator();
            }
            foreach (var ctx in ctxGroup) {
                if (ImGui.BeginMenu(ctx.Animator != null && ctx.Animator?.ActiveOwner == null ? $"{ctx.ShortName} *###{ctx.ShortName}" : $"{ctx.ShortName}###{ctx.ShortName}")) {
                    if (ctx != meshContexts[0]) {
                        if (ImGui.Selectable($"{AppIcons.SI_GenericDelete} Remove", ImGuiSelectableFlags.NoAutoClosePopups)) {
                            RemoveSubmesh(ctx);
                            ImGui.EndMenu();
                            break;
                        }
                    }
                    if (ImGui.BeginMenu($"{AppIcons.SI_GenericInfo} Info")) {
                        ShowMeshInfo(ctx, false);
                        ImGui.EndMenu();
                    }
                    if (ImGui.BeginMenu($"{AppIcons.SI_Generic3Axis} Transform")) {
                        ctx.ShowTransformUI(meshContexts);
                        ImGui.EndMenu();
                    }
                    if (ctx.IsAnimatable && ImGui.BeginMenu($"{AppIcons.SI_FileType_FBXSKEL} Skeleton")) {
                        EnsureAnimationsInit();
                        ctx.ShowSkeletonPicker();
                        ImGui.EndMenu();
                    }
                    if (ctx.IsAnimatable && ImGui.BeginMenu($"{AppIcons.SI_Animation} Animation")) {
                        EnsureAnimationsInit();
                        ctx.ShowAnimSettings(meshContexts, ctx != meshContexts[0]);
                        ImGui.EndMenu();
                    }
                    if (ImGui.BeginMenu($"{AppIcons.SI_MeshViewerMeshGroup} Mesh Groups")) {
                        ShowMeshGroupSettings(ctx.Component);
                        ImGui.EndMenu();
                    }
                    if (ImGui.BeginMenu($"{AppIcons.SI_FileType_MDF} Material")) {
                        ctx.ShowMaterialSettings();
                        ImGui.EndMenu();
                    }
                    if (ImGui.MenuItem($"{AppIcons.SI_WindowOpenNew} Open In Standalone Window")) {
                        EditorWindow.CurrentWindow?.AddFileEditor(ctx.Handle);
                    }
                    ImGui.EndMenu();
                }
            }
            if (groupId++ >= 1) ImGui.Separator();
        }

        ImGui.SeparatorText("Add Mesh");
        if (addCollectionCtx == null) {
            addCollectionCtx = context.AddChild<MeshViewer, string>(
                "Source File",
                this,
                new ResourcePathPicker(Workspace, FileFilters.MeshFile, KnownFileFormats.Mesh) { Flags = ResourcePathPicker.PathPickerFlags.EditorOnly },
                (v) => v!.addCollectionPath,
                (v, p) => v.addCollectionPath = p ?? "");
        }
        addCollectionCtx.ShowUI();

        if (AppImguiHelpers.ShowRecentFiles(AppConfig.Settings.RecentMeshes, Workspace.Game, ref addCollectionPath)) {
            addCollectionCtx.ResetState();
        }
        if (!string.IsNullOrEmpty(addCollectionPath)) {
            if (Workspace.ResourceManager.TryResolveGameFile(addCollectionPath, out var resolvedFile) && resolvedFile.Format.format == KnownFileFormats.Mesh) {
                if (!meshContexts.Any(c => c.Handle == resolvedFile) && ImGui.Button($"{AppIcons.SI_GenericAdd} Add")) {
                    AppConfig.Settings.RecentMeshes.AddRecent(Workspace.Game, addCollectionPath);
                    CreateAdditionalMesh(resolvedFile).ChangeMesh(true);
                }
            } else {
                if (Path.IsPathFullyQualified(addCollectionPath)) {
                    ImGui.TextColored(Colors.Warning, """
                        Can't resolve .mesh file from the given path.
                        Re-check if you have the right file path and if the mesh is actually valid.
                        If it's a mesh with a streaming file, ensure both are extracted to a matching file path.
                        """u8);
                } else {
                    ImGui.TextColored(Colors.Warning, """
                        Can't resolve .mesh file from the given path.
                        Re-check if you have the right file path and if the mesh is actually valid.
                        """u8);
                }
            }
        }

        ImGui.SeparatorText("Manage Collection");
        if (ImGui.Selectable($"{AppIcons.SI_Save} Save collection")) {
            var collection = GetSerializedCollection();
            var collectionsDir = Path.Combine(AppConfig.Instance.GetGameUserPath(Workspace.Game), "mesh_collections");
            Directory.CreateDirectory(collectionsDir);
            PlatformUtils.ShowSaveFileDialog((path) => {
                using var fs = File.Create(path);
                JsonSerializer.Serialize(fs, collection, JsonConfig.configJsonOptions);
            }, Path.Combine(collectionsDir, Handle.Filename.ToString() + ".collection.json"), FileFilters.CollectionJsonFile);
        }
        if (ImGui.Selectable($"{AppIcons.SI_GenericImport} Load collection")) {
            var collectionsDir = Path.Combine(AppConfig.Instance.GetGameUserPath(Workspace.Game), "mesh_collections/");
            PlatformUtils.ShowFileDialog((files) => {
                MainLoop.Instance.InvokeFromUIThread(() => {
                    LoadCollection(files[0]);
                });
            }, collectionsDir, FileFilters.CollectionJsonFile);
        }
        if (ImGui.Selectable($"{AppIcons.SI_GenericClear} Remove all additional meshes")) {
            while (meshContexts.Count > 1) {
                RemoveSubmesh(meshContexts.Last());
            }
        }
    }

    private MeshCollection GetSerializedCollection()
    {
        var arr = new List<MeshCollectionItem>();
        foreach (var c in meshContexts) arr.Add(c.ToJson(meshContexts));
        return new MeshCollection(arr);
    }

    public void LoadCollection(string file)
    {
        try {
            using var fs = File.OpenRead(file);
            var coll =  Workspace.ResourceManager.CreateFileHandle(file, null, fs, true, true).Resource as MeshCollection;
            if (coll == null || coll.Items.Count == 0) return;
            LoadCollection(coll);
            AppConfig.Settings.RecentFiles.AddRecent(Workspace.Game, file);
        } catch (Exception e) {
            Logger.Error("Failed to load mesh collection: " + e.Message);
        }
    }

    public void LoadCollection(MeshCollection collection)
    {
        try {
            if (collection.Items.Count == 0) return;
            while (meshContexts.Count > 1) {
                RemoveSubmesh(meshContexts.Last());
            }
            if (meshContexts.Count == 0) {
                CreateAdditionalMesh(Handle);
            }
            meshContexts[0].LoadFromJson(collection.Items[0]);
            for (int i = 1; i < collection.Items.Count; i++) {
                CreateAdditionalMesh(Handle).LoadFromJson(collection.Items[i]!);
            }
            for (int i = 0; i < collection.Items.Count; i++) {
                var ctx = meshContexts[i];
                var ownerName = (collection.Items[i]!).Owner;
                if (!string.IsNullOrEmpty(ownerName)) {
                    EnsureAnimationsInit();
                    var anim = ctx.Animator!;
                    anim.owner = meshContexts.FirstOrDefault(c => c.ShortName == ownerName)?.Animator ?? meshContexts[0].Animator;
                }

                ctx.Animator?.owner ??= meshContexts[0].Animator;
                if (i != 0) {
                    if (ctx.Animator?.owner != null) {
                        var ownerGo = meshContexts.First(mc => mc.Animator == ctx.Animator!.owner).GameObject;
                        if (ownerGo != ctx.GameObject) {
                            ownerGo.AddChild(ctx.GameObject);
                        } else {
                            meshContexts[0].GameObject.AddChild(ctx.GameObject);
                        }
                    } else {
                        meshContexts[0].GameObject.AddChild(ctx.GameObject);
                    }
                }
            }
        } catch (Exception e) {
            Logger.Error("Failed to load mesh collection: " + e.Message);
        }
    }

    private UIContext? addCollectionCtx;
    private string addCollectionPath = "";

    private void ShowMeshGroupSettings(MeshComponent meshComponent)
    {
        var meshGroupIds = meshComponent.MeshHandle?.Meshes.Select(m => m.MeshGroup).Order().Distinct();
        var parts = RszFieldCache.Mesh.PartsEnable.Get(meshComponent.Data);
        if (meshGroupIds == null || parts == null) {
            ImGui.TextColored(Colors.Error, "Could not resolve enabled parts for current game.");
            return;
        }
        foreach (var group in meshGroupIds) {
            if (group < 0 || group >= parts.Count) continue;

            var enabled = (bool)parts[group];
            if (ImGui.Checkbox(group.ToString(), ref enabled)) {
                parts[group] = (object)enabled;
                meshComponent.RefreshIfActive();
            }
        }
    }

    private static void ShowMeshInfo(MeshViewerContext ctx, bool allowEditing)
    {
        var handle = ctx.Handle;
        string filepath = handle.Filepath;
        ImGui.InputText("Path", ref filepath, 255, ImGuiInputTextFlags.AutoSelectAll);
        if (ImGui.IsItemClicked()) {
            EditorWindow.CurrentWindow?.CopyToClipboard(handle.Filepath);
        }
        ImGui.SameLine();
        ImGui.TextDisabled($"({handle.HandleType})");
        var mesh = ctx.MeshFile;
        ImGui.Text($"Total Vertices: {mesh.VertexCount}");
        ImGui.Text($"Total Polygons: {mesh.PolyCount}");
        ImGui.Text($"Sub Meshes: {mesh.MeshCount}");
        ImGui.Text($"Materials: {mesh.MaterialCount}");
        ImGui.Text($"Bones: {mesh.BoneCount}");
        if (allowEditing) {
            ImGui.Separator();
            if (ImGui.TreeNode("Raw Data")) {
                var meshCtx = ctx.UI.GetChild<MeshFileHandler>();
                if (meshCtx == null) {
                    meshCtx = ctx.UI.AddChild("Raw Mesh Data", mesh.NativeMesh, new MeshFileHandler());
                }
                meshCtx.ShowUI();
                if (meshCtx.Changed && !handle.Modified) {
                    handle.Modified = true;
                }
                ImGui.TreePop();
            }
        }
    }

    private string? GetAnimErrors()
    {
        var mesh = meshContexts.First().MeshFile;
        if (mesh == null || !(mesh.NativeMesh.BoneData?.DeformBones.Count > 0) || !Animators.Any()) return null;

        var deformBoneLimit = mesh.NativeMesh.CurrentVersionConfig == null ? 0 : MeshFile.GetDeformBoneLimit(mesh.NativeMesh.CurrentVersionConfig);
        if (deformBoneLimit > 0 && mesh.NativeMesh.BoneData.DeformBones.Count > deformBoneLimit) {
            return $"""
                Mesh has too many deform bones, it will not work correctly ingame.
                At most {deformBoneLimit} are supported by the {mesh.NativeMesh.CurrentVersionConfig} format.
                """;
        }
        if (mesh.NativeMesh.BoneData.DeformBones.Count > 255) {
            return """
                Some animations might not be fully accurate in the preview.
                Mesh has more than 255 deform bones, the rest will not animate in the preview.
                To view full animations, export them to an external mesh editor.

                This is NOT an issue ingame, with the mesh itself, or the animation, but a Content Editor rendering limitation.
                """;
        }

        if (mesh.NativeMesh.MeshBuffer?.ExtraWeights?.Length > 0) {
            return """
                Some animations might not be fully accurate in the preview.
                The mesh contains an extra weight buffer, only the main one (6 or 8 weights depending on game) is currently supported for preview.
                To view full animations, export them to an external mesh editor.

                This is NOT an issue ingame, with the mesh itself, or the animation, but a Content Editor rendering limitation.
                """;
        }

        return null;
    }

    private void AlignMatNamesToMdf(MeshViewerContext ctx)
    {
        var meshComponent = ctx.Component;
        var mesh = ctx.Mesh;
        if (meshComponent.MeshHandle == null || mesh == null) return;

        var mdfMats = mesh.Material.Materials;
        var matNames = ctx.MeshFile.NativeMesh.MaterialNames;
        if (matNames.Count < mdfMats.Count) {
            var missingMats = mdfMats.Select(m => m.name).Where(m => !matNames.Contains(m));
            foreach (var mat in missingMats) {
                if (matNames.Count >= mdfMats.Count) break;
                matNames.Add(mat);
            }
        }
        Handle.Modified = true;
    }

    private MdfBatchExporter? mdfExporter;
    private void ShowImportExportMenu()
    {
        var mesh = meshContexts.First().MeshFile;
        if (mesh == null) return;

        if (Handle.Format.format == KnownFileFormats.Mesh) {
            DrawFileControls(data);
        }

        using var _ = ImguiHelpers.Disabled(exportInProgress);
        // note: this UI is getting crowded, rework into tabs?
        ShowImportExportControls(mesh);
        ShowTexControls();
        ShowMeshConvertControls(mesh);
        ShowConvertSkeletonControls(mesh);
    }

    private void ShowConvertSkeletonControls(CommonMeshResource mesh)
    {
        if (!(mesh.NativeMesh.BoneData?.Bones.Count > 0)) return;

        ImGui.SeparatorText("Convert Skeleton");
        if (ImGui.Button($"{AppIcons.SI_GenericConvert} Save Skeleton")) {
            var hasVersion = Workspace.Env.TryGetFileExtensionVersion("fbxskel", out var version)
                || Workspace.Env.TryGetFileExtensionVersion("skeleton", out version)
                || Workspace.Env.TryGetFileExtensionVersion("refskel", out version);
            if (!hasVersion) {
                Logger.Warn("Could not determine correct skeleton format version. You might need to manually specify the correct version.");
                version = 8;
            }

            var skeleton = new FbxSkelFile(new FileHandler());
            foreach (var bone in mesh.NativeMesh.BoneData.Bones.OrderBy(b => b.index)) {
                Matrix4x4.Decompose(bone.localTransform.ToSystem(), out var scale, out var rot, out var pos);
                skeleton.Bones.Add(new ReeLib.FbxSkel.RefBone() {
                    name = bone.name,
                    position = pos,
                    rotation = rot,
                    scale = scale,
                    symmetryIndex = (short)(bone.Symmetry?.index ?? bone.index),
                    parentIndex = (short)(bone.Parent?.index ?? -1),
                });
            }
            PlatformUtils.ShowSaveFileDialog((file) => {
                skeleton.WriteTo(file);
            }, null, new FileFilter("Skeleton File", $"skeleton.{version}", $"fbxskel.{version}", $"refskel.{version}"));
        }
    }

    private void ShowMeshConvertControls(CommonMeshResource mesh)
    {
        ImGui.SeparatorText("Convert Mesh");
        var conv1 = ImGui.Button($"{AppIcons.SI_GenericConvert}");
        ImguiHelpers.Tooltip("Convert");
        ImGui.SameLine();
        ImguiHelpers.ValueCombo("Mesh Version", MeshFile.AllVersionConfigsWithExtension, MeshFile.AllVersionConfigs, ref exportTemplate);
        var bundleConvert = Workspace.CurrentBundle != null && ImguiHelpers.SameLine() && ImGui.Button("Convert to bundle ...");
        if (conv1 || bundleConvert) {
            var ver = MeshFile.GetFilePathVersion(exportTemplate);
            var ext = $".mesh.{ver}";
            var defaultFilename = PathUtils.GetFilenameWithoutExtensionOrVersion(Handle.Filepath).ToString() + ext;
            if (mesh.NativeMesh.Header.version == 0) {
                mesh.NativeMesh.ChangeVersion(exportTemplate);
            }
            var exportMesh = mesh.NativeMesh.RewriteClone(Workspace);
            exportMesh.ChangeVersion(exportTemplate);
            if (bundleConvert) {
                var tempres = new CommonMeshResource(defaultFilename, Workspace.Env) { NativeMesh = exportMesh };
                ResourcePathPicker.ShowSaveToBundle(Handle.Loader, tempres, Workspace, defaultFilename, Handle.NativePath);
            } else {
                PlatformUtils.ShowSaveFileDialog((path) => exportMesh.SaveAs(path), defaultFilename);
            }
        }
    }

    private void ShowTexControls()
    {
        if (meshContexts.Any(mc => mc.HasValidLoadedMdf2)) {
            ImGui.SeparatorText("Texture Export");
            if (ImGui.Button($"{AppIcons.SI_FileType_TEX} Batch export textures")) {
                mdfExporter = new MdfBatchExporter();
                ImGui.OpenPopup("Batch MDF exporter");
            }
        }

        if (mdfExporter != null) {
            if (ImGui.BeginPopupModal("Batch MDF exporter")) {
                if (mdfExporter.Show(meshContexts.Select(mc => mc.MaterialFile!).Where(x => x != null))) {
                    ImGui.CloseCurrentPopup();
                    mdfExporter = null;
                }
                ImGui.EndPopup();
            }
        }
    }

    private void ShowImportExportControls(CommonMeshResource mesh)
    {
        if (ImGui.Button($"{AppIcons.SI_GenericExport} Export Mesh")) {
            // potential export enhancement: include (embed?) textures
            if (meshContexts.FirstOrDefault()?.MeshFile is CommonMeshResource assmesh) {
                PlatformUtils.ShowSaveFileDialog((exportPath) => {
                    exportInProgress = true;
                    try {
                        var animator = PrimaryAnimator;
                        IEnumerable<MotFileBase>? mots = null;
                        if (exportAnimations && animator?.File != null) {
                            if (exportCurrentAnimationOnly && animator.ActiveMotion != null) {
                                mots = [animator.ActiveMotion];
                            } else if (animator.File.Format.format == KnownFileFormats.Motion) {
                                mots = [animator.File.GetFile<MotFile>()];
                            } else {
                                mots = animator.File.GetFile<MotlistFile>().MotFiles;
                            }
                        }

                        assmesh.ExportToFile(exportPath, exportLods, exportOcclusion, exportFbxskel ? PrimaryAnimator?.skeleton : null, mots, exportFullCollection ? meshContexts.Skip(1).Select(c => c.MeshFile) : null);
                    } catch (Exception e) {
                        Logger.Error(e, "Mesh export failed");
                    } finally {
                        exportInProgress = false;
                    }
                }, PathUtils.GetFilenameWithoutExtensionOrVersion(Handle.Filename).ToString(), FileFilters.MeshFile);
            } else {
                throw new NotImplementedException();
            }
        }
        if (Handle.Format.format == KnownFileFormats.Mesh) {
            ImGui.SameLine();
            if (ImGui.Button($"{AppIcons.SI_GenericImport} Import From File")) {
                var window = EditorWindow.CurrentWindow!;
                PlatformUtils.ShowFileDialog((files) => {
                    window.InvokeFromUIThread(() => {
                        lastImportSourcePath = files[0];
                        if (Workspace.ResourceManager.TryForceLoadFile(lastImportSourcePath, out var importedFile)) {
                            using var _ = importedFile;
                            var lodData = new MeshLodData().Read(meshContexts.FirstOrDefault()?.MeshFile.NativeMesh);
                            var importAsset = importedFile.GetResource<CommonMeshResource>();
                            var tmpHandler = new FileHandler(new MemoryStream(), Handle.Filepath);
                            importAsset.NativeMesh.WriteTo(tmpHandler, false);
                            Handle.Stream = tmpHandler.Stream.ToMemoryStream(disposeStream: false, forceCopy: true);
                            Handle.Revert(Workspace);
                            Handle.Modified = true;
                            ChangeMainMesh();
                            var ctx = meshContexts.First();
                            ctx.AlignMatNamesToMdf();
                            lodData.Apply(ctx.MeshFile.NativeMesh);
                        }
                    });
                }, lastImportSourcePath, filters: FileFilters.MeshFilesAll);
            }
        }
        ImGui.SameLine();
        ImguiHelpers.ToggleButton($"{AppIcons.SI_Settings}", ref showImportSettings, Colors.IconActive);
        ImguiHelpers.Tooltip("Show Settings");

        ImGui.SameLine();
        AppImguiHelpers.WikiLinkButton("https://github.com/kagenocookie/REE-Content-Editor/wiki/Mesh-editing", true);

        if (exportInProgress) {
            ImGui.SameLine();
            // we have no way of showing any progress from assimp's side (which is 99% of the export duration) so this is the best we can do
            ImGui.TextWrapped($"Exporting in progress. This may take a while for large files and for many animations...");
        }
        if (PrimaryAnimator?.File != null) {
            ImGui.Checkbox("Include animations", ref exportAnimations);
            if (exportAnimations) ImGui.Checkbox("Selected animation only", ref exportCurrentAnimationOnly);
        }
        if (PrimaryAnimator?.skeleton != null) {
            ImGui.Checkbox("Export with merged skeleton", ref exportFbxskel);
            ImguiHelpers.Tooltip("Whether to merge the mesh skeleton with the currently active skeleton file");
        }
        if (meshContexts.Count > 1) {
            ImGui.Checkbox("Export all meshes", ref exportFullCollection);
            ImguiHelpers.Tooltip("Whether to include all currently open meshes in the exported file");
        }
        if (mesh.NativeMesh.MeshData?.LODs.Count > 1 || mesh.NativeMesh.ShadowMesh?.LODs.Count > 0) ImGui.Checkbox("Include LODs and Shadow Mesh", ref exportLods);
        if (mesh.NativeMesh.OccluderMesh?.MeshGroups.Count > 0) ImGui.Checkbox("Include Occlusion Mesh", ref exportOcclusion);

        if (showImportSettings) {
            ImGui.SeparatorText("Import Settings");
            var scale = AppConfig.Settings.Import.Scale;
            if (ImGui.InputFloat("Import Scale", ref scale, "%.2f")) {
                AppConfig.Settings.Import.Scale = Math.Clamp(scale, 0.001f, 100);
                AppConfig.Settings.Save();
            }

            var rootId = AppConfig.Settings.Import.ForceRootIdentity;
            if (ImGui.Checkbox("Reset Root Orientation"u8, ref rootId)) {
                AppConfig.Settings.Import.ForceRootIdentity = rootId;
                AppConfig.Settings.Save();
            }
            ImguiHelpers.Tooltip("Forces the root bone into the default (Identity) orientation on import.\nUse if you find that only your the skeleton is fully rotated along an axis in your imported file"u8);
            var convertYUp = AppConfig.Settings.Import.ConvertZToYUpRootRotation;
            if (ImGui.Checkbox("Transform Z-Up -> Y-Up root axis for animations", ref convertYUp)) {
                AppConfig.Settings.Import.ConvertZToYUpRootRotation = convertYUp;
                AppConfig.Settings.Save();
            }
            ImguiHelpers.Tooltip("For modelling apps that don't know how to export with Y axis as up, this might fix the rotations of imported meshes."u8);
            var meshMatName = AppConfig.Settings.Import.ImportMaterialsFromMeshName;
            if (ImGui.Checkbox("Read material names from mesh name", ref meshMatName)) {
                AppConfig.Settings.Import.ImportMaterialsFromMeshName = meshMatName;
                AppConfig.Settings.Save();
            }
            ImguiHelpers.Tooltip("Enable this to import the material names from the mesh name (separated with double underscore e.g. `Group_1__Head_mat`).\nIf unchecked, the actual material name is used instead."u8);

            ImGui.SeparatorText("Export Settings"u8);
            scale = AppConfig.Settings.Import.ExportScale;
            if (ImGui.InputFloat("Export Scale"u8, ref scale, "%.2f")) {
                AppConfig.Settings.Import.ExportScale = Math.Clamp(scale, 0.001f, 100);
                AppConfig.Settings.Save();
            }
            ImguiHelpers.Tooltip("Scale up all vertices and animation positions for exported meshes.\nOnly used for FBX because GLB/GLTF already has functional units"u8);

            var bonesForW2 = AppConfig.Settings.Import.ExportSecondaryWeightAsBones;
            if (ImGui.Checkbox("Export secondary weight marker as bones"u8, ref bonesForW2)) {
                AppConfig.Settings.Import.ExportSecondaryWeightAsBones = bonesForW2;
                AppConfig.Settings.Save();
            }
            ImguiHelpers.Tooltip("Whether to export the bone secondary weight flag indicators as child bones.\nIf unchecked, additional empty nodes will instead be placed at the scene root.\nSee wiki for more details."u8);

            ImGui.Spacing();
            ImGui.Spacing();
        }
    }

    private bool UpdateAnimData()
    {
        EnsureAnimationsInit();
        foreach (var c in meshContexts) {
            UpdateAnimatorMesh(c.Component, false);
        }
        return true;
    }

    private void ShowRootAnimationList()
    {
        _animationListContext ??= meshContexts.FirstOrDefault();
        if (_animationListContext == null) return;

        var animControllers = meshContexts.Where(mc => mc.Animator?.ActiveOwner == null).ToArray();
        if (animControllers.Skip(1).Any()) {
            var names = animControllers.Select(c => c.ShortName).ToArray();
            ImguiHelpers.ValueCombo("Controller", names, animControllers, ref _animationListContext);
        }

        ShowAnimationMenu(_animationListContext);
    }
    private void ShowAnimationMenu(MeshViewerContext ctx)
    {
        ImGui.SeparatorText("Animations");
        UpdateAnimData();

        var animator = PrimaryAnimator;
        if (animator == null) return;
        ImguiHelpers.ToggleButton($"{AppIcons.SI_FileType_FBXSKEL}", ref showSkeleton, Colors.IconActive);
        ImguiHelpers.Tooltip("Show Skeleton"u8);
        ImGui.SameLine();
        using (var _ = ImguiHelpers.Disabled(string.IsNullOrEmpty(ctx.animationSourceFile))) {
            if (ImGui.Button($"{AppIcons.SI_Update}") && animator.File != null) {
                Workspace.ResourceManager.CloseFile(animator.File);
                foreach (var c in meshContexts) c.Animator?.Unload();
                SetAnimation(ctx.animationSourceFile);
            }
            ImguiHelpers.Tooltip("Force reload");
        }
        ImGui.SameLine();
        AppImguiHelpers.WikiLinkButton("https://github.com/kagenocookie/REE-Content-Editor/wiki/Animation-tools", true);

        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize("Animation File").X);
        ctx.ShowAnimSettings(meshContexts, false);
    }

    private void RenderSkeleton(MeshViewerContext ctx)
    {
        var mcomp = ctx.Component;
        var mesh = ctx.Mesh;
        if (mesh == null || scene == null || mcomp == null) return;

        if (_skeletonBuilder == null) {
            _skeletonBuilder = new GizmoShapeBuilder(new GizmoState(scene, new GizmoContainer(scene, mcomp)));
            _skeletonBuilder.SetGeometryType(ShapeBuilder.GeometryType.Line);
        }

        _skeletonBuilder.ClearShapes();
        _skeletonBuilder.Push();
        foreach (var bone in mesh.Bones!.Bones) {
            var transform = mesh.BoneMatrices[bone.index];
            var parent = bone.Parent;
            var parentTransform = bone.parentIndex == -1 ? Matrix4x4.Identity : mesh.BoneMatrices[bone.parentIndex];

            _skeletonBuilder.Add(new Capsule(
                parentTransform.Translation,
                transform.Translation,
                0.001f));

            _skeletonBuilder.Add(new Sphere(
                transform.Translation,
                0.005f));

            if (bone.Children.Count == 0) {
                var len = transform.Translation - parentTransform.Translation;
                var tip = transform.Translation + Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitZ, transform)) * len;
                _skeletonBuilder.Add(new Capsule(
                    transform.Translation,
                    tip,
                    0.001f));

                _skeletonBuilder.Add(new Sphere(tip, 0.005f));
            }
        }

        _skeletonBuilder.UpdateMesh();
        if (gizmoMeshHandle == null) {
            gizmoMeshHandle = new MeshHandle(new MeshResourceHandle(_skeletonBuilder.mesh!));
            var material = scene.RenderContext.GetMaterialBuilder(BuiltInMaterials.MonoColor, "skeleton")
                .Color("_MainColor", new Color(0xff, 0xff, 0, 0x77)).Blend();
            gizmoMeshHandle.SetMaterials(new MaterialGroup(material));
            scene.RenderContext.StoreMesh(gizmoMeshHandle.Handle);
        }
        (scene.RenderContext as OpenGLRenderContext)?.Batch.Gizmo
            .Add(new GizmoRenderBatchItem(gizmoMeshHandle.GetMaterial(0), gizmoMeshHandle.GetMesh(0), Matrix4x4.Identity, gizmoMeshHandle.GetMaterial(0)));
    }

    private bool UpdateAnimatorMesh(MeshComponent meshComponent, bool showErrorIfInvalid)
    {
        var mesh = meshComponent.MeshHandle;
        var motion = meshComponent.GameObject.GetComponent<Motion>();
        var animator = motion?.Animator;
        // note: this should probably be handled directly within Motion component?
        if (animator != null && animator.Mesh != mesh) {
            if (mesh is not AnimatedMeshHandle anim) {
                if (showErrorIfInvalid) {
                    ImGui.BeginChild("PlaybackError", new Vector2(300, 42), ImGuiChildFlags.AlwaysUseWindowPadding | ImGuiChildFlags.Borders | ImGuiChildFlags.AutoResizeY | ImGuiChildFlags.AlwaysAutoResize);
                    ImGui.TextColored(Colors.Error, "Mesh is not animatable");
                    ImGui.EndChild();
                }
                return false;
            }

            animator.SetMesh(anim);
        }
        return true;
    }

    private void ShowPlaybackControls(MeshComponent meshComponent)
    {
        var animator = PrimaryAnimator;
        if (animator?.ActiveMotion == null) return;
        if (!UpdateAnimatorMesh(meshComponent, true)) return;

        var windowSize = ImGui.GetWindowSize();
        var timestamp = $"{animator.CurrentTime:0.00} / {animator.TotalTime:0.00} ({animator.CurrentFrame:000} / {animator.TotalFrames:000})";
        var timestampSize = ImGui.CalcTextSize(timestamp) + new Vector2(5 * 48, 0);
        if (AppConfig.Instance.UseFullscreenAnimPlayback) {
            ImGui.SetCursorPos(new Vector2(20, ImGui.GetWindowHeight() - ImGui.GetContentRegionAvail().Y - ImGui.GetStyle().WindowPadding.Y - 80));
        } else {
            ImGui.SetCursorPos(new Vector2(windowSize.X - timestampSize.X - ImGui.GetStyle().WindowPadding.X * 2 - 100, TopMargin));
        }

        ImGui.PushStyleColor(ImGuiCol.ChildBg, ImguiHelpers.GetColor(ImGuiCol.WindowBg) with { W = 0.5f });
        ImGui.BeginChild("PlaybackControls", new Vector2(AppConfig.Instance.UseFullscreenAnimPlayback ? ImGui.GetContentRegionAvail().X - ImGui.GetStyle().WindowPadding.X : timestampSize.X + 50, 80), ImGuiChildFlags.AlwaysUseWindowPadding | ImGuiChildFlags.Borders | ImGuiChildFlags.AutoResizeY | ImGuiChildFlags.AlwaysAutoResize);

        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 4);
        if (ImGui.Button((animator.IsPlaying ? AppIcons.Pause : AppIcons.Play).ToString()) || ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows) && AppConfig.Instance.Key_MeshViewer_PauseAnim.Get().IsPressed() && !ImGui.GetIO().WantCaptureKeyboard) {
            if (animator.IsPlaying) {
                foreach (var c in meshContexts) c.Animator?.Pause();
            } else {
                foreach (var c in meshContexts) c.Animator?.Play();
            }
        }
        ImguiHelpers.Tooltip(animator.IsPlaying ? "Pause" : "Play");

        ImGui.SameLine();
        using (var _ = ImguiHelpers.Disabled(animator.CurrentTime == 0)) {
            if (ImGui.Button(AppIcons.SeekStart.ToString())) {
                foreach (var c in meshContexts) c.Animator?.Restart();
            }
        }
        ImguiHelpers.Tooltip("Restart");

        ImGui.SameLine();
        using (var _ = ImguiHelpers.Disabled(!animator.IsPlaying && !animator.IsActive)) {
            ImGui.PushStyleColor(ImGuiCol.Text, Colors.IconTertiary);
            if (ImGui.Button(AppIcons.Stop.ToString())) {
                foreach (var c in meshContexts) c.Animator?.Stop();
            }
            ImGui.PopStyleColor();
        }
        ImguiHelpers.Tooltip("Stop");

        ImGui.SameLine();
        using (var _ = ImguiHelpers.Disabled(!animator.IsActive)) {
            if (ImGui.Button(AppIcons.Previous.ToString()) || ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows) && AppConfig.Instance.Key_MeshViewer_PrevAnimFrame.Get().IsPressed()) {
                foreach (var c in meshContexts) {
                    var anim = c.Animator;
                    if (anim == null) continue;
                    anim.Pause();
                    anim.Seek((anim.CurrentFrame - 1) * anim.FrameDuration);
                    anim.Update(0);
                }
            }
        }
        ImguiHelpers.Tooltip("Previous Frame");

        ImGui.SameLine();
        using (var _ = ImguiHelpers.Disabled(!animator.IsActive)) {
            if (ImGui.Button(AppIcons.Next.ToString()) || ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows) && AppConfig.Instance.Key_MeshViewer_NextAnimFrame.Get().IsPressed()) {
                foreach (var c in meshContexts) {
                    var anim = c.Animator;
                    if (anim == null) continue;
                    anim.Pause();
                    anim.Seek((anim.CurrentFrame + 1) * anim.FrameDuration);
                    anim.Update(0);
                }
            }
        }
        ImguiHelpers.Tooltip("Next Frame");

        ImGui.SameLine();
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - 4);
        ImGui.Text(timestamp);

        using (var _ = ImguiHelpers.Disabled(!animator.IsActive)) {
            int frame = animator.CurrentFrame;
            ImGui.SetNextItemWidth(AppConfig.Instance.UseFullscreenAnimPlayback ? ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ItemSpacing.X - 75: 325);
            if (ImGui.SliderInt("##AnimFrameSlider", ref frame, 0, animator.TotalFrames)) {
                foreach (var c in meshContexts) {
                    var anim = c.Animator;
                    if (anim == null) continue;

                    if (AppConfig.Instance.PauseAnimPlayerOnSeek) {
                        anim.Pause();
                    }
                    anim.Seek(frame * anim.FrameDuration);
                    anim.Update(0);
                }
            }
        }
        ImGui.SameLine();
        if (ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows)) {
            int idx = Array.IndexOf(PlaybackSpeeds, playbackSpeed);

            if (AppConfig.Instance.Key_MeshViewer_IncreaseAnimSpeed.Get().IsPressed() && idx < PlaybackSpeeds.Length - 1) {
                playbackSpeed = PlaybackSpeeds[idx + 1];
            }

            if (AppConfig.Instance.Key_MeshViewer_DecreaseAnimSpeed.Get().IsPressed() && idx > 0) {
                playbackSpeed = PlaybackSpeeds[idx - 1];
            }
        }
        ImGui.SetNextItemWidth(75);
        if (ImGui.BeginCombo("##PlaybackSpeed", $"{playbackSpeed:0.##}x")) {
            for (int i = 0; i < PlaybackSpeeds.Length; i++) {
                bool selected = playbackSpeed == PlaybackSpeeds[i];
                if (ImGui.Selectable($"{PlaybackSpeeds[i]:0.##}x", selected)) {
                    playbackSpeed = PlaybackSpeeds[i];
                }
                if (selected) ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }
        ImguiHelpers.Tooltip("Playback Speed");

        ImGui.EndChild();
        ImGui.PopStyleColor();

        if (animator.IsPlaying && UpdateAnimData()) {
            foreach (var c in meshContexts) {
                c.Animator?.Update(Time.Delta * playbackSpeed);
            }
        } else if (animator.IsActive) {
            foreach (var c in meshContexts) {
                c.Animator?.Update(0);
            }
        }
    }

    public void SetAnimation(string animlist)
    {
        EnsureAnimationsInit();
        UpdateAnimData();
        foreach (var c in meshContexts) {
            var anim = c.Animator!;
            anim.LoadAnimationList(animlist);
            if (anim.Animations.Any()) {
                anim.SetActiveMotion(anim.Animations[0]);
            }
        }
    }

    public void SetAnimation(MotFile mot)
    {
        EnsureAnimationsInit();
        UpdateAnimData();
        foreach (var c in meshContexts) {
            var anim = c.Animator!;
            anim.SetActiveMotion(mot);
        }
    }

    public static bool IsSupportedFileExtension(string filepathOrExtension)
    {
        var format = PathUtils.ParseFileFormat(filepathOrExtension);
        if (format.format == KnownFileFormats.Mesh) return true;
        if (format.format != KnownFileFormats.Unknown) return false;

        return MeshLoader.StandardFileExtensions.Contains(Path.GetExtension(filepathOrExtension).ToLowerInvariant());
    }

    protected override void Dispose(bool disposing)
    {
        Handle?.References.Remove(this);
        if (scene != null) {
            _skeletonBuilder?.Dispose();
            _skeletonBuilder = null;
            EditorWindow.CurrentWindow?.SceneManager.UnloadScene(scene);
            scene = null;
        }
        meshContexts.Clear();
    }
    public void SyncFromScene(MeshViewer other, bool ignoreMotionClip)
    {
        if (scene == null || other.scene == null) return;

        scene.ActiveCamera.Transform.CopyFrom(other.scene.ActiveCamera.Transform);
        scene.ActiveCamera.ProjectionMode = other.scene.ActiveCamera.ProjectionMode;
        scene.ActiveCamera.NearPlane = other.scene.ActiveCamera.NearPlane;
        scene.ActiveCamera.FarPlane = other.scene.ActiveCamera.FarPlane;
        scene.ActiveCamera.FieldOfView = other.scene.ActiveCamera.FieldOfView;
        scene.ActiveCamera.OrthoSize = other.scene.ActiveCamera.OrthoSize;

        var mainCtx = meshContexts.FirstOrDefault();
        var otherAnimator = other.PrimaryAnimator;
        if (otherAnimator != null && mainCtx != null) {
            var animator = PrimaryAnimator ?? mainCtx.SetupAnimator();
            if (!ignoreMotionClip && animator.ActiveMotion != otherAnimator.ActiveMotion && otherAnimator.ActiveMotion != null) {
                animator.SetActiveMotion(otherAnimator.ActiveMotion);
            }
            if (otherAnimator.IsPlaying != animator.IsPlaying) {
                if (otherAnimator.IsPlaying) {
                    animator.Play();
                } else {
                    animator.Pause();
                }
            }
            animator.Seek(otherAnimator.CurrentTime);
        }

        isSynced = true;
    }

    private class MeshLodData
    {
        public uint lodHash;
        public List<float> lodFactors = new();
        public List<float> shadowLodFactors = new();

        public MeshLodData()
        {
        }

        public MeshLodData Read(MeshFile? mesh)
        {
            if (mesh == null) return this;
            lodHash = mesh.Header.lodHash;
            lodFactors = mesh.MeshData?.LODs.Select(lod => lod.lodFactor).ToList() ?? new();
            shadowLodFactors = mesh.ShadowMesh?.LODs.Select(lod => lod.lodFactor).ToList() ?? new();
            return this;
        }

        public void Apply(MeshFile mesh)
        {
            mesh.Header.lodHash = lodHash;
            for (int i = 0; i < lodFactors.Count && i < mesh.MeshData?.LODs.Count; i++) {
                mesh.MeshData.LODs[i].lodFactor = lodFactors[i];
            }
            for (int i = 0; i < lodFactors.Count && i < mesh.ShadowMesh?.LODs.Count; i++) {
                mesh.ShadowMesh.LODs[i].lodFactor = lodFactors[i];
            }
        }
    }
}

internal class MeshViewerContext(MeshViewer viewer, UIContext ui, FileHandle file)
{
    private string? loadedMdf;
    private string? mdfSource;
    private string? originalMDF;
    private string skeletonPath = "";
    private UIContext? mdfPickerContext;
    private bool useHighResTextures = true;

    public bool HasValidLoadedMdf2 => !string.IsNullOrEmpty(loadedMdf);
    public MdfFile? MaterialFile => string.IsNullOrEmpty(loadedMdf) ? null : viewer.Workspace.ResourceManager.GetFileHandle(loadedMdf)?.GetFile<MdfFile>();

    public UIContext UI { get; } = ui;
    public FileHandle Handle { get; set; } = file;
    private ContentWorkspace Workspace => viewer.Workspace;

    public GameObject GameObject { get; } = ui.Get<GameObject>();
    public MeshComponent Component { get; } = ui.Get<GameObject>().GetOrAddComponent<MeshComponent>();
    public Motion Motion { get; } = ui.Get<GameObject>().GetOrAddComponent<Motion>();
    public AnimatedMeshHandle? Mesh => Component.MeshHandle as AnimatedMeshHandle;
    public CommonMeshResource MeshFile => Handle.GetResource<CommonMeshResource>() ?? throw new Exception("invalid mesh");
    public Animator? Animator => GameObject.GetComponent<Motion>()?.Animator;
    public MeshViewerContext? FindOwner(IEnumerable<MeshViewerContext> ctx) => Animator?.owner == null ? null : ctx.FirstOrDefault(c => c.Animator == Animator?.owner);
    public bool IsAnimatable => Mesh != null;
    public string ShortName => Handle.Filename.ToString();

    public override string ToString() => Handle.Filepath;

    public Animator SetupAnimator()
    {
        var mot = Motion;
        if (mot.Animator != null) return mot.Animator;
        mot.InitAnimation();
        return mot.Animator!;
    }

    public void UpdateMaterial(bool force = false)
    {
        var meshComponent = Component;
        var mesh = meshComponent.MeshHandle;
        if (loadedMdf != mdfSource || force) {
            loadedMdf = mdfSource;
            if (string.IsNullOrEmpty(mdfSource)) {
                meshComponent.SetMesh(Handle, Handle);
            } else if (viewer.Workspace.ResourceManager.TryResolveGameFile(mdfSource, out var mdfHandle)) {
                meshComponent.SetMesh(Handle, mdfHandle);
            } else {
                meshComponent.SetMesh(Handle, Handle);
                if (mdfSource != originalMDF) {
                    Logger.Error("Could not locate mdf2 file " + mdfSource);
                }
            }
        }
    }

    public void TryGuessMdfFilepath()
    {
        if (!viewer.Workspace.Env.TryGetFileExtensionVersion("mdf2", out var mdfVersion)) {
            mdfVersion = -1;
        }

        var meshBasePath = PathUtils.GetFilepathWithoutExtensionOrVersion(Handle.Filepath).ToString();
        var ext = ".mdf2";
        if (mdfVersion != -1) ext += "." + mdfVersion;

        var mdfPath = meshBasePath + ext;
        if (!File.Exists(mdfPath)) mdfPath = meshBasePath + "_Mat" + ext;

        if (!File.Exists(mdfPath) && Handle.NativePath != null) {
            meshBasePath = PathUtils.GetFilepathWithoutExtensionOrVersion(Handle.NativePath).ToString();
            if (viewer.Workspace.ResourceManager.TryResolveGameFile(meshBasePath + ext, out _)) {
                mdfPath = meshBasePath + ext;
            } else if (viewer.Workspace.ResourceManager.TryResolveGameFile(meshBasePath + "_Mat" + ext, out _)) {
                mdfPath = meshBasePath + "_Mat" + ext;
            } else if (viewer.Workspace.ResourceManager.TryResolveGameFile(meshBasePath + "_00" + ext, out _)) {
                mdfPath = meshBasePath + "_00" + ext;
            } else {
                mdfPath = "";
            }
        }
        mdfSource = mdfPath;
        originalMDF = mdfPath;
        loadedMdf = null;
    }

    private void ApplyMeshChanges()
    {
        Handle.Modified = true;
        ChangeMesh(false);
    }

    public void ShowMaterialSettings()
    {
        var meshComponent = Component;
        if (ImguiHelpers.ToggleButton("Textures: " + (useHighResTextures ? "Hi-Res" : "Low-Res"), ref useHighResTextures, Colors.IconActive)) {
            meshComponent.UseStreamingTex = useHighResTextures;
            UpdateMaterial(true);
        }
        ImGui.SameLine();
        if (ImGui.Button($"{AppIcons.SI_ResetMaterial}")) {
            mdfSource = originalMDF;
            UpdateMaterial();
        }
        ImguiHelpers.Tooltip("Reset MDF"u8);
        if (mdfPickerContext == null) {
            mdfPickerContext = UI.AddChild<MeshViewerContext, string>(
                "MDF2 Material",
                this,
                new ResourcePathPicker(viewer.Workspace, KnownFileFormats.MeshMaterial) { Flags = ResourcePathPicker.PathPickerFlags.EditorOnly },
                (v) => v!.mdfSource,
                (v, p) => v.mdfSource = p ?? "");
        }
        mdfPickerContext.ShowUI();
        var mesh = MeshFile;
        if (mesh != null && meshComponent.MeshHandle?.Material != null) {
            var mdfMats = meshComponent.MeshHandle.Material.Materials;
            var matNames = mesh.NativeMesh.MaterialNames;
            var nameMismatch = matNames.Any(name => !mdfMats.Select(m => m.name).Contains(name));
            var error = GetMdfErrors();
            if (error != null) {
                ImGui.TextColored(Colors.Warning, error);
            }

            if (mesh.NativeMesh.MaterialNames.Count < mdfMats.Count && !string.IsNullOrEmpty(mdfSource)) {
                if (ImGui.Button("Add missing materials from selected MDF2")) {
                    AlignMatNamesToMdf();
                }
            }

            ImGui.SeparatorText("Material mapping");
            for (int i = 0; i < mesh.NativeMesh.MaterialNames.Count; i++) {
                var matName = mesh.NativeMesh.MaterialNames[i];
                ImGui.PushStyleColor(ImGuiCol.Text, Colors.IconTertiary);
                if (ImGui.Button($"{AppIcons.SI_GenericDelete2}##{i}")) {
                    var i_backup = i--;
                    UndoRedo.RecordCallback(null, () => matNames.RemoveAt(i_backup), () => matNames.Insert(i_backup, matName));
                    UndoRedo.AttachCallbackToLastAction(UndoRedo.CallbackType.Both, ApplyMeshChanges);
                    continue;
                }
                ImGui.PopStyleColor();
                ImGui.SameLine();
                ImGui.Text(i.ToString());
                ImGui.SameLine();
                if (ImGui.InputText($"##{i}", ref matName, 48)) {
                    var prevName = mesh.NativeMesh.MaterialNames[i];
                    int i_backup = i;
                    UndoRedo.RecordCallbackSetter(null, mesh.NativeMesh.MaterialNames, prevName, matName, (o, v) => o[i_backup] = v, $"MatName{i}_{Handle.Filepath}");
                    UndoRedo.AttachCallbackToLastAction(UndoRedo.CallbackType.Both, ApplyMeshChanges);
                }
            }
            if (ImGui.Button($"{AppIcons.SI_GenericAdd} Add")) {
                var newName = "NewMaterial".GetUniqueName(s => matNames.Contains(s));
                UndoRedo.RecordCallback(null, () => matNames.Add(newName), () => matNames.Remove(newName));
                UndoRedo.AttachCallbackToLastAction(UndoRedo.CallbackType.Both, ApplyMeshChanges);
            }
        }
    }

    public string? GetMdfErrors()
    {
        var meshComponent = Component;
        var mesh = MeshFile;
        if (meshComponent?.MeshHandle == null || mesh == null || string.IsNullOrEmpty(mdfSource)) return null;

        if (mesh != null && meshComponent.MeshHandle?.Material != null) {
            var mdfMats = meshComponent.MeshHandle.Material.Materials;
            var matNames = mesh.NativeMesh.MaterialNames;
            var nameMismatch = matNames.Any(name => !mdfMats.Select(m => m.name).Contains(name));
            if (mdfMats.Count != mesh.NativeMesh.MaterialNames.Count) {
                return "Mesh material count does not match MDF2 material count. Textures won't display correctly ingame.\nEnsure that both counts match.";
            }
            if (nameMismatch) {
                return "Mesh references material names that are not present in the selected MDF2.";
            }
        }

        return null;
    }

    public void AlignMatNamesToMdf()
    {
        var meshComponent = Component;
        var mesh = Mesh;
        if (meshComponent.MeshHandle == null || mesh == null) return;

        var mdfMats = mesh.Material.Materials;
        var matNames = MeshFile.NativeMesh.MaterialNames;
        if (matNames.Count < mdfMats.Count) {
            var missingMats = mdfMats.Select(m => m.name).Where(m => !matNames.Contains(m));
            foreach (var mat in missingMats) {
                if (matNames.Count >= mdfMats.Count) break;
                matNames.Add(mat);
            }
        }
        Handle.Modified = true;
    }
    public void ChangeMesh(bool resetMdf = true)
    {
        if (!Handle.References.Contains(viewer)) Handle.References.Add(viewer);
        var isInitial = !Component.HasMesh;
        var mesh = MeshFile;
        if (mesh == null) {
            if (Handle.Filepath.Contains("streaming/", StringComparison.OrdinalIgnoreCase)) {
                Logger.Error("Can't directly open streaming meshes. Open the non-streaming file instead.");
            }
            return;
        }
        if (resetMdf) TryGuessMdfFilepath();

        var meshComponent = Component;
        if (isInitial) {
            meshComponent.UseStreamingTex = true;
        }

        meshComponent.Transform.InvalidateTransform();
        if (!string.IsNullOrEmpty(mdfSource)) {
            UpdateMaterial(true);
        } else {
            meshComponent.SetMesh(Handle, Handle);
        }
    }

    public void ShowSkeletonPicker()
    {
        var animator = Animator;
        if (animator == null) return;
        var ctx = UI.GetChild("Skeleton") ?? UI.AddChild("Skeleton", this,
            new ResourcePathPicker(viewer.Workspace, KnownFileFormats.Skeleton, KnownFileFormats.FbxSkeleton, KnownFileFormats.RefSkeleton) { Flags = ResourcePathPicker.PathPickerFlags.EditorOnly },
            (v) => v!.skeletonPath,
            (v, p) => v.skeletonPath = p ?? ""
        );

        ctx.ShowUI();

        if (AppImguiHelpers.ShowRecentFiles(AppConfig.Settings.RecentSkeletons, viewer.Workspace.Game, ref skeletonPath)) {
            ctx.ResetState();
        }

        if (string.IsNullOrEmpty(skeletonPath)) {
            if (animator.skeleton != null) {
                animator.SetSkeleton(null);
            }
        } else if (Workspace.ResourceManager.TryResolveGameFile(skeletonPath, out var resolvedFile) &&
            resolvedFile.GetFile<FbxSkelFile>() != animator.skeleton
        ) {
            AppConfig.Settings.RecentSkeletons.AddRecent(viewer.Workspace.Game, skeletonPath);
            animator.SetSkeleton(resolvedFile.GetFile<FbxSkelFile>());
        }
    }

    public MeshCollectionItem ToJson(IEnumerable<MeshViewerContext> meshContexts)
    {
        return new MeshCollectionItem() {
            Mesh = Handle.Filepath,
            Skeleton = skeletonPath,
            Material = loadedMdf,
            Owner = FindOwner(meshContexts)?.ShortName ?? null,
            ParentJoint = GameObject.Transform.ParentJoint,
        };
    }

    public void LoadFromJson(MeshCollectionItem item)
    {
        if (!string.IsNullOrEmpty(item.Mesh) && Workspace.ResourceManager.TryResolveGameFile(item.Mesh, out var meshFile) && meshFile != Handle) {
            Handle = meshFile;
        }
        if (!string.IsNullOrEmpty(item.Material) && item.Material != loadedMdf) {
            mdfSource = item.Material;
        }
        if (!string.IsNullOrEmpty(item.Skeleton) && item.Skeleton != skeletonPath) {
            var anim = Animator ?? SetupAnimator();
            skeletonPath = item.Skeleton;
            if (Workspace.ResourceManager.TryResolveGameFile(skeletonPath, out var skel)) {
                anim.skeleton = skel.GetFile<FbxSkelFile>();
            }
        }

        ChangeMesh(false);
        GameObject.Transform.ParentJoint = item.ParentJoint ?? "";
    }

    private UIContext? animationPickerContext;
    internal string animationSourceFile = "";
    private string? loadedAnimationSource;
    private string motFilter = "";
    private bool isMotFilterMatchCase = false;

    private void ChangeAnim(IEnumerable<MeshViewerContext> meshContexts, MotFileBase activeAnim)
    {
        foreach (var mc in meshContexts) {
            if (mc.Animator?.owner == Animator) {
                mc.Animator!.SetActiveMotion(activeAnim);
            }
        }
    }

    public void ShowAnimSettings(List<MeshViewerContext> meshContexts, bool showControllerSelection)
    {
        if (Animator == null) return;

        if (showControllerSelection) {
            string parentLabel = "Animation Controller";
            if (Animator.owner != Animator) {
                parentLabel = "Parent: " + FindOwner(meshContexts)?.ShortName;
            }
            if (ImGui.BeginMenu(parentLabel)) {
                foreach (var other in meshContexts) {
                    if (other == this || other.Animator?.owner != other.Animator) continue;
                    if (ImGui.Selectable(other.ShortName, other.Animator == Animator, ImGuiSelectableFlags.NoAutoClosePopups)) {
                        Animator.owner = other.Animator;
                        other.GameObject.AddChild(GameObject);
                    }
                }
                if (Animator.owner != Animator && ImGui.Selectable("[Make controller]", ImGuiSelectableFlags.NoAutoClosePopups)) {
                    Animator.owner = Animator;
                    GameObject.Parent?.RemoveChild(GameObject);
                    GameObject.Folder?.AddGameObject(GameObject);
                }
                ImGui.EndMenu();
            }
            ImGui.Separator();
        }

        if (Animator.owner != Animator) return;

        animationPickerContext ??= UI.AddChild<MeshViewerContext, string>(
            "Animation File",
            this,
            new ResourcePathPicker(Workspace, FileFilters.MeshFile, KnownFileFormats.MotionList, KnownFileFormats.Motion) { Flags = ResourcePathPicker.PathPickerFlags.EditorOnly },
            (v) => v!.animationSourceFile,
            (v, p) => v.animationSourceFile = p ?? "");

        animationPickerContext.ShowUI();

        var settings = AppConfig.Settings;
        if (settings.RecentMotlists.Count > 0) {
            if (AppImguiHelpers.ShowRecentFiles(settings.RecentMotlists, Workspace.Game, ref animationSourceFile)) {
                animationPickerContext.ResetState();
            }
        }
        if (animationSourceFile != loadedAnimationSource) {
            if (!string.IsNullOrEmpty(animationSourceFile)) {
                loadedAnimationSource = animationSourceFile;
                foreach (var c in meshContexts) {
                    if (c == this || c.Animator?.owner == Animator) c.Animator?.LoadAnimationList(loadedAnimationSource);
                }
                settings.RecentMotlists.AddRecent(Workspace.Game, animationSourceFile);
            } else {
                foreach (var c in meshContexts) {
                    if (c == this || c.Animator?.owner == Animator) c.Animator?.Unload();
                }
                loadedAnimationSource = animationSourceFile;
            }
        }

        var animator = Animator;
        if (animator?.AnimationCount > 0) {
            ImGui.Separator();
            ImGui.Spacing();
            var ignoreRoot = animator.IgnoreRootMotion;
            if (ImGui.Button($"{AppIcons.SI_WindowOpenNew}")) {
                if (animator.File!.Format.format == KnownFileFormats.Motion) {
                    var fakeMotlist = new MotlistFile(new FileHandler());
                    var ff = animator.File.GetFile<MotFile>();
                    fakeMotlist.MotFiles.Add(ff);
                    var fakeHandle = FileHandle.CreateEmbedded(new MotListFileLoader(), new BaseFileResource<MotlistFile>(fakeMotlist));
                    EditorWindow.CurrentWindow?.AddSubwindow(new MotlistEditor(Workspace, fakeHandle));
                } else {
                    EditorWindow.CurrentWindow?.AddSubwindow(new MotlistEditor(Workspace, animator.File!));
                }
            }
            ImguiHelpers.Tooltip("Open current motlist in Motlist Editor");
            ImGui.SameLine();
            if (ImguiHelpers.ToggleButtonMultiColor(AppIcons.SIC_IgnoreRootMotion, ref ignoreRoot, new[] { Colors.IconTertiary, Colors.IconPrimary, Colors.IconPrimary }, Colors.IconActive)) {
                AppConfig.Settings.MeshViewer.DisableRootMotion = ignoreRoot;
                AppConfig.Settings.Save();
            }
            ImguiHelpers.Tooltip("Ignore Root Motion");
            foreach (var c in meshContexts) c.Animator?.IgnoreRootMotion = ignoreRoot;

            ImGui.SameLine();
            ImguiHelpers.ToggleButton($"{AppIcons.SI_GenericMatchCase}", ref isMotFilterMatchCase, Colors.IconActive);
            ImguiHelpers.Tooltip("Match Case");

            ImGui.SameLine();
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
            AppImguiHelpers.ClearableInputText("##MotFilter"u8, $"{AppIcons.SI_GenericMagnifyingGlass} Filter Animations", ref motFilter, 200);

            ImGui.Spacing();
            foreach (var mot in animator.Animations) {
                var name = mot.Name;
                if (!string.IsNullOrEmpty(motFilter) && !name.Contains(motFilter, isMotFilterMatchCase ? StringComparison.InvariantCulture : StringComparison.InvariantCultureIgnoreCase)) continue;

                ImGui.PushID(mot.GetHashCode());
                if (ImGui.RadioButton(name, animator.ActiveMotion == mot)) {
                    ChangeAnim(meshContexts, (MotFile)mot);
                    if (animator == viewer.Animators.FirstOrDefault()) {
                        foreach (var other in viewer.MeshContexts) {
                            if (other.Animator == animator) continue;

                            var motId = Animator.GetMotionId(mot.Name);
                            var sameMot = (other.Animator?.Animations.FirstOrDefault(anim => anim.Name == mot.Name)
                                ?? other.Animator?.Animations.FirstOrDefault(anim => Animator.GetMotionId(anim.Name) == motId));
                            if (sameMot != null) {
                                other.ChangeAnim(viewer.MeshContexts, sameMot);
                            }
                        }
                    }
                }
                if (ImGui.BeginPopupContextItem(name)) {
                    if (ImGui.Selectable("Copy name")) {
                        EditorWindow.CurrentWindow?.CopyToClipboard(name, $"Copied name: {name}");
                        ImGui.CloseCurrentPopup();
                    }
                    ImGui.EndPopup();
                }
                ImGui.PopID();
            }
        } else if (animator?.File != null) {
            ImGui.TextColored(Colors.Note, "Selected file contains no playable animations");
        }
    }

    public void ShowTransformUI(List<MeshViewerContext> meshContexts)
    {
        var owner = FindOwner(meshContexts);

        var childCtx = meshContexts.Where(mc => mc.FindOwner(meshContexts) == this);
        if (owner == this || owner == null) {
            var tr = UI.GetChild("Transform") ?? UI.AddChild("Transform", GameObject.Transform);
            if (tr.uiHandler == null) {
                WindowHandlerFactory.AddDefaultHandler<Transform>(tr);
            }

            ImGui.SetNextItemOpen(true, ImGuiCond.Always);
            tr.ShowUI();
        } else {
            var tr = owner.UI.GetChild("Transform") ?? owner.UI.AddChild("Transform", owner.GameObject.Transform);
            if (tr.uiHandler == null) {
                WindowHandlerFactory.AddDefaultHandler<Transform>(tr);
            }
            ImGui.SetNextItemOpen(true, ImGuiCond.Always);
            tr.ShowUI();
        }
    }
}
