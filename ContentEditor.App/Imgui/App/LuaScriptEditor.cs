using Assimp;
using ContentEditor.App.FileLoaders;
using ContentEditor.App.ImguiHandling;
using ContentEditor.App.Lua;
using ContentEditor.App.Windowing;
using ContentEditor.Core;
using ContentPatcher;
using ReeLib;

namespace ContentEditor.App;

public class LuaScriptEditor : FileEditor
{
    public override string HandlerName => "Lua Script Editor";
    public override bool HasUnsavedChanges => string.IsNullOrEmpty(Script.Script) ? !string.IsNullOrEmpty(originalScript) : Script.Script != originalScript;

    private string? originalScript;
    private string? currentGroup;
    private string? selectedGroup;
    private string[] Groups = [];
    private string[] FilesInCurrentGroup = [];

    private string groupFilter = "";
    // private string scriptFilter = "";

    public LuaScript Script => (LuaScript)Handle.Resource;

    private string CurrentGroupFolder => Path.Combine(AppConfig.Instance.LuaUserPath, currentGroup ?? "");

    private string? currentScriptPath;
    private string? currentScriptPathRelative;

    public LuaScriptEditor(ContentWorkspace workspace, FileHandle file) : base(file)
    {
        Workspace = workspace;
    }

    public LuaScriptEditor(ContentWorkspace workspace, FileHandle file, IWindowHandler windowContext) : base(file)
    {
        Workspace = workspace;
    }

    public ContentWorkspace Workspace { get; }

    protected override string GetSavePathSuggestion(FileHandle handle)
    {
        if (handle.HandleType == FileHandleType.New) {
            if (!Directory.Exists(CurrentGroupFolder)) {
                FileSystemUtils.EnsureDirectoryExists(CurrentGroupFolder);
            }
            return Path.Combine(CurrentGroupFolder, handle.Filename.ToString());
        }
        return base.GetSavePathSuggestion(handle);
    }

    public override void Init(UIContext context)
    {
        base.Init(context);
        currentScriptPath = Handle.Filepath;
        var scriptBasePath = AppConfig.Instance.LuaUserPath.NormalizeFilepath();
        if (scriptBasePath.StartsWith(currentScriptPath.NormalizeFilepath())) {
            var relativeParts = Path.GetRelativePath(scriptBasePath, currentScriptPath).Normalize().Split('/', 2);
            if (relativeParts.Length == 1) {
                currentGroup = selectedGroup = "";
            } else {
                currentGroup = selectedGroup = relativeParts[0];
            }
        }
        ScanScriptFolder();
        originalScript = Script.Script;
    }

    public void OpenScript(string scriptPath, string? relativePath)
    {
        if (HasUnsavedChanges) {
            ShowSaveConfirmation(false);
            return;
        }
        if (Workspace.ResourceManager.TryGetOrLoadFile(scriptPath, out var newHandle)) {
            DisconnectFile();
            Handle = newHandle;
            ConnectFile();
        } else {
            Script.Script = File.ReadAllText(scriptPath);
        }
        originalScript = Script.Script;
        currentScriptPath = scriptPath;
        currentScriptPathRelative = relativePath;
    }

    private void RevertFile()
    {
        if (string.IsNullOrEmpty(currentScriptPath) || !File.Exists(currentScriptPath)) {
            originalScript = Script.Script;
            return;
        }

        originalScript = Script.Script = File.ReadAllText(currentScriptPath);
    }

    private void ScanScriptFolder()
    {
        var basePath = AppConfig.Instance.LuaUserPath;
        FilesInCurrentGroup = [];
        if (!Directory.Exists(basePath)) {
            return;
        }

        var gameName = Lang.TranslateGame(Workspace.Game.name);
        var groups = new List<string>();
        foreach (var folder in Directory.EnumerateDirectories(basePath)) {
            var relative = Path.GetRelativePath(basePath, folder).NormalizeFilepath();
            Logger.Info(relative);
            groups.Add(relative);
        }
        if (!groups.Contains("General")) groups.Add("General");
        if (!groups.Contains(gameName)) groups.Add(gameName);
        Groups = groups.ToArray();

        // TODO store last selected group?
        if (currentGroup == null) {
            SetScriptGroup(gameName);
        } else {
            ScanCurrentLuaFiles();
        }
    }

    protected override void OnFileSaved()
    {
        base.OnFileSaved();
        originalScript = Script.Script;
    }

    protected override void OnFileReverted()
    {
        base.OnFileReverted();
        originalScript = Script.Script;
    }

