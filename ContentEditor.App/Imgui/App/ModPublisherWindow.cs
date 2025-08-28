using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ContentEditor.App.Graphics;
using ContentEditor.App.ImguiHandling;
using ContentEditor.App.Windowing;
using ContentEditor.Core;
using ContentEditor.Editor;
using ContentPatcher;
using ImGuiNET;
using ReeLib;

namespace ContentEditor.App;

public class ModPublisherWindow : IWindowHandler
{
    public string HandlerName => " Publish Mod ";

    public bool HasUnsavedChanges => false;
    public ContentWorkspace Workspace { get; }

    private UIContext context = null!;

    public ModPublisherWindow(ContentWorkspace workspace)
    {
        Workspace = workspace;
    }

    public void Init(UIContext context)
    {
        this.context = context;
    }

    public void OnIMGUI()
    {
        if (Workspace.CurrentBundle == null) {
            ImGui.TextColored(Colors.Error, "There is no active bundle!");
            return;
        }
        var bundle = Workspace.CurrentBundle;
        var window = EditorWindow.CurrentWindow!;

        ImGui.Text("Active bundle: " + bundle.Name);
        ImGui.Text("Author: " + bundle.Author);
        ImGui.Text("Description: " + bundle.Description);
        ImGui.Text("Created at: " + bundle.CreatedAt);

        if (ImGui.Button("Edit metadata")) {
            window.ShowBundleManagement();
        }
        ImGui.SameLine();
        if (ImGui.Button("Publish as loose files ...")) {
            PlatformUtils.ShowFolderDialog((outputPath) => {
                var modconfig = Path.Combine(outputPath, "modinfo.ini");
                if (!File.Exists(modconfig)) {
                    File.WriteAllText(modconfig, bundle.ToModConfigIni());
                    Logger.Info("Created modinfo.ini in " + modconfig);
                }
                if (window.ApplyContentPatches(outputPath, bundle.Name)) {
                    File.WriteAllText(Path.Combine(outputPath, "bundle.json"), JsonSerializer.Serialize(bundle, JsonConfig.jsonOptions));
                }
            });
        }

        ImGui.SeparatorText("Content");
        if (bundle.Entities.Count > 0 && ImGui.TreeNode("Entities")) {
            foreach (var e in bundle.Entities) {
                ImGui.Text($"{e.Type} {e.Id} : {e.Label}");
            }

            ImGui.TreePop();
        }
        if (bundle.ResourceListing?.Count > 0 && ImGui.TreeNode("Files")) {
            foreach (var e in bundle.ResourceListing) {
                ImGui.PushID(e.Key);
                if (ImGui.Button("Open")) {
                    var path = Workspace.BundleManager.ResolveBundleLocalPath(bundle, e.Key);
                    if (!File.Exists(path)) {
                        window.OpenFiles([e.Value.Target]);
                    } else {
                        window.OpenFiles([path]);
                    }
                }
                ImGui.SameLine();

                if (e.Value.Diff != null && (e.Value.Diff is JsonObject odiff && odiff.Count > 1)) {
                    if (ImGui.Button("Show diff")) {
                        window.AddSubwindow(new JsonViewer(e.Value.Diff, $"{e.Key} => {e.Value.Target}"));
                    }
                    if (ImGui.IsItemHovered()) ImGui.SetItemTooltip("Partial patch generated at: " + e.Value.DiffTime.ToString("O"));
                    ImGui.SameLine();
                }
                ImGui.Text(e.Key);
                ImGui.SameLine();
                ImGui.TextColored(Colors.Faded, e.Value.Target);
                ImGui.PopID();
            }

            ImGui.TreePop();
        }
        if (bundle.Entities.Count == 0 && !(bundle.ResourceListing?.Count > 0)) {
            ImGui.TextColored(Colors.Info, "There is currently no content inside the bundle.");
        }
    }

    public void OnWindow() => this.ShowDefaultWindow(context);

    public bool RequestClose()
    {
        return false;
    }
}