
using System.Numerics;
using ContentEditor.App.Windowing;
using ContentEditor.Core;
using ContentEditor.Editor;

namespace ContentEditor.App;

public class ErrorModal : IWindowHandler, IDisposable
{
    public bool HasUnsavedChanges => false;

    public string HandlerName => "Error";

    private readonly TranslatableBase title;
    private readonly TranslatableBase text;
    private readonly IRectWindow? parent;
    public Action? OnClosed;
    private bool isOpen = false;

    private WindowData data = null!;
    protected UIContext context = null!;

    public ErrorModal(TranslatableBase title, TranslatableBase text, IRectWindow? parent = null, Action? onClosed = null)
    {
        this.title = title;
        this.text = text;
        this.parent = parent;
        OnClosed = onClosed;
    }

    public void Init(UIContext context)
    {
        this.context = context;
        data = context.Get<WindowData>();
    }
    public void OnWindow()
    {
        OnIMGUI();
    }

    public void OnIMGUI()
    {
        if (!isOpen) {
            ImGui.OpenPopup(title);
            isOpen = true;
        }
        AppImguiHelpers.ShowActionModal(title, $"{AppIcons.SI_GenericError}", Colors.IconTertiary, text, null, AppImguiHelpers.ActionModalType.Error);
    }

    public bool RequestClose()
    {
        return false;
    }

    public void Dispose()
    {
        OnClosed?.Invoke();
    }
}
