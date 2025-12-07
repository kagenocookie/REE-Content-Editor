using ContentEditor.App.Windowing;
using ContentEditor.Core;
using ContentPatcher;
using ReeLib;
using ReeLib.Msg;

namespace ContentEditor.App.ImguiHandling;

public class MsgFileEditor : FileEditor, IWorkspaceContainer
{
    public override bool HasUnsavedChanges => context.Changed;

    public override string HandlerName => "Msg";
    public string Filename => Handle.Filepath;

    public MsgFile File { get; }

    public ContentWorkspace Workspace { get; }

    private Language selectedLanguage = Language.English; // TODO user-defined default language
    private int selectedRow = -1;
    private string filter = string.Empty;

    private bool scrollToSelected;

    private static string[] LangOptions = Enum.GetValues<Language>().Where(v => v != Language.Max).Select(a => a.ToString()).ToArray();
    private static Language[] LangValues = Enum.GetValues<Language>().Where(v => v != Language.Max).ToArray();

    public MsgFileEditor(ContentWorkspace env, FileHandle file) : base(file)
    {
        Workspace = env;
        File = file.GetFile<MsgFile>();
    }

    private sealed class MessageNameComparer : Singleton<MessageNameComparer>, IComparer<MessageEntry>
    {
        public int Compare(MessageEntry? x, MessageEntry? y) => x == null || y == null ? 0 : x.Header.entryName.CompareTo(y.Header.entryName);
    }
    private sealed class MessageTextComparer(int langIndex) : IComparer<MessageEntry>
    {
        public int Compare(MessageEntry? x, MessageEntry? y) => x == null || y == null ? 0 : x.Strings[langIndex].CompareTo(y.Strings[langIndex]);

        public static readonly MessageTextComparer[] LanguageComparers = Enum.GetValues<Language>().Select(l => new MessageTextComparer((int)l)).ToArray();
    }

    private record struct IndexedItem(MessageEntry entry, int index);

    private bool IsFiltered(MessageEntry entry)
    {
        var filter = this.filter.Trim();
        if (string.IsNullOrEmpty(filter)) return true;
        if (entry.Name.Contains(filter, StringComparison.InvariantCultureIgnoreCase)) return true;
        var str = entry.Strings[(int)selectedLanguage];
        if (str.Contains(filter, StringComparison.InvariantCultureIgnoreCase)) return true;

        if (Guid.TryParse(filter, out var guid) && guid == entry.Guid) {
            return true;
        }

        return false;
    }

