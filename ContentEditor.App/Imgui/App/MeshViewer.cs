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
    private FileHandle? fileHandle;

    private WindowData data = null!;
    protected UIContext context = null!;

    private bool isDragging;
    private Vector2 lastDragPos;

    public MeshViewer(ContentWorkspace workspace, FileHandle file)
    {
        meshPath = file.Filepath;
        // texture = new Mesh(EditorWindow.CurrentWindow!.GLContext, );
        // texture.LoadFromFile(file);
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

    public void ChangeMesh(string filepath)
    {
        fileHandle?.References.Remove(this);
        fileHandle = null;
        meshPath = filepath;
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

    public void OnIMGUI()
    {
        if (scene == null) {
            scene = EditorWindow.CurrentWindow!.SceneManager.CreateScene();
        }
        if (previewGameobject == null) {
            scene.Add(previewGameobject = new GameObject("_preview", Workspace.Env, null, scene));
            previewGameobject.AddComponent<MeshComponent>();
        }

        var meshComponent = previewGameobject.RequireComponent<MeshComponent>();
        if (fileHandle != null && !meshComponent.HasMesh) {
            scene.StoreResource(fileHandle);
            meshComponent.UpdateMesh(mesh, scene);
            var bounds = meshComponent.MeshLocalBounds;
            scene.Camera.GameObject.Transform.LocalPosition = Vector3.One * (-(bounds.maxpos - bounds.minpos).Length() * 0.35f);
            scene.Camera.GameObject.Transform.LocalRotation = Quaternion.CreateFromYawPitchRoll(Silk.NET.Maths.Scalar.DegreesToRadians(-30f), Silk.NET.Maths.Scalar.DegreesToRadians(45f), 0);
        }

        if (mesh == null) {
            if (meshPath == null) {
                ImGui.Text("No mesh selected");
                return;
            }

            ChangeMesh(meshPath);
        }

        if (mesh != null && fileHandle != null) {
            ImGui.Text(fileHandle.Filepath);
            ImGui.SameLine();
            if (ImGui.Button("Reset view")) {
                scene.Camera.GameObject.Transform.LocalPosition = default;
                scene.Camera.GameObject.Transform.LocalRotation = Quaternion.Identity;
                lastDragPos = new();
            }
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
                    if (ImGui.IsMouseDown(ImGuiMouseButton.Left)) {
                        if (ImGui.IsMouseDown(ImGuiMouseButton.Right)) {
                            scene.Camera.GameObject.Transform.TranslateForwardAligned(new Vector3(-delta.X, delta.Y, 0) * 0.05f);
                        } else {
                            scene.Camera.GameObject.Transform.TranslateForwardAligned(new Vector3(delta.X, 0, delta.Y) * -0.05f);
                        }
                    } else if (ImGui.IsMouseDown(ImGuiMouseButton.Right)) {
                        scene.Camera.GameObject.Transform.Rotate(Quaternion<float>.CreateFromYawPitchRoll(delta.X * 0.01f, delta.Y * 0.01f, 0));
                    }
                }
            }
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
            EditorWindow.CurrentWindow?.SceneManager.RemoveScene(scene);
            scene.Dispose();
            scene = null;
        }
        mesh = null;
    }
}