    private void SetScriptGroup(string group)
    {
        selectedGroup = currentGroup = group;
        ScanCurrentLuaFiles();
    }

    private void ScanCurrentLuaFiles()
    {
        var basePath = CurrentGroupFolder;
        if (!Directory.Exists(basePath)) {
            FilesInCurrentGroup = [];
            return;
        }

        var files = new List<string>();
        foreach (var folder in Directory.EnumerateFiles(basePath, "*.lua", SearchOption.AllDirectories)) {
            var relative = Path.GetRelativePath(basePath, folder).NormalizeFilepath();
            files.Add(relative);
        }

        FilesInCurrentGroup = files.ToArray();
    }

    public override void OnWindow() => this.ShowDefaultWindow(Context, $"{Handle.Filename.ToString()}###lua{WindowData.ID}");

    protected override void DrawFileControls(WindowData data)
    {
        if (ImGui.Button($"{AppIcons.SI_FileNew}")) {
            if (HasUnsavedChanges) {
                ShowSaveConfirmation(false);
            } else {
                DisconnectFile();
                Handle = Workspace.ResourceManager.CreateNewFile(LuaFileLoader.Instance, "Script", "lua")!;
                ConnectFile();
                originalScript = "";
                currentScriptPath = null;
                currentScriptPathRelative = null;
            }
        }

        ImguiHelpers.Tooltip("Create New Script");
        ImGui.SameLine();
        ImguiHelpers.VerticalSeparator();
        ImGui.SameLine();
        base.DrawFileControls(data);
    }

    protected override void PostFileConfirmation()
    {
        base.PostFileConfirmation();
        RevertFile();
    }

    protected override void DrawFileContents()
    {
        if (!AppConfig.Instance.DisableScriptSafetyWarning) {
            ImGui.TextColored(Colors.Warning, """
                Be careful when executing scripts you don't understand, custom scripts could affect things beyond what's normally allowed by the app,
                including altering important files or replacing / breaking your files. Think twice before running untrusted code.
                Most scripted actions do not support undo so it won't be possible to automatically revert changes after a file is saved.
                This warning can be disabled via right click.
                """u8);
            if (ImGui.IsItemHovered() && ImGui.IsItemClicked(ImGuiMouseButton.Right)) {
                ImGui.OpenPopup("WarningConfirmation");
            }
            if (ImGui.BeginPopup("WarningConfirmation")) {
                if (ImGui.Selectable("I understand, disable this warning")) {
                    AppConfig.Instance.DisableScriptSafetyWarning.Set(true);
                }
                ImGui.EndPopup();
            }
        }
        if (ImguiHelpers.FilterableCombo("Script Group", Groups, Groups, ref selectedGroup, ref groupFilter)) {
            SetScriptGroup(selectedGroup ?? "");
        }

        if (FilesInCurrentGroup.Length > 0) {
            if (ImguiHelpers.FilterableCombo("Stored Script", FilesInCurrentGroup, FilesInCurrentGroup, ref currentScriptPathRelative, ref groupFilter)) {
                if (!string.IsNullOrEmpty(currentScriptPathRelative)) {
                    var fullPath = Path.Combine(CurrentGroupFolder, currentScriptPathRelative);
                    if (File.Exists(fullPath)) {
                        OpenScript(fullPath, currentScriptPathRelative);
                    } else {
                        Logger.Error("Script file not found: " + fullPath);
                    }
                }
            }
        }

        if (!string.IsNullOrEmpty(selectedGroup)) {
            if (ImGui.Button($"{AppIcons.SI_FolderOpen}")) {
                FileSystemUtils.ShowFileInExplorer(CurrentGroupFolder);
            }
            ImguiHelpers.Tooltip("Open Script Folder");
            ImGui.SameLine();
        }
        AppImguiHelpers.WikiLinkButton("https://github.com/kagenocookie/REE-Content-Editor/wiki/Lua-API", true);
        ImGui.SameLine();
        if (ImGui.Button($"{AppIcons.SI_Reset}##rescan_scripts")) {
            ScanScriptFolder();
        }
        ImguiHelpers.Tooltip("Re-scan scripts folder");
        ImGui.SameLine();

        if (ImGui.Button("Execute Script"u8)) {
            var lua = LuaWrapper.Create(Workspace, EditorWindow.CurrentWindow);
            lua.Run(Script.Script);
        }

        ImGui.Text("Script");
        var avail = ImGui.GetContentRegionAvail();
        var currentScript = Script.Script;
        if (ImGui.InputTextMultiline("##Script", ref currentScript, 10000, avail)) {
            Script.Script = currentScript;
            Handle.Modified = true;
        }
    }
}