    protected override void DrawFileContents()
    {
        ImGui.PushID(Filename);
        if (File.Entries.Count == 0) {
            File.Read();
        }

        if (ImGui.Button("Create new entry")) {
            var entry = File.AddNewEntry();
            UndoRedo.RecordCallback(
                context,
                () => { if (!File.Entries.Contains(entry)) File.Entries.Add(entry); },
                () => File.Entries.Remove(entry)
            );
            selectedRow = File.Entries.Count - 1;
            scrollToSelected = true;
            filter = string.Empty;
        }
        ImGui.SameLine();
        if (ImGui.Button("Paste as new entry")) {
            var clipboard = EditorWindow.CurrentWindow?.GetClipboard();
            if (clipboard != null) {
                try {
                    var msg = MessageData.FromJson(clipboard);
                    Logger.Debug("Copied message: ", msg);
                    MessageEntry entry;
                    if (File.FindEntryByKey(msg.MessageKey) != null) {
                        entry = File.AddNewEntry();
                    } else {
                        entry = File.AddNewEntry(msg.MessageKey, msg.Guid);
                    }
                    msg.MessagesToEntry(entry);
                    selectedRow = File.Entries.Count - 1;
                    scrollToSelected = true;
                    filter = string.Empty;
                    UndoRedo.RecordCallback(
                        context,
                        () => { if (!File.Entries.Contains(entry)) File.Entries.Add(entry); },
                        () => File.Entries.Remove(entry)
                    );
                } catch (Exception e) {
                    Logger.Error(e, "Failed to paste new message entry");
                }
            }
        }
        ImGui.SameLine();
        ImGui.SetNextItemWidth(Math.Min(300, ImGui.CalcItemWidth()));
        ImguiHelpers.ValueCombo("Language", LangOptions, LangValues, ref selectedLanguage);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(Math.Min(400, ImGui.CalcItemWidth() - ImGui.GetCursorPosX()));
        ImGui.InputText("Filter", ref filter, 128);

        var size = ImGui.GetWindowSize() - ImGui.GetCursorPos();
        var w = size.X;
        var msgListHovered = false;
        ImGui.BeginChild("msg_list", new System.Numerics.Vector2(w, size.Y), ImGuiChildFlags.ResizeX);
        var langIndex = (int)selectedLanguage;
        if (ImGui.BeginTable("Messages", 3, ImGuiTableFlags.Sortable | ImGuiTableFlags.Resizable | ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingStretchProp)) {
            ImGui.TableSetupColumn("Index", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize, 60 * UI.UIScale);
            ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch, 0.3f);
            ImGui.TableSetupColumn("Message", ImGuiTableColumnFlags.WidthStretch, 0.7f);
            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableHeadersRow();
            var sort = ImGui.TableGetSortSpecs();
            ImGui.TableNextColumn();
            var entries = File.Entries.Select((e, i) => new IndexedItem(e, i));
            var sortedEntries = sort.Specs.ColumnIndex switch {
                1 => sort.Specs.SortDirection == ImGuiSortDirection.Ascending ? entries.OrderBy(e => e.entry, MessageNameComparer.Instance) : entries.OrderByDescending(e => e.entry, MessageNameComparer.Instance),
                2 => sort.Specs.SortDirection == ImGuiSortDirection.Ascending ? entries.OrderBy(e => e.entry, MessageTextComparer.LanguageComparers[langIndex]) : entries.OrderByDescending(e => e.entry, MessageTextComparer.LanguageComparers[langIndex]),
                _ => sort.Specs.SortDirection == ImGuiSortDirection.Ascending ? entries : entries.Reverse(),
            };

            foreach (var item in sortedEntries) {
                if (!IsFiltered(item.entry)) {
                    continue;
                }
                if (ImGui.Selectable(item.index.ToString(), item.index == selectedRow, ImGuiSelectableFlags.SpanAllColumns)) {
                    selectedRow = item.index;
                }
                var entry = item.entry;
                ImGui.TableNextColumn();
                var name = entry.Header.entryName;
                ImGui.Text(name);
                ImGui.TableNextColumn();
                var str = entry.Strings[langIndex];
                {
                    var nl = str.IndexOf('\n');
                    if (nl != -1) str = str.Substring(0, nl).TrimEnd();
                    ImGui.Text(str);
                }
                if (scrollToSelected && item.index == selectedRow) {
                    ImguiHelpers.ScrollItemIntoView();
                    scrollToSelected = false;
                }
                ImGui.TableNextColumn();
            }
            msgListHovered = ImGui.IsWindowHovered();
            ImGui.EndTable();
        }
        ImGui.EndChild();
        if (selectedRow != -1 && msgListHovered) {
            if (ImGui.IsKeyPressed(ImGuiKey.Delete)) {
                ShowDeleteConfirm(selectedRow);
            }
            if (ImGui.IsKeyPressed(ImGuiKey.DownArrow) && selectedRow < File.Entries.Count - 1) {
                var next = File.Entries.Where((e, i) => i > selectedRow && IsFiltered(e)).FirstOrDefault();
                if (next != null) {
                    selectedRow = File.Entries.IndexOf(next);
                    scrollToSelected = true;
                }
            }
            if (ImGui.IsKeyPressed(ImGuiKey.UpArrow) && selectedRow > 0) {
                var next = File.Entries.Where((e, i) => i < selectedRow && IsFiltered(e)).LastOrDefault();
                if (next != null) {
                    selectedRow = File.Entries.IndexOf(next);
                    scrollToSelected = true;
                }
            }
        }
        if (selectedRow >= 0 && selectedRow < File.Entries.Count) {
            var selected = File.Entries[selectedRow];
            ImGui.SameLine();
            ImGui.BeginChild("Message " + selected.Header.entryName);
            if (ImGui.Button("Delete entry")) {
                ShowDeleteConfirm(selectedRow);
            }
            ImGui.SameLine();
            if (ImGui.Button("Copy entry")) {
                var data = new MessageData(selected, Filename, "");
                EditorWindow.CurrentWindow?.CopyToClipboard(data.ToJson().ToJsonString(), "Entry copied!");
            }
            ImGui.Text("Guid: " + selected.Header.guid);
            ImGui.SameLine();
            if (ImGui.Button("Copy")) {
                EditorWindow.CurrentWindow?.CopyToClipboard(selected.Header.guid.ToString(), "GUID copied!");
            }
            var prevname = selected.Header.entryName;
            if (ImGui.InputText("Name", ref selected.Header.entryName, 128)) {
                UndoRedo.RecordCallbackSetter(context,
                    selected,
                    prevname,
                    selected.Header.entryName,
                    (entry, val) => entry.Header.entryName = val,
                    $"{context} {selected.Header.entryName} Name"
                );
                Handle.Modified = true;
            }
            var msg = selected.Strings[langIndex];
            if (ImGui.InputTextMultiline("Message", ref msg, 1024, new System.Numerics.Vector2(ImGui.CalcItemWidth(), 300))) {
                UndoRedo.RecordCallbackSetter(context,
                    selected,
                    selected.Strings[langIndex],
                    msg,
                    (entry, val) => entry.Strings[langIndex] = val,
                    $"{context} {selected.Header.entryName} Message"
                );
                Handle.Modified = true;
            }
            ImGui.SeparatorText("Attributes");
            ImGui.BeginChild("Attributes");
            for (int i = 0; i < File.AttributeItems.Count; i++) {
                var attr = File.AttributeItems[i];
                var attrValue = selected.AttributeValues![i];
                var attrLabel = string.IsNullOrEmpty(attr.Name) ? $"[Attr {i}]" : attr.Name;
                switch (attr.ValueType) {
                    case AttributeValueType.Long:
                        var l = (long)attrValue;
                        if (ImguiHelpers.InputScalar(attrLabel, ImGuiDataType.S64, ref l)) {
                            Handle.Modified = true;
                            UndoRedo.RecordCallbackSetter(context,
                                selected,
                                (long)attrValue,
                                l,
                                (entry, val) => selected.AttributeValues[i] = val,
                                $"{context} {selected.Header.entryName} Attribute {i}"
                            );
                        }
                        break;
                    case AttributeValueType.Double:
                        var d = (double)attrValue;
                        if (ImGui.InputDouble(attrLabel, ref d)) {
                            UndoRedo.RecordCallbackSetter(context,
                                selected,
                                (double)attrValue,
                                d,
                                (entry, val) => selected.AttributeValues[i] = val,
                                $"{context} {selected.Header.entryName} Attribute {i}"
                            );
                            Handle.Modified = true;
                        }
                        break;
                    case AttributeValueType.String:
                        var s = (string)attrValue;
                        if (ImGui.InputText(attrLabel, ref s, 100)) {
                            Handle.Modified = true;
                            UndoRedo.RecordCallbackSetter(context,
                                selected,
                                (string)attrValue,
                                s,
                                (entry, val) => selected.AttributeValues[i] = val,
                                $"{context} {selected.Header.entryName} Attribute {i}"
                            );
                        }
                        break;
                    case AttributeValueType.Empty:
                        ImGui.Text("Empty: " + attrLabel);
                        break;
                }
            }
            ImGui.EndChild();
            ImGui.EndChild();
        }
        ImGui.PopID();
    }

    private void ShowDeleteConfirm(int index)
    {
        var entry = File.Entries[index];
        EditorWindow.CurrentWindow!.AddSubwindow(new ConfirmationDialog("Deleting entry", $"Are you sure you wish to delete the entry {entry.Name}?", this, () => {
            selectedRow = -1;
            UndoRedo.RecordCallback(
                context,
                () => File.Entries.Remove(entry),
                () => {
                    if (index >= File.Entries.Count - 1) File.Entries.Insert(index, entry);
                    else File.Entries.Add(entry);
                }
            );
        }));
    }
}
