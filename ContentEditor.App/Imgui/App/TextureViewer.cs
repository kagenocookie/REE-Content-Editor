using ContentEditor.App.Graphics;
using ContentEditor.App.Windowing;
using ContentEditor.Core;
using ContentPatcher;
using ImGuiNET;
using ReeLib;

namespace ContentEditor.App;

public class TextureViewer : IWindowHandler, IDisposable, IFocusableFileHandleReferenceHolder
{
    public bool HasUnsavedChanges => false;

    public string HandlerName => $"Texture Viewer";

    public bool IsClosable => true;

    private Texture? texture;
    private string? texturePath;

    private static readonly HashSet<string> StandardImageFileExtensions = [".png", ".bmp", ".gif", ".jpg", ".jpeg", ".webp", ".tga", ".tiff", ".qoi", ".dds"];

    private WindowData data = null!;
    protected UIContext context = null!;

    public TextureViewer(string path)
    {
        texturePath = path;
    }

    public TextureViewer(FileHandle file)
    {
        texturePath = file.Filepath;
        texture = new Texture();
        texture.LoadFromFile(file);
        file.References.Add(this);
    }

    public TextureViewer()
    {
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

    public void SetImageSource(string filepath)
    {
        texture?.Dispose();
        texturePath = filepath;
        texture = new Texture().LoadFromFile(filepath);
    }

    public void Init(UIContext context)
    {
        this.context = context;
        data = context.Get<WindowData>();
    }

    public void OnWindow()
    {
        if (!ImguiHelpers.BeginWindow(data, null, ImGuiWindowFlags.MenuBar)) {
            WindowManager.Instance.CloseWindow(data);
            return;
        }
        if (ImGui.BeginMenuBar()) {
            if (ImGui.MenuItem("Open ...")) {
                var window = EditorWindow.CurrentWindow!;
                PlatformUtils.ShowFileDialog((files) => {
                    window.InvokeFromUIThread(() => SetImageSource(files[0]));
                });
            }
            ImGui.EndMenuBar();
        }
        ImGui.BeginGroup();
        OnIMGUI();
        ImGui.EndGroup();
        ImGui.End();
    }
    public void OnIMGUI()
    {
        if (texture == null) {
            if (texturePath == null) {
                ImGui.Text("No texture selected");
                return;
            }

            this.SetImageSource(texturePath);
        }

        if (texture != null) {
            ImGui.Text(texture.Path);
            ImGui.Image((nint)texture.Handle, ImGui.GetWindowSize() - ImGui.GetCursorPos() - ImGui.GetStyle().WindowPadding);
        } else {
            ImGui.Text("No texture selected");
        }
    }

    public static bool IsSupportedFileExtension(string filepathOrExtension)
    {
        var format = PathUtils.ParseFileFormat(filepathOrExtension);
        if (format.format == KnownFileFormats.Texture) {
            return true;
        }

        return StandardImageFileExtensions.Contains(Path.GetExtension(filepathOrExtension));
    }

    public bool RequestClose()
    {
        return false;
    }

    public void Dispose()
    {
        texture?.Dispose();
        texture = null;
    }
}