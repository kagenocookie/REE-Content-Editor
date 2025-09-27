using System.Numerics;
using ContentEditor.App.Graphics;
using ContentEditor.App.ImguiHandling;
using ContentEditor.App.Windowing;
using ContentEditor.Core;
using ContentEditor.Editor;
using ContentPatcher;
using ImGuiNET;
using ReeLib;
using Silk.NET.Maths;

namespace ContentEditor.App;

public class MeshViewer : IWindowHandler, IDisposable, IFocusableFileHandleReferenceHolder
{
    public bool HasUnsavedChanges => false;

    public string HandlerName => $"Mesh Viewer";

    public bool CanClose => true;
    public bool CanFocus => true;

    IRectWindow? IFileHandleReferenceHolder.Parent => data.ParentWindow;

    public ContentWorkspace Workspace { get; }
    private Scene? scene;
    private GameObject? previewGameobject;

    public Scene? Scene => scene;

    private const float TopMargin = 30;

    private string? loadedMdf;
    private string? mdfSource;
    private string? originalMDF;
    private UIContext? mdfPickerContext;

    private UIContext? animationPickerContext;
    private string animationSourceFile = "";
    private Animator? animator;
    private string motFilter = "";
    private string? loadedAnimationSource;
    public Animator? Animator => animator;

    private AssimpMeshResource? mesh;
    private string? meshPath;
    private FileHandle fileHandle;

    public AssimpMeshResource? Mesh => mesh;

    private WindowData data = null!;
    protected UIContext context = null!;

    private bool isDragging;
    private Vector2 lastDragPos;
    private float moveSpeed = 25.0f;
    private float moveSpeedMultiplier = 1.0f;
    private float rotateSpeed = 2.0f;
    private float zoomSpeed = 0.1f;
    private float yaw, pitch;
    private const float pitchLimit = MathF.PI / 2 - 0.01f;
    private enum TextureMode
    {
        HighRes,
        LowRes
    }
    private bool isMDFUpdateRequest = false;
    private TextureMode textureMode = TextureMode.HighRes;

    private bool expandMenu = true;
    private bool isSynced;

    public MeshViewer(ContentWorkspace workspace, FileHandle file)
    {
        Workspace = workspace;
        ChangeMesh(fileHandle = file);
    }

    private void TryGuessMdfFilepath()
    {
        if (!Workspace.Env.TryGetFileExtensionVersion("mdf2", out var mdfVersion)) {
            mdfVersion = -1;
        }

        var meshBasePath = PathUtils.GetFilepathWithoutExtensionOrVersion(fileHandle.Filepath);
        var mdfPath = meshBasePath.ToString() + ".mdf2";
        // if (Path.IsPathRooted(meshBasePath)) {
        //     mdfPath = Directory.EnumerateFiles(Path.GetDirectoryName(meshBasePath!).ToString(), Path.GetFileName(meshBasePath).ToString() + ".mdf2.*").FirstOrDefault();
        // } else {
        //     if (mdfVersion != -1) mdfPath += "." + mdfVersion;
        // }
        if (mdfVersion != -1) mdfPath += "." + mdfVersion;
        this.mdfSource = mdfPath;
        this.originalMDF = mdfPath;
    }

    public void Focus()
    {
        var data = context.Get<WindowData>();
        ImGui.SetWindowFocus(data.Name ?? $"{data.Handler}##{data.ID}");
    }

    public void Close()
    {
        var data = context.Get<WindowData>();
        EditorWindow.CurrentWindow?.CloseSubwindow(data);
    }

    public void Init(UIContext context)
    {
        this.context = context;
        data = context.Get<WindowData>();
    }

    public void ChangeMesh(string newMesh)
    {
        if (Workspace.ResourceManager.TryResolveFile(newMesh, out var newFile) && newFile != fileHandle) {
            ChangeMesh(newFile);
        }
    }

    private void ChangeMesh(FileHandle newFile)
    {
        fileHandle?.References.Remove(this);
        fileHandle = newFile;
        meshPath = fileHandle.Filepath;
        fileHandle.References.Add(this);
        mesh = fileHandle.GetResource<AssimpMeshResource>();
        TryGuessMdfFilepath();

        var meshComponent = previewGameobject?.GetComponent<MeshComponent>();
        if (meshComponent != null) {
            meshComponent.SetMesh(fileHandle, fileHandle);
        }
    }

