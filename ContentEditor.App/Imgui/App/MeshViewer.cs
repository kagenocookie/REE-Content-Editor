using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using ContentEditor.App.FileLoaders;
using ContentEditor.App.Graphics;
using ContentEditor.App.ImguiHandling;
using ContentEditor.App.ImguiHandling.Mesh;
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


    private UIContext? animationPickerContext;
    private string animationSourceFile = "";
    private string motFilter = "";
    private bool isMotFilterMatchCase = false;
    private bool isMotFilterActive = false;
    private string? loadedAnimationSource;
    // private Animator? animator;
    // public Animator? Animator => meshContexts.FirstOrDefault()?.Animator;
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

    public CommonMeshResource? Mesh => meshContexts.FirstOrDefault()?.MeshFile;

    private WindowData data = null!;

    private bool isDragging;
    private const float pitchLimit = MathF.PI / 2 - 0.01f;

    private bool showAnimationsMenu = false;
    private bool isSynced;

    private bool exportAnimations = true;
    private bool exportCurrentAnimationOnly;
    private bool exportInProgress;
    private bool exportLods = false;
    private bool exportOcclusion = true;

    private string? lastImportSourcePath;

    private bool showSkeleton;
    private GizmoShapeBuilder? _skeletonBuilder;
    private MeshHandle? gizmoMeshHandle;

    private bool showImportSettings;

    private bool? _hasTooManyWeightsForRender;

    private List<MeshViewerContext> meshContexts = new();

    protected override void Reset()
    {
        base.Reset();
        ChangeMainMesh();
    }

    public MeshViewer(ContentWorkspace workspace, FileHandle file) : base(file)
    {
        windowFlags = ImGuiWindowFlags.MenuBar;
        Workspace = workspace;
        exportTemplate = file.GetResource<CommonMeshResource>()?.NativeMesh.CurrentVersionConfig ?? MeshFile.AllVersionConfigs.Last();
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
        _hasTooManyWeightsForRender = null;
        var ctx = meshContexts.FirstOrDefault() ?? CreateAdditionalMesh(Handle);
        if (!Handle.References.Contains(this)) Handle.References.Add(this);
        ctx.ChangeMesh(resetMdf);

        var mesh = ctx.MeshFile;

        if (mesh.HasAnimations && string.IsNullOrEmpty(animationSourceFile)) {
            animationSourceFile = Handle.Filepath;
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
            ImGui.BeginChild("OverlayControls", new Vector2(480, AppConfig.Instance.UseFullscreenAnimPlayback ? ImGui.GetContentRegionAvail().Y - 80 : 0), ImGuiChildFlags.AutoResizeY | ImGuiChildFlags.AlwaysAutoResize | ImGuiChildFlags.AlwaysUseWindowPadding | ImGuiChildFlags.Borders);

            ImGui.SameLine();
            ShowAnimationMenu(meshComponent);

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
        if (ImGui.Button($"{AppIcons.GameObject}")) ImGui.OpenPopup("CameraSettings");
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
                if (ImGui.MenuItem($"{AppIcons.SI_Animation} Animations")) showAnimationsMenu = !showAnimationsMenu;
                if (showAnimationsMenu) ImguiHelpers.HighlightMenuItem($"{AppIcons.SI_Animation} Animations");
                var animWarns = GetAnimErrors();
                if (animWarns != null) {
                    using var _ = ImguiHelpers.OverrideStyleCol(ImGuiCol.Text, Colors.Warning);
                    ImGui.MenuItem($"{AppIcons.SI_GenericWarning}##mesh");
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
                    ShowAnimationMenu(mainCtx.Component);
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
    }

    private void ShowMeshCollections()
    {
        foreach (var ctx in meshContexts) {
            if (ImGui.BeginMenu(ctx.Handle.Filename.ToString())) {
                if (ctx != meshContexts[0] && ImGui.Selectable($"{AppIcons.SI_GenericDelete} Remove")) {
                    RemoveSubmesh(ctx);
                    ImGui.EndMenu();
                    break;
                }
                if (ImGui.BeginMenu($"{AppIcons.SI_GenericInfo} Info")) {
                    ShowMeshInfo(ctx, false);
                    ImGui.EndMenu();
                }
                if (ctx.IsAnimatable && ImGui.BeginMenu($"{AppIcons.SI_TagCharacter} Skeleton")) {
                    ctx.ShowSkeletonPicker();
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

        ImGui.SeparatorText("Add Mesh");
        if (addCollectionCtx == null) {
            addCollectionCtx = context.AddChild<MeshViewer, string>(
                "Source File",
                this,
                new ResourcePathPicker(Workspace, FileFilters.MeshFile, KnownFileFormats.Mesh) { UseNativesPath = true, IsPathForIngame = false, DisableWarnings = true },
                (v) => v!.addCollectionPath,
                (v, p) => v.addCollectionPath = p ?? "");
        }
        addCollectionCtx.ShowUI();

        if (AppImguiHelpers.ShowRecentFiles(AppConfig.Settings.RecentMeshes, Workspace.Game, ref addCollectionPath)) {
            addCollectionCtx.ResetState();
        }
        if (!string.IsNullOrEmpty(addCollectionPath) &&
            Workspace.ResourceManager.TryResolveGameFile(addCollectionPath, out var resolvedFile) &&
            resolvedFile.Format.format == KnownFileFormats.Mesh &&
            !meshContexts.Any(c => c.Handle == resolvedFile)
        ) {
            if (ImGui.Selectable($"{AppIcons.SI_GenericAdd} Add", ImGuiSelectableFlags.NoAutoClosePopups)) {
                AppConfig.Settings.RecentMeshes.AddRecent(Workspace.Game, addCollectionPath);
                CreateAdditionalMesh(resolvedFile).ChangeMesh(true);
            }
        }

        ImGui.SeparatorText("Manage Collection");
        if (ImGui.Selectable($"{AppIcons.SI_Save} Save collection")) {
            var arr = new JsonArray();
            foreach (var c in meshContexts) arr.Add(c.ToJson());
            var jsonStr = arr.ToJsonString();
            var collectionsDir = Path.Combine(AppConfig.Instance.GetGameUserPath(Workspace.Game), "mesh_collections");
            Directory.CreateDirectory(collectionsDir);
            PlatformUtils.ShowSaveFileDialog((path) => {
                using var fs = File.Create(path);
                JsonSerializer.Serialize(fs, arr, JsonConfig.configJsonOptions);
            }, Path.Combine(collectionsDir, Handle.Filename.ToString() + ".collection.json"), FileFilters.CollectionJsonFile);
        }
        if (ImGui.Selectable($"{AppIcons.SI_GenericImport} Load collection")) {
            var collectionsDir = Path.Combine(AppConfig.Instance.GetGameUserPath(Workspace.Game), "mesh_collections/");
            PlatformUtils.ShowFileDialog((files) => {
                try {
                    using var fs = File.OpenRead(files[0]);
                    var arr = JsonSerializer.Deserialize<JsonArray>(fs);
                    if (arr == null || arr.Count == 0) return;
                    while (meshContexts.Count > 1) {
                        RemoveSubmesh(meshContexts.Last());
                    }
                    meshContexts[0].LoadFromJson((JsonObject)arr[0]!);
                    for (int i = 1; i < arr.Count; i++) {
                        CreateAdditionalMesh(Handle).LoadFromJson((JsonObject)arr[i]!);
                    }
                } catch (Exception e) {
                    Logger.Error("Failed to load mesh collection: " + e.Message);
                }
            }, collectionsDir, FileFilters.CollectionJsonFile);
        }
        if (ImGui.Selectable($"{AppIcons.SI_GenericClear} Remove all additional meshes")) {
            while (meshContexts.Count > 1) {
                RemoveSubmesh(meshContexts.Last());
            }
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
        ImGui.Text($"Path: {handle.Filepath} ({handle.HandleType})");
        if (ImGui.BeginPopupContextItem("##filepath")) {
            if (ImGui.Selectable("Copy path")) {
                EditorWindow.CurrentWindow?.CopyToClipboard(handle.Filepath);
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }
        var mesh = ctx.MeshFile;
        ImGui.Text("Total Vertices: " + mesh.VertexCount);
        ImGui.Text("Total Polygons: " + mesh.PolyCount);
        ImGui.Text("Sub Meshes: " + mesh.MeshCount);
        ImGui.Text("Materials: " + mesh.MaterialCount);
        ImGui.Text("Bones: " + mesh.BoneCount);

        if (allowEditing && ImGui.TreeNode("Raw Data")) {
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

    private string? GetAnimErrors()
    {
        var mesh = meshContexts.First().MeshFile;
        if (mesh == null || !(mesh.NativeMesh.BoneData?.DeformBones.Count > 0) || !Animators.Any()) return null;

        if (mesh.NativeMesh.BoneData.DeformBones.Count > 255) {
            return """
                Some animations might not be fully accurate in the preview.
                Mesh has more than 255 deform bones, the rest will not animate in the preview.
                To view full animations, export them to an external mesh editor.

                This is NOT an issue ingame, with the mesh itself, or the animation, but a REE Content Editor rendering limitation.
                """;
        }

        if (_hasTooManyWeightsForRender == null) {
            _hasTooManyWeightsForRender = mesh.NativeMesh.MeshBuffer != null && mesh.NativeMesh.MeshBuffer.Weights.Any(w => w.boneWeights.Count(w => w > 0) > 4);
        }
        if (_hasTooManyWeightsForRender ?? false) {
            return """
                Some animations might not be fully accurate in the preview.
                Some vertices have more than 4 bone weights, only the first 4 are currently supported for preview.
                To view full animations, export them to an external mesh editor.

                This is NOT an issue ingame, with the mesh itself, or the animation, but a REE Content Editor rendering limitation.
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

    private void ShowImportExportMenu()
    {
        var mesh = meshContexts.First().MeshFile;
        if (mesh == null) return;

        if (Handle.Format.format == KnownFileFormats.Mesh) {
            DrawFileControls(data);
        }

        using var _ = ImguiHelpers.Disabled(exportInProgress);
        if (ImGui.Button($"{AppIcons.SI_GenericExport} Export Mesh")) {
            // potential export enhancement: include (embed?) textures
            if (Handle.Resource is CommonMeshResource assmesh) {
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

                        assmesh.ExportToFile(exportPath, exportLods, exportOcclusion, mots);
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
                            var importAsset = importedFile.GetResource<CommonMeshResource>();
                            var tmpHandler = new FileHandler(new MemoryStream(), Handle.Filepath);
                            importAsset.NativeMesh.WriteTo(tmpHandler, false);
                            Handle.Stream = tmpHandler.Stream.ToMemoryStream(disposeStream: false, forceCopy: true);
                            Handle.Revert(Workspace);
                            Handle.Modified = true;
                            ChangeMainMesh();
                            meshContexts.First().AlignMatNamesToMdf();
                        }
                    });
                }, lastImportSourcePath, fileExtension: FileFilters.MeshFilesAll);
            }
        }
        ImGui.SameLine();
        ImguiHelpers.ToggleButton($"{AppIcons.SI_Settings}", ref showImportSettings, Colors.IconActive);
        ImguiHelpers.Tooltip("Show Settings");

        if (exportInProgress) {
            ImGui.SameLine();
            // we have no way of showing any progress from assimp's side (which is 99% of the export duration) so this is the best we can do
            ImGui.TextWrapped($"Exporting in progress. This may take a while for large files and for many animations...");
        }
        if (PrimaryAnimator?.File != null) {
            ImGui.Checkbox("Include animations", ref exportAnimations);
            if (exportAnimations) ImGui.Checkbox("Selected animation only", ref exportCurrentAnimationOnly);
        }
        if (mesh.NativeMesh.MeshData?.LODs.Count > 1 || mesh.NativeMesh.ShadowMesh?.LODs.Count > 0) ImGui.Checkbox("Include LODs and Shadow Mesh", ref exportLods);
        if (mesh.NativeMesh.OccluderMesh?.MeshGroups.Count > 0) ImGui.Checkbox("Include Occlusion Mesh", ref exportOcclusion);
        if (showImportSettings) {
            ImGui.SeparatorText("Import Settings");
            var scale = AppConfig.Settings.Import.Scale * 100;
            if (ImGui.InputFloat("Import Scale", ref scale, "%.00f%%")) {
                AppConfig.Settings.Import.Scale = Math.Clamp(scale / 100, 0.001f, 100);
                AppConfig.Settings.Save();
            }
            var rootId = AppConfig.Settings.Import.ForceRootIdentity;
            if (ImGui.Checkbox("Force Default Root Orientation", ref rootId)) {
                AppConfig.Settings.Import.ForceRootIdentity = rootId;
                AppConfig.Settings.Save();
            }
            ImguiHelpers.Tooltip("Forces the root bone into the default (Identity) orientation on import.\nUse if you find that your imported mesh skeletons are fully rotated along an axis");
            var convertYUp = AppConfig.Settings.Import.ConvertZToYUpRootRotation;
            if (ImGui.Checkbox("Transform Z-Up -> Y-Up root axis for animations", ref convertYUp)) {
                AppConfig.Settings.Import.ConvertZToYUpRootRotation = convertYUp;
                AppConfig.Settings.Save();
            }
            ImguiHelpers.Tooltip("For modelling apps that don't know how to export with Y axis as up, this might fix the rotations of imported meshes.");
            ImGui.Spacing();
            ImGui.Spacing();
        }

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

    private bool UpdateAnimData()
    {
        foreach (var c in meshContexts) {
            var anim = c.Animator ?? c.SetupAnimator();
            // allow customizable owners? (e.g. paired animations like in pragmata)
            if (c != meshContexts[0]) {
                anim.owner = meshContexts[0].Animator;
            }
            if (!UpdateAnimatorMesh(c.Component)) return false;
        }
        return true;
    }

    private void ShowAnimationMenu(MeshComponent meshComponent)
    {
        ImGui.SeparatorText("Animations");
        if (!UpdateAnimData()) {
            return;
        }

        var animator = PrimaryAnimator;
        if (animator == null) return;

        ImGui.Checkbox("Show Skeleton", ref showSkeleton);
        ImGui.Spacing();
        using (var _ = ImguiHelpers.Disabled(string.IsNullOrEmpty(animationSourceFile))) {
            if (ImGui.Button($"{AppIcons.SI_Update}") && animator.File != null) {
                Workspace.ResourceManager.CloseFile(animator.File);
                foreach (var c in meshContexts) c.Animator?.Unload();
                loadedAnimationSource = null;
            }
            ImguiHelpers.Tooltip("Force reload");
        }
        ImGui.SameLine();
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize("Source File").X - ImGui.GetStyle().FramePadding.X);
        if (animationPickerContext == null) {
            animationPickerContext = context.AddChild<MeshViewer, string>(
                "Source File",
                this,
                new ResourcePathPicker(Workspace, FileFilters.MeshFilesAll, KnownFileFormats.MotionList, KnownFileFormats.Motion) { UseNativesPath = true, IsPathForIngame = false, DisableWarnings = true },
                (v) => v!.animationSourceFile,
                (v, p) => v.animationSourceFile = p ?? "");
        }
        animationPickerContext.ShowUI();

        var settings = AppConfig.Settings;
        if (settings.RecentMotlists.Count > 0) {
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize("Recent files").X - ImGui.GetStyle().FramePadding.X);
            if (AppImguiHelpers.ShowRecentFiles(settings.RecentMotlists, Workspace.Game, ref animationSourceFile)) {
                animationPickerContext.ResetState();
            }
        }
        if (animationSourceFile != loadedAnimationSource) {
            if (!string.IsNullOrEmpty(animationSourceFile)) {
                loadedAnimationSource = animationSourceFile;
                foreach (var c in meshContexts) c.Animator?.LoadAnimationList(loadedAnimationSource);
                settings.RecentMotlists.AddRecent(Workspace.Game, animationSourceFile);
            } else {
                foreach (var c in meshContexts) c.Animator?.Unload();
                loadedAnimationSource = animationSourceFile;
            }
        }

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
            ImguiHelpers.ToggleButtonMultiColor(AppIcons.SIC_IgnoreRootMotion, ref ignoreRoot, new[] { Colors.IconTertiary, Colors.IconPrimary, Colors.IconPrimary }, Colors.IconActive);
            ImguiHelpers.Tooltip("Ignore Root Motion");
            foreach (var c in meshContexts) c.Animator?.IgnoreRootMotion = ignoreRoot;

            ImGui.SameLine();
            ImguiHelpers.ToggleButton($"{AppIcons.SI_GenericMatchCase}", ref isMotFilterMatchCase, Colors.IconActive);
            ImguiHelpers.Tooltip("Match Case");

            ImGui.SameLine();
            ImGui.SetNextItemAllowOverlap();
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
            ImGui.InputTextWithHint("##MotFilter", $"{AppIcons.SI_GenericMagnifyingGlass} Filter Animations", ref motFilter, 200);
            isMotFilterActive = ImGui.IsItemActive();
            if (!string.IsNullOrEmpty(motFilter)) {
                ImGui.SameLine();
                ImGui.SetCursorScreenPos(new Vector2(ImGui.GetItemRectMax().X - ImGui.GetFrameHeight() - ImGui.GetStyle().FramePadding.X, ImGui.GetItemRectMin().Y));
                ImGui.SetNextItemAllowOverlap();
                if (ImGui.Button($"{AppIcons.SI_GenericClose}")) {
                    motFilter = string.Empty;
                }
            }

            ImGui.Spacing();
            foreach (var mot in animator.Animations) {
                var name = mot.Name;
                if (!string.IsNullOrEmpty(motFilter) && !name.Contains(motFilter, isMotFilterMatchCase ? StringComparison.InvariantCulture : StringComparison.InvariantCultureIgnoreCase)) continue;

                ImGui.PushID(mot.GetHashCode());
                if (ImGui.RadioButton(name, animator.ActiveMotion == mot)) {
                    SetAnimation((MotFile)mot);
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

    private bool UpdateAnimatorMesh(MeshComponent meshComponent)
    {
        var mesh = meshComponent.MeshHandle;
        var motion = meshComponent.GameObject.GetComponent<Motion>();
        var animator = motion?.Animator;
        // note: this should probably be handled directly within Motion component?
        if (animator != null && animator.Mesh != mesh) {
            if (mesh is not AnimatedMeshHandle anim) {
                ImGui.BeginChild("PlaybackError", new Vector2(300, 42), ImGuiChildFlags.AlwaysUseWindowPadding | ImGuiChildFlags.Borders | ImGuiChildFlags.AutoResizeY | ImGuiChildFlags.AlwaysAutoResize);
                ImGui.TextColored(Colors.Error, "Mesh is not animatable");
                ImGui.EndChild();
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
        if (!UpdateAnimatorMesh(meshComponent)) return;

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
        if (ImGui.Button((animator.IsPlaying ? AppIcons.Pause : AppIcons.Play).ToString()) || ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows) && AppConfig.Instance.Key_MeshViewer_PauseAnim.Get().IsPressed() && !isMotFilterActive) {
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
        }
    }

    public void SetAnimation(string animlist)
    {
        UpdateAnimData();
        foreach (var c in meshContexts) {
            var anim = c.Animator ?? c.SetupAnimator();
            anim.LoadAnimationList(animlist);
            if (anim.Animations.Any()) {
                anim.SetActiveMotion(anim.Animations[0]);
            }
        }
    }

    public void SetAnimation(MotFile mot)
    {
        UpdateAnimData();
        foreach (var c in meshContexts) {
            var anim = c.Animator ?? c.SetupAnimator();
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
}

internal class MeshViewerContext(MeshViewer viewer, UIContext ui, FileHandle file)
{
    private string? loadedMdf;
    private string? mdfSource;
    private string? originalMDF;
    private string skeletonPath = "";
    private UIContext? mdfPickerContext;
    private bool useHighResTextures = true;

    public UIContext UI { get; } = ui;
    public FileHandle Handle { get; set; } = file;
    private ContentWorkspace Workspace => viewer.Workspace;

    public GameObject GameObject { get; } = ui.Get<GameObject>();
    public MeshComponent Component { get; } = ui.Get<GameObject>().GetOrAddComponent<MeshComponent>();
    public Motion Motion { get; } = ui.Get<GameObject>().GetOrAddComponent<Motion>();
    public AnimatedMeshHandle? Mesh => Component.MeshHandle as AnimatedMeshHandle;
    public CommonMeshResource MeshFile => Handle.GetResource<CommonMeshResource>() ?? throw new Exception("invalid mesh");
    public Animator? Animator => GameObject.GetComponent<Motion>()?.Animator;
    public bool IsAnimatable => Mesh != null;

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
        if (ImGui.Checkbox("Textures: " + (useHighResTextures ? "Hi-Res" : "Low-Res"), ref useHighResTextures)) {
            meshComponent.UseStreamingTex = useHighResTextures;
            UpdateMaterial(true);
        }
        ImGui.SameLine();
        if (ImGui.Button($"{AppIcons.SI_ResetMaterial}")) {
            mdfSource = originalMDF;
            UpdateMaterial();
        }
        ImguiHelpers.Tooltip("Reset MDF");
        if (mdfPickerContext == null) {
            mdfPickerContext = UI.AddChild<MeshViewerContext, string>(
                "MDF2 Material",
                this,
                new ResourcePathPicker(viewer.Workspace, KnownFileFormats.MeshMaterial) { UseNativesPath = true, IsPathForIngame = false },
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
                if (ImGui.Button($"{AppIcons.SI_GenericDelete}##{i}")) {
                    var i_backup = i--;
                    UndoRedo.RecordCallback(null, () => matNames.RemoveAt(i_backup), () => matNames.Insert(i_backup, matName));
                    UndoRedo.AttachCallbackToLastAction(UndoRedo.CallbackType.Both, ApplyMeshChanges);
                    continue;
                }
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
            if (ImGui.Button($"{AppIcons.SI_GenericAdd}")) {
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
            if (Handle.Filepath.Contains("streaming/")) {
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
            new ResourcePathPicker(viewer.Workspace, KnownFileFormats.Skeleton, KnownFileFormats.FbxSkeleton, KnownFileFormats.RefSkeleton) { DisableWarnings = true, IsPathForIngame = false, UseNativesPath = true },
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

    public JsonObject ToJson()
    {
        return new JsonObject([
            new("mesh", JsonValue.Create(Handle.Filepath)),
            new("skeleton", JsonValue.Create(skeletonPath)),
            new("material", JsonValue.Create(loadedMdf))
        ]);
    }

    public void LoadFromJson(JsonObject obj)
    {
        var mesh = obj["mesh"]?.GetValue<string>();
        var skeleton = obj["skeleton"]?.GetValue<string>();
        var material = obj["material"]?.GetValue<string>();
        if (!string.IsNullOrEmpty(mesh) && Workspace.ResourceManager.TryResolveGameFile(mesh, out var meshFile) && meshFile != Handle) {
            Handle = meshFile;
        }
        if (!string.IsNullOrEmpty(material) && material != loadedMdf) {
            mdfSource = material;
        }
        if (!string.IsNullOrEmpty(skeleton) && skeleton != skeletonPath) {
            var anim = Animator ?? SetupAnimator();
            skeletonPath = skeleton;
            if (Workspace.ResourceManager.TryResolveGameFile(skeletonPath, out var skel)) {
                anim.skeleton = skel.GetFile<FbxSkelFile>();
            }
        }

        ChangeMesh(false);
    }
}
