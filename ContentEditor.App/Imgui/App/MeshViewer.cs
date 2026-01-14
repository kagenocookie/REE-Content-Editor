using System.Numerics;
using ContentEditor.App.FileLoaders;
using ContentEditor.App.Graphics;
using ContentEditor.App.ImguiHandling;
using ContentEditor.App.ImguiHandling.Mesh;
using ContentEditor.App.Windowing;
using ContentEditor.Core;
using ContentEditor.Editor;
using ContentPatcher;
using ReeLib;
using ReeLib.via;

namespace ContentEditor.App;

public class MeshViewer : FileEditor, IDisposable, IFocusableFileHandleReferenceHolder
{
    public override bool HasUnsavedChanges => Handle.Modified;

    public override string HandlerName => $"Mesh Viewer";

    IRectWindow? IFileHandleReferenceHolder.Parent => data.ParentWindow;

    public ContentWorkspace Workspace { get; }
    private Scene? scene;
    private GameObject? previewGameobject;

    public Scene? Scene => scene;

    private const float TopMargin = 64;

    private string? loadedMdf;
    private string? mdfSource;
    private string? originalMDF;
    private UIContext? mdfPickerContext;

    private UIContext? animationPickerContext;
    private string animationSourceFile = "";
    private Animator? animator;
    private string motFilter = "";
    private bool isMotFilterMatchCase = false;
    private string? loadedAnimationSource;
    public Animator? Animator => animator;
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

    private CommonMeshResource? mesh;
    private string? meshPath;
    public CommonMeshResource? Mesh => mesh;

    private WindowData data = null!;

    private bool isDragging;
    private const float pitchLimit = MathF.PI / 2 - 0.01f;
    private enum TextureMode
    {
        HighRes,
        LowRes
    }
    private bool isMDFUpdateRequest = false;
    private TextureMode textureMode = TextureMode.HighRes;

    private bool showAnimationsMenu = false;
    private bool isSynced;

    private bool exportAnimations = true;
    private bool exportCurrentAnimationOnly;
    private bool exportInProgress;

    private string? lastImportSourcePath;

    private bool showSkeleton;
    private GizmoShapeBuilder? _skeletonBuilder;
    private MeshHandle? gizmoMeshHandle;

    private bool showImportSettings;

    protected override void Reset()
    {
        base.Reset();
        ChangeMesh();
    }

    public MeshViewer(ContentWorkspace workspace, FileHandle file) : base(file)
    {
        windowFlags = ImGuiWindowFlags.MenuBar;
        Workspace = workspace;
        ChangeMesh();

        exportTemplate = mesh?.NativeMesh.CurrentVersionConfig ?? MeshFile.AllVersionConfigs.Last();
    }

    private void TryGuessMdfFilepath()
    {
        if (!Workspace.Env.TryGetFileExtensionVersion("mdf2", out var mdfVersion)) {
            mdfVersion = -1;
        }

        var meshBasePath = PathUtils.GetFilepathWithoutExtensionOrVersion(Handle.Filepath).ToString();
        var ext = ".mdf2";
        if (mdfVersion != -1) ext += "." + mdfVersion;

        var mdfPath = meshBasePath + ext;
        if (!File.Exists(mdfPath)) mdfPath = meshBasePath + "_Mat" + ext;

        if (!File.Exists(mdfPath) && Handle.NativePath != null) {
            meshBasePath = PathUtils.GetFilepathWithoutExtensionOrVersion(Handle.NativePath).ToString();
            if (Workspace.ResourceManager.TryResolveGameFile(meshBasePath + ext, out _)) {
                mdfPath = meshBasePath + ext;
            } else if (Workspace.ResourceManager.TryResolveGameFile(meshBasePath + "_Mat" + ext, out _)) {
                mdfPath = meshBasePath + "_Mat" + ext;
            } else {
                mdfPath = "";
            }
        }
        mdfSource = mdfPath;
        originalMDF = mdfPath;
        loadedMdf = null;
    }

