using System.Numerics;
using ContentEditor.App.Graphics;
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

    private AssimpMeshResource? mesh;
    private string? meshPath;
    private FileHandle fileHandle;

    private WindowData data = null!;
    protected UIContext context = null!;

    private bool isDragging;
    private Vector2 lastDragPos;
    private float moveSpeed = 25.0f;
    private float rotateSpeed = 2.0f;
    private float zoomSpeed = 0.1f;
    private float yaw, pitch;
    private const float pitchLimit = MathF.PI / 2 - 0.01f;
    public MeshViewer(ContentWorkspace workspace, FileHandle file)
    {
        meshPath = file.Filepath;
        file.References.Add(this);
        Workspace = workspace;
        fileHandle = file;
        mesh = file.GetResource<AssimpMeshResource>();
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
    private Vector3 GetMeshCenter(GameObject gameObject)
    {
        var meshComp = gameObject.GetComponent<MeshComponent>();
        if (meshComp != null) {
            var bounds = meshComp.LocalBounds;
            return (bounds.minpos + bounds.maxpos) * 0.5f;
        }
        return gameObject.Transform.Position;
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
            meshComponent.SetMesh(fileHandle, fileHandle);
            scene.RenderContext.ProjectionMode = RenderContext.CameraProjection.Orthographic;
            CenterCameraToSceneObject();
        }

        if (mesh != null) {
            var expectedSize = ImGui.GetWindowSize() - ImGui.GetCursorPos() - ImGui.GetStyle().WindowPadding;
            expectedSize.X = Math.Max(expectedSize.X, 4);
            expectedSize.Y = Math.Max(expectedSize.Y, 4);
            var nativeSize = data.ParentWindow.Size;
            scene.RenderContext.SetRenderToTexture(expectedSize);

            if (scene.RenderContext.RenderTargetTextureHandle == 0) return;

            var c = ImGui.GetCursorPos();
            ImGui.Image((nint)scene.RenderContext.RenderTargetTextureHandle, expectedSize, new System.Numerics.Vector2(0, 1), new System.Numerics.Vector2(1, 0));
            ImGui.SetCursorPos(c);
            ImGui.InvisibleButton("##image", expectedSize, ImGuiButtonFlags.MouseButtonLeft|ImGuiButtonFlags.MouseButtonRight);
            if (ImGui.IsItemClicked(ImGuiMouseButton.Right) || ImGui.IsItemClicked(ImGuiMouseButton.Left)) {
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
                        if (ImGui.IsMouseDown(ImGuiMouseButton.Left)) {
                            if (ImGui.IsMouseDown(ImGuiMouseButton.Right)) {
                                scene.Camera.GameObject.Transform.TranslateForwardAligned(new Vector3(moveDelta.X, 0, -moveDelta.Y) * moveSpeed * scene.RenderContext.DeltaTime);
                            } else {
                                scene.Camera.GameObject.Transform.TranslateForwardAligned(new Vector3(-moveDelta.X, moveDelta.Y, 0) * moveSpeed * scene.RenderContext.DeltaTime);
                            }
                        }
                    } else if (ImGui.IsMouseDown(ImGuiMouseButton.Right)) {
                        yaw += moveDelta.X * rotateSpeed;
                        pitch += moveDelta.Y * rotateSpeed;
                        if (scene.RenderContext.ProjectionMode == RenderContext.CameraProjection.Perspective) {
                            var rotation = Quaternion<float>.CreateFromYawPitchRoll(yaw, pitch, 0);
                            scene.Camera.GameObject.Transform.LocalRotation = rotation.ToSystem();
                        } else {
                            pitch = Math.Clamp(pitch, -pitchLimit, pitchLimit);
                            var target = previewGameobject != null ? GetMeshCenter(previewGameobject): Vector3.Zero;
                            float distance = Vector3.Distance(scene.Camera.GameObject.Transform.Position, target);
                            var rotation = Quaternion<float>.CreateFromYawPitchRoll(yaw, pitch, 0);
                            var offset = Vector3.Transform(new Vector3(0, 0, -distance), rotation.ToSystem());
                            scene.Camera.GameObject.Transform.LocalPosition = target + offset;
                            scene.Camera.GameObject.Transform.LocalRotation = rotation.ToSystem();
                        }
                    }
                }
            }
            if (ImGui.IsItemHovered()) {
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

            ImGui.SetCursorPos(new Vector2(20, 45));
            ImGui.PushStyleColor(ImGuiCol.ChildBg, new System.Numerics.Vector4(0.2f, 0.2f, 0.2f, 0.5f));
            ImGui.BeginChild("OverlayControls", new Vector2(480, 220), ImGuiChildFlags.AlwaysUseWindowPadding | ImGuiChildFlags.Borders);

            ImGui.SeparatorText("Camera Controls:");
            if (ImGui.RadioButton("Orthographic", scene.RenderContext.ProjectionMode == RenderContext.CameraProjection.Orthographic)) {
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
            if (ImGui.Button("Reset View")) {
                CenterCameraToSceneObject();
                lastDragPos = new();
            }
            if (scene.RenderContext.ProjectionMode == RenderContext.CameraProjection.Perspective) {
                float fov = scene.RenderContext.FieldOfView;
                if (ImGui.SliderAngle("Field of View", ref fov, 10.0f, 120.0f)) {
                    scene.RenderContext.FieldOfView = fov;
                }
            } else {
                float ortho = scene.RenderContext.OrthoSize;
                if (ImGui.SliderFloat("Field of View", ref ortho, 0.1f, 2.0f)) {
                    scene.RenderContext.OrthoSize = ortho;
                }
            }
            ImGui.SliderFloat("Move Speed", ref moveSpeed, 1.0f, 50.0f);
            ImGui.SliderFloat("Rotate Speed", ref rotateSpeed, 0.1f, 10.0f);
            ImGui.SliderFloat("Zoom Speed", ref zoomSpeed, 0.01f, 1.0f);
            if (ImGui.TreeNode("Mesh Info")) {
                // TODO SILVER: Fix path display, add more info about the mesh i.e.: vert count, tris count, submesh (material) count 
                ImGui.Text($"Path: {fileHandle.Filepath}");
                ImGui.TreePop();
            }
            ImGui.PopStyleColor();
            ImGui.EndChild();
        } else {
            ImGui.Text("No mesh selected");
        }
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
}