    public void OnWindow()
    {
        if (!ImguiHelpers.BeginWindow(data)) {
            WindowManager.Instance.CloseWindow(data);
            return;
        }
        ImGui.BeginGroup();
        OnIMGUI();
        ImGui.EndGroup();
        ImGui.End();
    }
    private void CenterCameraToSceneObject()
    {
        if (previewGameobject == null || scene == null) return;

        scene.Camera.LookAt(previewGameobject, true);
    }
    public void OnIMGUI()
    {
        if (scene == null) {
            scene = EditorWindow.CurrentWindow!.SceneManager.CreateScene(fileHandle, true);
        }
        MeshComponent meshComponent;
        if (previewGameobject == null) {
            scene.Add(previewGameobject = new GameObject("_preview", Workspace.Env, null, scene));
            meshComponent = previewGameobject.AddComponent<MeshComponent>();
        } else {
            meshComponent = previewGameobject.RequireComponent<MeshComponent>();
        }

        if (!meshComponent.HasMesh) {
            meshComponent.IsStreamingTex = true;
            meshComponent.SetMesh(fileHandle, fileHandle);
            scene.RenderContext.ProjectionMode = RenderContext.CameraProjection.Orthographic;
            CenterCameraToSceneObject();
        }

        if (mesh == null) {
            ImGui.Text("No mesh selected");
            return;
        }

        var expectedSize = ImGui.GetWindowSize() - ImGui.GetCursorPos() - ImGui.GetStyle().WindowPadding;
        expectedSize.X = Math.Max(expectedSize.X, 4);
        expectedSize.Y = Math.Max(expectedSize.Y, 4);
        var nativeSize = data.ParentWindow.Size;
        float meshSize = meshComponent.LocalBounds.Size.Length();
        scene.RenderContext.FarPlane = meshSize + 100.0f;
        scene.RenderContext.SetRenderToTexture(expectedSize);

        if (scene.RenderContext.RenderTargetTextureHandle == 0) return;

        var c = ImGui.GetCursorPos();
        ImGui.Image((nint)scene.RenderContext.RenderTargetTextureHandle, expectedSize, new System.Numerics.Vector2(0, 1), new System.Numerics.Vector2(1, 0));
        ImGui.SetCursorPos(c);
        ImGui.InvisibleButton("##image", expectedSize, ImGuiButtonFlags.MouseButtonLeft | ImGuiButtonFlags.MouseButtonRight);
        // need to store the click/hover events for after so we can handle clicks on the empty area below the info window same as a mesh image click event
        var meshClick = ImGui.IsItemClicked(ImGuiMouseButton.Right) || ImGui.IsItemClicked(ImGuiMouseButton.Left);
        var hoveredMesh = ImGui.IsItemHovered();

        if (isSynced) {
            isSynced = false;
        } else {
            ImGui.SetCursorPos(new Vector2(17, TopMargin));
            ImGui.PushStyleColor(ImGuiCol.ChildBg, 0);
            ImGui.BeginChild("OverlayControlsContainer", new Vector2(480, ImGui.GetContentRegionAvail().Y - ImGui.GetStyle().WindowPadding.Y), 0, ImGuiWindowFlags.NoMove);
            ImGui.PushStyleColor(ImGuiCol.ChildBg, ImguiHelpers.GetColor(ImGuiCol.WindowBg) with { W = 0.5f });
            ImGui.BeginChild("OverlayControls", new Vector2(480, 0), ImGuiChildFlags.AlwaysUseWindowPadding | ImGuiChildFlags.Borders | ImGuiChildFlags.AutoResizeY | ImGuiChildFlags.AlwaysAutoResize);

            if (ImGui.ArrowButton("##expand", expandMenu ? ImGuiDir.Down : ImGuiDir.Right)) {
                expandMenu = !expandMenu;
            }
            ImGui.SameLine();

            ImGui.SeparatorText("Controls");
            if (expandMenu) {
                ShowMenu(meshComponent);
            }

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
                lastDragPos = ImGui.GetMousePos();
            }
        } else if (isDragging && !ImGui.IsMouseDown(ImGuiMouseButton.Left) && !ImGui.IsMouseDown(ImGuiMouseButton.Right)) {
            isDragging = false;
        }