    public void ChangeMesh(string newMesh)
    {
        if (Workspace.ResourceManager.TryResolveGameFile(newMesh, out var newFile) && newFile != Handle) {
            ChangeMesh(newFile);
        }
    }

    public void ChangeMesh(FileHandle newHandle)
    {
        Handle.References.Remove(this);
        Handle = newHandle;
        ChangeMesh();
    }

    private void ChangeMesh()
    {
        meshPath = Handle.Filepath;
        if (!Handle.References.Contains(this)) Handle.References.Add(this);
        mesh = Handle.GetResource<CommonMeshResource>();
        if (mesh == null) {
            if (Handle.Filepath.Contains("streaming/")) {
                Logger.Error("Can't directly open streaming meshes. Open the non-streaming file instead.");
            }
            return;
        }
        TryGuessMdfFilepath();

        var meshComponent = previewGameobject?.GetComponent<MeshComponent>();
        meshComponent?.Transform.InvalidateTransform();
        meshComponent?.SetMesh(Handle, Handle);
        if (mesh.HasAnimations && string.IsNullOrEmpty(animationSourceFile)) {
            animationSourceFile = Handle.Filepath;
        }
    }

    private void CenterCameraToSceneObject()
    {
        if (previewGameobject == null || scene == null) return;

        scene.ActiveCamera.LookAt(previewGameobject, true);
        scene.ActiveCamera.OrthoSize = previewGameobject.GetWorldSpaceBounds().Size.Length() * 0.7f;
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

        MeshComponent meshComponent;
        if (previewGameobject == null) {
            scene.Add(previewGameobject = new GameObject("_preview", Workspace.Env, null, scene));
            meshComponent = previewGameobject.AddComponent<MeshComponent>();
        } else {
            meshComponent = previewGameobject.RequireComponent<MeshComponent>();
        }

        if (!meshComponent.HasMesh) {
            meshComponent.IsStreamingTex = true;
            meshComponent.SetMesh(Handle, Handle);
            scene.ActiveCamera.ProjectionMode = AppConfig.Settings.MeshViewer.DefaultProjection;
            CenterCameraToSceneObject();
        }

        if (mesh == null) {
            ImGui.Text("No mesh selected");
            return;
        }

        if (showSkeleton) {
            RenderSkeleton();
        }

        Vector2? embeddedMenuPos = null;
        if (!ShowMenu(meshComponent) && !isSynced) {
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
            ImGui.SetCursorPos(new Vector2(17, TopMargin));
            ImGui.PushStyleColor(ImGuiCol.ChildBg, 0);
            ImGui.BeginChild("OverlayControlsContainer", new Vector2(480, ImGui.GetContentRegionAvail().Y - ImGui.GetStyle().WindowPadding.Y), 0, ImGuiWindowFlags.NoMove);
            ImGui.PushStyleColor(ImGuiCol.ChildBg, ImguiHelpers.GetColor(ImGuiCol.WindowBg) with { W = 0.5f });
            ImGui.BeginChild("OverlayControls", new Vector2(480, 0), ImGuiChildFlags.AlwaysUseWindowPadding | ImGuiChildFlags.Borders | ImGuiChildFlags.AutoResizeY | ImGuiChildFlags.AlwaysAutoResize);

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

    private bool ShowMenu(MeshComponent meshComponent)
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

            if (!isSynced && previewGameobject != null) {
                if (ImGui.MenuItem($"{AppIcons.SI_GenericInfo} Mesh Info")) ImGui.OpenPopup("MeshInfo");
                ImguiHelpers.VerticalSeparator();
                if (ImGui.MenuItem($"{AppIcons.SI_MeshViewerMeshGroup} Mesh Groups")) ImGui.OpenPopup("MeshGroups");
                if (ImGui.MenuItem($"{AppIcons.SI_FileType_MDF} Material")) ImGui.OpenPopup("Material");
                if (ImGui.BeginMenu($"{AppIcons.SI_FileType_RCOL} RCOL")) {
                    var rcolEdit = Scene!.Root.SetEditMode(previewGameobject.GetOrAddComponent<RequestSetColliderComponent>());
                    rcolEdit?.DrawMainUI();
                    ImGui.EndMenu();
                }
                ImguiHelpers.VerticalSeparator();
                if (ImGui.MenuItem($"{AppIcons.SI_Animation} Animations")) showAnimationsMenu = !showAnimationsMenu;
                if (showAnimationsMenu) ImguiHelpers.HighlightMenuItem($"{AppIcons.SI_Animation} Animations");
                ImguiHelpers.VerticalSeparator();
                if (ImGui.MenuItem($"{AppIcons.SI_GenericIO} Import / Export")) ImGui.OpenPopup("Export");

                if (ImGui.BeginPopup("MeshInfo")) {
                    ShowMeshInfo();
                    ImGui.EndPopup();
                }
                if (ImGui.BeginPopup("Material")) {
                    ShowMaterialSettings(meshComponent);
                    ImGui.EndPopup();
                }
                if (ImGui.BeginPopup("MeshGroups")) {
                    ShowMeshGroupSettings(meshComponent);
                    ImGui.EndPopup();
                }
                if (ImGui.BeginPopup("Export")) {
                    ShowImportExportMenu();
                    ImGui.EndPopup();
                }
                if (ImGui.BeginPopup("Animations")) {
                    ShowAnimationMenu(meshComponent);
                    ImGui.EndPopup();
                }
            }
            ImGui.EndMenuBar();
            UpdateMaterial(meshComponent);
            return true;
        } else {
            UpdateMaterial(meshComponent);
            return false;
        }
    }

