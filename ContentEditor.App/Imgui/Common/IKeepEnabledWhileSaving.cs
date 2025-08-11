using System.Numerics;
using ContentEditor.App.Windowing;
using ContentEditor.Core;
using ImGuiNET;

namespace ContentEditor.App;

/// <summary>
/// Denotes that the window should stay enabled even while the app is saving data. Should not be used by file editors or windows embedded them.
/// </summary>
public interface IKeepEnabledWhileSaving : IWindowHandler { }