        if (isDragging) {
            var delta = ImGui.GetMousePos() - lastDragPos;
            lastDragPos = ImGui.GetMousePos();
            if (delta != Vector2.Zero) {
                var moveDelta = new Vector2(delta.X / expectedSize.X, delta.Y / expectedSize.Y);
                if (ImGui.IsMouseDown(ImGuiMouseButton.Left)) {
                    moveSpeedMultiplier = ImGui.IsKeyDown(ImGuiKey.LeftShift) ? 10.0f : 1.0f;
                    float cameraMoveSpeed = moveSpeed * moveSpeedMultiplier;
                    if (ImGui.IsMouseDown(ImGuiMouseButton.Right)) {
                        scene.Camera.Transform.TranslateForwardAligned(new Vector3(moveDelta.X, 0, -moveDelta.Y) * cameraMoveSpeed * scene.RenderContext.DeltaTime);
                    } else {
                        scene.Camera.Transform.TranslateForwardAligned(new Vector3(-moveDelta.X, moveDelta.Y, 0) * cameraMoveSpeed * scene.RenderContext.DeltaTime);
                    }
                } else if (ImGui.IsMouseDown(ImGuiMouseButton.Right)) {
                    yaw += moveDelta.X * rotateSpeed;
                    pitch += moveDelta.Y * rotateSpeed;
                    if (scene.RenderContext.ProjectionMode == RenderContext.CameraProjection.Perspective) {
                        scene.Camera.Transform.LocalRotation = Quaternion<float>.CreateFromYawPitchRoll(yaw, pitch, 0).ToSystem();
                    } else {
                        pitch = Math.Clamp(pitch, -pitchLimit, pitchLimit);
                        scene.Camera.Transform.LocalRotation = Quaternion<float>.CreateFromYawPitchRoll(yaw, pitch, 0).ToSystem();
                    }
                }
            }

            if (ImGui.IsMouseDown(ImGuiMouseButton.Right)) {
                var moveVec = Vector3.Zero;
                if (ImGui.IsKeyDown(ImGuiKey.W)) moveVec.Z--;
                if (ImGui.IsKeyDown(ImGuiKey.S)) moveVec.Z++;
                if (ImGui.IsKeyDown(ImGuiKey.A)) moveVec.X--;
                if (ImGui.IsKeyDown(ImGuiKey.D)) moveVec.X++;
                if (ImGui.IsKeyDown(ImGuiKey.Q)) moveVec.Y--;
                if (ImGui.IsKeyDown(ImGuiKey.E)) moveVec.Y++;

                if (moveVec != Vector3.Zero) {
                    var multiplier = Time.Delta * moveSpeed * (ImGui.IsKeyDown(ImGuiKey.LeftShift) ? 0.5f : 0.075f);
                    scene.Camera.Transform.TranslateForwardAligned(multiplier * moveVec);
                }
            }
        }

