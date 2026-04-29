using System.Numerics;
using ContentEditor.App.FileLoaders;
using ContentEditor.App.Windowing;
using ContentEditor.Core;
using ReeLib;

namespace ContentEditor.App.Widgets;

public class MdfBatchExporter
{
    private Dictionary<string, HashSet<string>>? _exportTextures;
    private string? _exportSelectedMat;
    private ExportFormats _exportFormat;
    private enum ExportFormats
    {
        DDS,
        PNG,
        TGA,
    }

    public bool Show(IEnumerable<MdfFile> files)
    {
        var allMats = files.SelectMany(f => f.Materials).GroupBy(m => m.Header.matName);
        if (_exportTextures == null) {
            _exportTextures = new();
            foreach (var mat in allMats) {
                var data = _exportTextures[mat.Key] = new ();
                foreach (var tex in mat.First().Textures) {
                    if (string.IsNullOrEmpty(tex.texPath)) continue;
                    if (MaterialGroupWrapper.AlbedoTextureNames.Contains(tex.texType)
                        || MaterialGroupWrapper.NormalTextureNames.Contains(tex.texType)
                        || MaterialGroupWrapper.ATXXTextureNames.Contains(tex.texType)) {
                        data.Add(tex.texPath);
                    }
                }
            }
        }
        ImGui.SeparatorText("Material texture export");
        var matNames = allMats.Select(m => m.Key).ToArray();
        if (ImGui.Button("Select All")) {
            foreach (var mat in allMats) {
                var data = _exportTextures[mat.Key] = new ();
                foreach (var tex in mat.First().Textures) {
                    if (string.IsNullOrEmpty(tex.texPath)) continue;
                    data.Add(tex.texPath);
                }
            }
        }
        if (ImguiHelpers.SameLine() && ImGui.Button("Clear Selection")) {
            _exportTextures.Clear();
        }
        if (ImguiHelpers.SameLine() && ImGui.Button("Reset Selection")) {
            _exportTextures = null;
        }
        ImGui.SameLine();
        ImGui.SetNextItemWidth(ImGui.GetFrameHeightWithSpacing() + ImGui.CalcTextSize("DDS").X);
        ImguiHelpers.CSharpEnumCombo("Export Format", ref _exportFormat);

        ImguiHelpers.ValueCombo("Material", matNames, matNames, ref _exportSelectedMat);
        if (!string.IsNullOrEmpty(_exportSelectedMat)) {
            ImGui.Separator();
            var mat = allMats.FirstOrDefault(mm => mm.Key == _exportSelectedMat);
            if (mat != null) {
                var area = ImGui.GetContentRegionAvail() - new Vector2(0, 32 * UI.UIScale + ImGui.GetStyle().FramePadding.Y);
                var data = _exportTextures?.GetValueOrDefault(_exportSelectedMat) ?? new();
                ImGui.BeginChild("TexList", area);
                foreach (var tex in mat.First().Textures) {
                    var selected = data.Contains(tex.texPath ?? "");
                    if (ImGui.Checkbox("##" + tex.texType, ref selected)) {
                        if (selected) {
                            data.Add(tex.texPath!);
                        } else {
                            data.Remove(tex.texPath!);
                        }
                        (_exportTextures ??= new())[_exportSelectedMat] = data;
                    }
                    ImGui.SameLine();
                    ImGui.Text($"{tex.texType} | {tex.texPath}");
                }
                ImGui.EndChild();
            }
        } else {
            ImGui.Dummy(new Vector2(200, 100));
        }
        var confirmed = false;
        ImGui.Separator();
        if (ImGui.Button("Cancel")) {
            _exportTextures = null;
            confirmed = true;
        }
        ImGui.SameLine();
        if (ImGui.Button("Export")) {
            var wnd = EditorWindow.CurrentWindow;
            PlatformUtils.ShowFolderDialog((target) => {
                wnd!.InvokeFromUIThread(() => {
                    if (_exportTextures == null) return;

                    foreach (var file in files) {
                        var fmt = _exportFormat switch {
                            ExportFormats.PNG => "png",
                            ExportFormats.TGA => "tga",
                            ExportFormats.DDS => "dds",
                            _ => "dds",
                        };
                        MaterialGroupLoader.ExportTextures(wnd.Workspace, file, target, fmt, (mat, param) => {
                            return param.texPath != null && _exportTextures.GetValueOrDefault(mat.Name)?.Contains(param.texPath) == true;
                        });
                    }
                    FileSystemUtils.ShowFileInExplorer(target);

                    _exportTextures = null;
                });
            });
            confirmed = true;
        }
        return confirmed;
    }

}
