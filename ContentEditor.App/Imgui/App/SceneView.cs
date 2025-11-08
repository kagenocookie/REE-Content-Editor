using System.Numerics;
using ContentEditor.App.FileLoaders;
using ContentEditor.App.Windowing;
using ContentEditor.Core;
using ContentPatcher;
using ImGuiNET;
using ReeLib;

namespace ContentEditor.App;

public class SceneView : IWindowHandler, IKeepEnabledWhileSaving
{
    public bool HasUnsavedChanges => false;

    public string HandlerName => $"Scene";

    int IWindowHandler.FixedID => -100;

    public ContentWorkspace Workspace { get; }
    public Scene Scene { get; }

    private WindowData data = null!;
    protected UIContext context = null!;

    public SceneView(ContentWorkspace workspace, Scene scene)
    {
        Workspace = workspace;
        Scene = scene;
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
        var dragging = Scene.Mouse.IsDragging;
        if (dragging) ImGui.EndDisabled();
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(0));
        if (!ImguiHelpers.BeginWindow(data, flags: ImGuiWindowFlags.MenuBar)) {
            WindowManager.Instance.CloseWindow(data);
            ImGui.PopStyleVar();
            if (dragging) ImGui.BeginDisabled();
            return;
        }
        ImGui.PopStyleVar();
        ImGui.BeginGroup();
        OnIMGUI();
        ImGui.EndGroup();
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(0));
        ImGui.End();
        ImGui.PopStyleVar();
        if (dragging) ImGui.BeginDisabled();
    }

    public void OnIMGUI()
    {
        if (!Scene.IsActive) {
            ImGui.GetWindowDrawList().AddText(new Vector2(8, 8), 0xffffffff, "Scene is not active");
        }

        ShowMenu();
        var expectedSize = ImGui.GetWindowSize() - ImGui.GetCursorPos();
        expectedSize.X = Math.Max(expectedSize.X, 4);
        expectedSize.Y = Math.Max(expectedSize.Y, 4);
        Scene.RenderContext.SetRenderToTexture(expectedSize);

        if (Scene.RenderContext.RenderTargetTextureHandle == 0) return;

        var c = ImGui.GetCursorPos();
        var cc = ImGui.GetCursorScreenPos();
        Scene.OwnRenderContext.ViewportOffset = cc;
        ImGui.Image((nint)Scene.RenderContext.RenderTargetTextureHandle, expectedSize, new System.Numerics.Vector2(0, 1), new System.Numerics.Vector2(1, 0));
        Scene.RenderUI();
        ImGui.SetCursorPos(c);
        ImGui.InvisibleButton("##image", expectedSize, ImGuiButtonFlags.MouseButtonLeft | ImGuiButtonFlags.MouseButtonRight);
        // need to store the click/hover events for after so we can handle clicks on the empty area below the info window same as a mesh image click event
        var meshClick = ImGui.IsItemClicked(ImGuiMouseButton.Right) || ImGui.IsItemClicked(ImGuiMouseButton.Left);
        var hoveredMesh = ImGui.IsItemHovered();

        // 3D view controls
        meshClick = meshClick || ImGui.IsItemClicked(ImGuiMouseButton.Right) || ImGui.IsItemClicked(ImGuiMouseButton.Left);

        if (meshClick) {
            if (!isDragging) {
                isDragging = true;
            }
        } else if (isDragging && !ImGui.IsMouseDown(ImGuiMouseButton.Left) && !ImGui.IsMouseDown(ImGuiMouseButton.Right)) {
            isDragging = false;
        }

        if (isDragging || hoveredMesh) {
            AppImguiHelpers.RedirectMouseInputToScene(Scene, hoveredMesh);
        }
    }
    private bool isDragging;

    private void ShowMenu()
    {
        // TODO scene menu bar
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
}