        if (hoveredMesh) {
            float wheel = ImGui.GetIO().MouseWheel;
            if (Math.Abs(wheel) > float.Epsilon) {
                if (scene.RenderContext.ProjectionMode == RenderContext.CameraProjection.Perspective) {
                    var zoom = scene.Camera.GameObject.Transform.LocalForward * (wheel * zoomSpeed) * -1.0f;
                    scene.Camera.GameObject.Transform.LocalPosition += zoom;
                } else {
                    float ortho = scene.RenderContext.OrthoSize;
                    ortho *= (1.0f - wheel * zoomSpeed);
                    ortho = Math.Clamp(ortho, 0.01f, 100.0f);
                    scene.RenderContext.OrthoSize = ortho;
                }
            }
        }
    }

    private void ShowMenu(MeshComponent meshComponent)
    {
        if (ImGui.RadioButton("Orthographic", scene!.RenderContext.ProjectionMode == RenderContext.CameraProjection.Orthographic)) {
            scene.RenderContext.ProjectionMode = RenderContext.CameraProjection.Orthographic;
            CenterCameraToSceneObject();
            lastDragPos = new();
        }
        ImGui.SameLine();
        if (ImGui.RadioButton("Perspective", scene.RenderContext.ProjectionMode == RenderContext.CameraProjection.Perspective)) {
            scene.RenderContext.ProjectionMode = RenderContext.CameraProjection.Perspective;
            CenterCameraToSceneObject();
            lastDragPos = new();
        }
        ImGui.SameLine();
        if (ImGui.Button($"{AppIcons.SI_ResetCamera}")) {
            CenterCameraToSceneObject();
            lastDragPos = new();
        }
        ImguiHelpers.Tooltip("Reset View Camera");
        if (scene.RenderContext.ProjectionMode == RenderContext.CameraProjection.Perspective) {
            float fov = scene.RenderContext.FieldOfView;
            if (ImGui.SliderAngle("Field of View", ref fov, 10.0f, 120.0f)) {
                scene.RenderContext.FieldOfView = fov;
            }
        } else {
            float ortho = scene.RenderContext.OrthoSize;
            if (ImGui.SliderFloat("Field of View", ref ortho, 0.1f, 10.0f)) {
                scene.RenderContext.OrthoSize = ortho;
            }
        }
        ImGui.SliderFloat("Move Speed", ref moveSpeed, 1.0f, 50.0f);
        ImguiHelpers.Tooltip("[Hold] Left Shift to move 10x faster.");
        ImGui.SliderFloat("Rotate Speed", ref rotateSpeed, 0.1f, 10.0f);
        ImGui.SliderFloat("Zoom Speed", ref zoomSpeed, 0.01f, 1.0f);
        ImGui.SeparatorText("Material");
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
                new ResourcePathPicker(Workspace, Workspace.Env.TypeCache.GetResourceSubtypes(KnownFileFormats.MaterialDefinition)) { UseNativesPath = true, IsPathForIngame = false },
                (v) => v!.mdfSource,
                (v, p) => v.mdfSource = p ?? "");
        }
        mdfPickerContext.ShowUI();
        UpdateMaterial(meshComponent);
        if (ImGui.TreeNode("Mesh Info")) {
            ImGui.TextWrapped($"Path: {fileHandle.Filepath}");
            if (ImGui.BeginPopupContextItem("##filepath")) {
                if (ImGui.Selectable("Copy path")) {
                    EditorWindow.CurrentWindow?.CopyToClipboard(fileHandle.Filepath);
                    ImGui.CloseCurrentPopup();
                }
                ImGui.EndPopup();
            }
            ImGui.Text("Total Vertices: " + mesh!.VertexCount);
            ImGui.Text("Total Polygons: " + mesh.PolyCount);
            ImGui.Text("Sub Meshes: " + mesh.MeshCount);
            ImGui.Text("Materials: " + mesh.MaterialCount);
            // there can't be more than one skeleton per .mesh file, so any one mesh will do here for the count
            ImGui.Text("Bones: " + mesh.BoneCount);
            ImGui.TreePop();
        }

        var meshGroupIds = mesh!.GroupIDs.ToList();
        if (meshGroupIds.Count > 1) {
            if (ImGui.TreeNode($"Mesh Groups ({meshGroupIds.Count})")) {
                var parts = RszFieldCache.Mesh.PartsEnable.Get(meshComponent.Data);
                foreach (var group in meshGroupIds) {
                    if (group < 0 || group >= parts.Count) continue;

                    var enabled = (bool)parts[group];
                    if (ImGui.Checkbox(group.ToString(), ref enabled)) {
                        parts[group] = (object)enabled;
                        meshComponent.RefreshIfActive();
                    }
                }
                ImGui.TreePop();
            }
        }
        if (ImGui.TreeNode("Animations")) {
            ShowAnimationMenu(meshComponent);
            ImGui.TreePop();
        }
    }

    private void ShowAnimationMenu(MeshComponent meshComponent)
    {
        if (animator == null) {
            animator = new (Workspace);
        }
        if (!UpdateAnimatorMesh(meshComponent)) return;

        if (animationPickerContext == null) {
            animationPickerContext = context.AddChild<MeshViewer, string>(
                "Source File",
                this,
                new ResourcePathPicker(Workspace, Workspace.Env.TypeCache.GetResourceSubtypes(KnownFileFormats.MotionBase)) { UseNativesPath = true, IsPathForIngame = false },
                (v) => v!.animationSourceFile,
                (v, p) => v.animationSourceFile = p ?? "");
        }
        animationPickerContext.ShowUI();
        if (animationSourceFile != loadedAnimationSource) {
            if (!string.IsNullOrEmpty(animationSourceFile)) {
                animator.LoadAnimationList(loadedAnimationSource = animationSourceFile);
            } else {
                animator.Unload();
                loadedAnimationSource = animationSourceFile;
            }
        }

        if (animator?.AnimationCount > 0) {
            ImGui.InputText("Filter", ref motFilter, 200);
            foreach (var (name, mot) in animator.Animations) {
                if (!string.IsNullOrEmpty(motFilter) && !name.Contains(motFilter, StringComparison.InvariantCultureIgnoreCase)) continue;

                if (ImGui.RadioButton(name, animator.ActiveMotion == mot)) {
                    animator.SetActiveMotion(mot);
                }
            }
        }
    }

    private void UpdateMaterial(MeshComponent meshComponent)
    {
        var mesh = meshComponent.MeshHandle;
        if (loadedMdf != mdfSource || isMDFUpdateRequest) {
            loadedMdf = mdfSource;
            isMDFUpdateRequest = false;
            if (string.IsNullOrEmpty(mdfSource)) {
                meshComponent.SetMesh(fileHandle, fileHandle);
            } else if (Workspace.ResourceManager.TryResolveFile(mdfSource, out var mdfHandle)) {
                meshComponent.SetMesh(fileHandle, mdfHandle);
            } else {
                meshComponent.SetMesh(fileHandle, fileHandle);
                Logger.Error("Could not locate mdf2 file " + mdfSource);
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
        ImGui.SetCursorPos(new Vector2(windowSize.X - timestampSize.X - ImGui.GetStyle().WindowPadding.X * 2, TopMargin));
        ImGui.PushStyleColor(ImGuiCol.ChildBg, ImguiHelpers.GetColor(ImGuiCol.WindowBg) with { W = 0.5f });
        ImGui.BeginChild("PlaybackControls", new Vector2(timestampSize.X, 46), ImGuiChildFlags.AlwaysUseWindowPadding | ImGuiChildFlags.Borders | ImGuiChildFlags.AutoResizeY | ImGuiChildFlags.AlwaysAutoResize);

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

        ImGui.SameLine();
        using (var _ = ImguiHelpers.Disabled(animator.CurrentTime == 0)) {
            if (ImGui.Button(AppIcons.SeekStart.ToString(), btnHeight)) {
                animator.Restart();
            }
        }

        ImGui.SameLine();
        using (var _ = ImguiHelpers.Disabled(!animator.IsPlaying && !animator.IsActive)) {
            if (ImGui.Button(AppIcons.Stop.ToString(), btnHeight)) {
                animator.Stop();
            }
        }

        ImGui.SameLine();
        using (var _ = ImguiHelpers.Disabled(!animator.IsActive)) {
            if (ImGui.Button(AppIcons.Previous.ToString(), btnHeight)) {
                animator.Pause();
                animator.Seek((animator.CurrentFrame - 1) * animator.FrameDuration);
                animator.Update(0);
            }
        }

        ImGui.SameLine();
        using (var _ = ImguiHelpers.Disabled(!animator.IsActive)) {
            if (ImGui.Button(AppIcons.Next.ToString(), btnHeight)) {
                animator.Pause();
                animator.Seek((animator.CurrentFrame + 1) * animator.FrameDuration);
                animator.Update(0);
            }
        }

        ImGui.SameLine();
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - 4);
        ImGui.Text(timestamp);

        ImGui.EndChild();
        ImGui.PopStyleColor();

        if (animator.IsPlaying) animator.Update(Time.Delta);
    }

    public void SetAnimation(string animlist)
    {
        if (animator == null) {
            animator = new (Workspace);
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
            animator = new (Workspace);
        }
        animator.SetActiveMotion(mot);
    }

    public static bool IsSupportedFileExtension(string filepathOrExtension)
    {
        var format = PathUtils.ParseFileFormat(filepathOrExtension);
        if (format.format == KnownFileFormats.Mesh) {
            return true;
        }

        return MeshLoader.StandardFileExtensions.Contains(Path.GetExtension(filepathOrExtension));
    }

    public bool RequestClose()
    {
        return false;
    }
    public void Dispose()
    {
        fileHandle?.References.Remove(this);
        if (scene != null) {
            EditorWindow.CurrentWindow?.SceneManager.UnloadScene(scene);
            scene = null;
        }
        mesh = null;
    }
    public void SyncFromScene(MeshViewer other, bool ignoreMotionClip)
    {
        if (scene == null || other.scene == null) return;

        scene.ActiveCamera.Transform.CopyFrom(other.scene.ActiveCamera.Transform);
        scene.RenderContext.ProjectionMode = other.scene.RenderContext.ProjectionMode;
        scene.RenderContext.NearPlane = other.scene.RenderContext.NearPlane;
        scene.RenderContext.FarPlane = other.scene.RenderContext.FarPlane;
        scene.RenderContext.FieldOfView = other.scene.RenderContext.FieldOfView;
        scene.RenderContext.OrthoSize = other.scene.RenderContext.OrthoSize;

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