    private void ShowMeshGroupSettings(MeshComponent meshComponent)
    {
        var meshGroupIds = mesh!.GroupIDs.ToList();
        var parts = RszFieldCache.Mesh.PartsEnable.Get(meshComponent.Data);
        if (parts == null) {
            ImGui.TextColored(Colors.Error, "Could not resolve enabled parts field for current game.");
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

    private void ShowMeshInfo()
    {
        ImGui.Text($"Path: {Handle.Filepath} ({Handle.HandleType})");
        if (ImGui.BeginPopupContextItem("##filepath")) {
            if (ImGui.Selectable("Copy path")) {
                EditorWindow.CurrentWindow?.CopyToClipboard(Handle.Filepath);
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }
        ImGui.Text("Total Vertices: " + mesh!.VertexCount);
        ImGui.Text("Total Polygons: " + mesh.PolyCount);
        ImGui.Text("Sub Meshes: " + mesh.MeshCount);
        ImGui.Text("Materials: " + mesh.MaterialCount);
        ImGui.Text("Bones: " + mesh.BoneCount);
        if (ImGui.TreeNode("Raw Data")) {
            var meshCtx = context.GetChild<MeshFileHandler>();
            if (meshCtx == null) {
                meshCtx = context.AddChild("Raw Mesh Data", mesh.NativeMesh, new MeshFileHandler());
            }
            meshCtx.ShowUI();
            if (meshCtx.Changed && !Handle.Modified) {
                Handle.Modified = true;
            }
            ImGui.TreePop();
        }
    }

    private void ShowMaterialSettings(MeshComponent meshComponent)
    {
        bool useHighRes = textureMode == TextureMode.HighRes;
        if (ImGui.Checkbox("Textures: " + (useHighRes ? "Hi-Res" : "Low-Res"), ref useHighRes)) {
            textureMode = useHighRes ? TextureMode.HighRes : TextureMode.LowRes;
            isMDFUpdateRequest = true;
            meshComponent.IsStreamingTex = useHighRes;
            UpdateMaterial(meshComponent);
        }
        ImGui.SameLine();
        if (ImGui.Button($"{AppIcons.SI_ResetMaterial}")) {
            mdfSource = originalMDF;
            UpdateMaterial(meshComponent);
        }
        ImguiHelpers.Tooltip("Reset MDF");
        if (mdfPickerContext == null) {
            mdfPickerContext = context.AddChild<MeshViewer, string>(
                "MDF2 Material",
                this,
                new ResourcePathPicker(Workspace, Workspace.Env.TypeCache.GetResourceSubtypes(KnownFileFormats.MeshMaterial)) { UseNativesPath = true, IsPathForIngame = false },
                (v) => v!.mdfSource,
                (v, p) => v.mdfSource = p ?? "");
        }
        mdfPickerContext.ShowUI();
    }

    private void ShowImportExportMenu()
    {
        if (mesh == null) return;

        if (Handle.Format.format == KnownFileFormats.Mesh) {
            DrawFileControls(data);
        }

        using var _ = ImguiHelpers.Disabled(exportInProgress);
        if (ImGui.Button($"{AppIcons.SI_GenericExport} Export Mesh")) {
            // potential export enhancement: include (embed) textures
            if (Handle.Resource is CommonMeshResource assmesh) {
                PlatformUtils.ShowSaveFileDialog((exportPath) => {
                    exportInProgress = true;
                    try {
                        if (!exportAnimations || animator?.File == null) {
                            assmesh.ExportToFile(exportPath);
                        } else if (exportCurrentAnimationOnly) {
                            if (animator.ActiveMotion == null) {
                                assmesh.ExportToFile(exportPath);
                            } else {
                                assmesh.ExportToFile(exportPath, [animator.ActiveMotion]);
                            }
                        } else {
                            if (animator.File.Format.format == KnownFileFormats.Motion) {
                                assmesh.ExportToFile(exportPath, [animator.File.GetFile<MotFile>()]);
                            } else {
                                assmesh.ExportToFile(exportPath, animator.File.GetFile<MotlistFile>().MotFiles);
                            }
                        }
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
                            ChangeMesh();
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
        if (animator?.File != null) ImGui.Checkbox("Include animations", ref exportAnimations);
        if (animator?.File != null && exportAnimations) ImGui.Checkbox("Selected animation only", ref exportCurrentAnimationOnly);
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
            var ver = MeshFile.GetFileExtension(exportTemplate);
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

    private void ShowAnimationMenu(MeshComponent meshComponent)
    {
        ImGui.SeparatorText("Animations");
        if (animator == null) {
            animator = new(Workspace);
        }
        if (!UpdateAnimatorMesh(meshComponent)) return;

        ImGui.Checkbox("Show Skeleton", ref showSkeleton);

        if (animationPickerContext == null) {
            animationPickerContext = context.AddChild<MeshViewer, string>(
                "Source File",
                this,
                new ResourcePathPicker(Workspace, FileFilters.MeshFilesAll, KnownFileFormats.MotionList, KnownFileFormats.Motion) { UseNativesPath = true, IsPathForIngame = false, DisableWarnings = true },
                (v) => v!.animationSourceFile,
                (v, p) => v.animationSourceFile = p ?? "");
        }
        animationPickerContext.ShowUI();
        if (!string.IsNullOrEmpty(animationSourceFile) && animator.File != null && ImGui.Button("Force reload")) {
            Workspace.ResourceManager.CloseFile(animator.File);
            animator.Unload();
            loadedAnimationSource = null;
        }
        var settings = AppConfig.Settings;
        if (settings.RecentMotlists.Count > 0) {
            var selection = animationSourceFile;
            var options = settings.RecentMotlists.ToArray();
            if (ImguiHelpers.ValueCombo("Recent files", options, options, ref selection)) {
                animationSourceFile = selection;
                animationPickerContext.ResetState();
            }
        }
        if (animationSourceFile != loadedAnimationSource) {
            if (!string.IsNullOrEmpty(animationSourceFile)) {
                animator.LoadAnimationList(loadedAnimationSource = animationSourceFile);
                AppConfig.Instance.AddRecentMotlist(animationSourceFile);
            } else {
                animator.Unload();
                loadedAnimationSource = animationSourceFile;
            }
        }

        if (animator?.AnimationCount > 0) {
            ImGui.Separator();
            ImGui.Spacing();
            var ignoreRoot = animator.IgnoreRootMotion;
            if (ImGui.Button($"{AppIcons.SI_FileType_MOTLIST}")) {
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
            animator.IgnoreRootMotion = ignoreRoot;

            ImGui.SameLine();
            ImguiHelpers.ToggleButton($"{AppIcons.SI_GenericMatchCase}", ref isMotFilterMatchCase, Colors.IconActive);
            ImguiHelpers.Tooltip("Match Case");

            ImGui.SameLine();
            ImGui.SetNextItemAllowOverlap();
            ImGui.InputTextWithHint("##MotFilter", $"{AppIcons.SI_GenericMagnifyingGlass} Filter Animations", ref motFilter, 200);
            if (!string.IsNullOrEmpty(motFilter)) {
                ImGui.SameLine();
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() - (ImGui.CalcTextSize($"{AppIcons.SI_GenericClose}").X * 2));
                ImGui.SetNextItemAllowOverlap();
                if (ImGui.Button($"{AppIcons.SI_GenericClose}")) {
                    motFilter = string.Empty;
                }
            }
            ImGui.Spacing();
            foreach (var (name, mot) in animator.Animations) {
                if (!string.IsNullOrEmpty(motFilter) && !name.Contains(motFilter, isMotFilterMatchCase ? StringComparison.InvariantCulture : StringComparison.InvariantCultureIgnoreCase)) continue;

                if (ImGui.RadioButton(name, animator.ActiveMotion == mot)) {
                    animator.SetActiveMotion(mot);
                }
                if (ImGui.BeginPopupContextItem(name)) {
                    if (ImGui.Selectable("Copy name")) {
                        EditorWindow.CurrentWindow?.CopyToClipboard(name, $"Copied name: {name}");
                        ImGui.CloseCurrentPopup();
                    }
                    ImGui.EndPopup();
                }
            }
        } else if (animator?.File != null) {
            ImGui.TextColored(Colors.Note, "Selected file contains no playable animations");
        }
    }

    private void RenderSkeleton()
    {
        var mcomp = previewGameobject?.GetComponent<MeshComponent>();
        var mesh = mcomp?.MeshHandle as AnimatedMeshHandle;
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

    private void UpdateMaterial(MeshComponent meshComponent)
    {
        var mesh = meshComponent.MeshHandle;
        if (loadedMdf != mdfSource || isMDFUpdateRequest) {
            loadedMdf = mdfSource;
            isMDFUpdateRequest = false;
            if (string.IsNullOrEmpty(mdfSource)) {
                meshComponent.SetMesh(Handle, Handle);
            } else if (Workspace.ResourceManager.TryResolveGameFile(mdfSource, out var mdfHandle)) {
                meshComponent.SetMesh(Handle, mdfHandle);
            } else {
                meshComponent.SetMesh(Handle, Handle);
                if (mdfSource != originalMDF) {
                    Logger.Error("Could not locate mdf2 file " + mdfSource);
                }
            }
        }
    }

    private bool UpdateAnimatorMesh(MeshComponent meshComponent)
    {
        if (animator == null) return false;
        var mesh = meshComponent.MeshHandle;
        if (animator.Mesh != mesh) {
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
        if (animator?.ActiveMotion == null) return;
        if (!UpdateAnimatorMesh(meshComponent)) return;

        var windowSize = ImGui.GetWindowSize();
        var timestamp = $"{animator.CurrentTime:0.00} / {animator.TotalTime:0.00} ({animator.CurrentFrame:000} / {animator.TotalFrames:000})";
        var timestampSize = ImGui.CalcTextSize(timestamp) + new Vector2(5 * 48, 0);
        ImGui.SetCursorPos(new Vector2(windowSize.X - timestampSize.X - ImGui.GetStyle().WindowPadding.X * 2 - 100, TopMargin));
        ImGui.PushStyleColor(ImGuiCol.ChildBg, ImguiHelpers.GetColor(ImGuiCol.WindowBg) with { W = 0.5f });
        ImGui.BeginChild("PlaybackControls", new Vector2(timestampSize.X + 50, 80), ImGuiChildFlags.AlwaysUseWindowPadding | ImGuiChildFlags.Borders | ImGuiChildFlags.AutoResizeY | ImGuiChildFlags.AlwaysAutoResize);

        var p = ImGui.GetStyle().FramePadding;
        // the margins are weird on the buttons by default - font issue maybe, either way adding a bit of extra Y here
        var btnHeight = new Vector2(0, UI.FontSize + p.Y);
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 4);
        if (ImGui.Button((animator.IsPlaying ? AppIcons.Pause : AppIcons.Play).ToString(), btnHeight)) {
            if (animator.IsPlaying) {
                animator.Pause();
            } else {
                animator.Play();
            }
        }
        ImguiHelpers.Tooltip(animator.IsPlaying ? "Pause" : "Play");

        ImGui.SameLine();
        using (var _ = ImguiHelpers.Disabled(animator.CurrentTime == 0)) {
            if (ImGui.Button(AppIcons.SeekStart.ToString(), btnHeight)) {
                animator.Restart();
            }
        }
        ImguiHelpers.Tooltip("Restart");

        ImGui.SameLine();
        using (var _ = ImguiHelpers.Disabled(!animator.IsPlaying && !animator.IsActive)) {
            ImGui.PushStyleColor(ImGuiCol.Text, Colors.IconTertiary);
            if (ImGui.Button(AppIcons.Stop.ToString(), btnHeight)) {
                animator.Stop();
            }
            ImGui.PopStyleColor();
        }
        ImguiHelpers.Tooltip("Stop");

        ImGui.SameLine();
        using (var _ = ImguiHelpers.Disabled(!animator.IsActive)) {
            if (ImGui.Button(AppIcons.Previous.ToString(), btnHeight)) {
                animator.Pause();
                animator.Seek((animator.CurrentFrame - 1) * animator.FrameDuration);
                animator.Update(0);
            }
        }
        ImguiHelpers.Tooltip("Previous Frame");

        ImGui.SameLine();
        using (var _ = ImguiHelpers.Disabled(!animator.IsActive)) {
            if (ImGui.Button(AppIcons.Next.ToString(), btnHeight)) {
                animator.Pause();
                animator.Seek((animator.CurrentFrame + 1) * animator.FrameDuration);
                animator.Update(0);
            }
        }
        ImguiHelpers.Tooltip("Next Frame");

        ImGui.SameLine();
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - 4);
        ImGui.Text(timestamp);

        using (var _ = ImguiHelpers.Disabled(!animator.IsActive)) {
            int frame = animator.CurrentFrame;
            ImGui.SetNextItemWidth(325);
            if (ImGui.SliderInt("##AnimFrameSlider", ref frame, 0, animator.TotalFrames)) {
                animator.Pause();
                animator.Seek(frame * animator.FrameDuration);
                animator.Update(0);
            }
        }
        ImGui.SameLine();
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

        if (animator.IsPlaying) animator.Update(Time.Delta * playbackSpeed);

    }

    public void SetAnimation(string animlist)
    {
        if (animator == null) {
            animator = new(Workspace);
        }
        animator.LoadAnimationList(animlist);
        var firstAnim = animator.Animations.FirstOrDefault();
        if (firstAnim.Value != null) {
            animator.SetActiveMotion(firstAnim.Value);
        }
    }

    public void SetAnimation(MotFile mot)
    {
        if (animator == null) {
            animator = new(Workspace);
        }
        animator.SetActiveMotion(mot);
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
        mesh = null;
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

        if (other.animator != null) {
            if (animator == null) {
                animator = new Animator(Workspace);
            }
            if (!ignoreMotionClip && animator.ActiveMotion != other.animator.ActiveMotion && other.animator.ActiveMotion != null) {
                animator.SetActiveMotion(other.animator.ActiveMotion);
            }
            if (other.animator.IsPlaying != animator.IsPlaying) {
                if (other.animator.IsPlaying) {
                    animator.Play();
                } else {
                    animator.Pause();
                }
            }
            animator.Seek(other.animator.CurrentTime);
        }

        isSynced = true;
    }
}
