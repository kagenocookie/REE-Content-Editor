namespace ContentEditor.App.Windowing;

using System;
using System.Diagnostics;
using System.Numerics;
using ContentEditor.Core;
using ContentPatcher;
using ImGuiNET;
using ReeLib;
using Silk.NET.Windowing;

public class MainWindow : EditorWindow
{
    internal MainWindow() : base(0)
    {
        GameChanged += OnGameChanged;
    }

    internal override void InitializeWindow()
    {
        var windowSize = AppConfig.Instance.WindowRect.Get();
        InitWindowEvents(new WindowOptions(
            isVisible: true,
            position: new Silk.NET.Maths.Vector2D<int>((int)windowSize.X, (int)windowSize.Y),
            size: new Silk.NET.Maths.Vector2D<int>((int)windowSize.Z, (int)windowSize.W),
            framesPerSecond: 0.0,
            updatesPerSecond: 0.0,
            GraphicsAPI.Default,
            title: "REE Content Editor v" + AppConfig.Version,
            WindowState.Normal,
            WindowBorder.Resizable,
            isVSync: true,
            shouldSwapAutomatically: true,
            VideoMode.Default
        ));
        if (!string.IsNullOrWhiteSpace(AppConfig.Instance.MainSelectedGame)) {
            // TODO proper workspace selection (allow multiple workspaces for same bundle)
            SetWorkspace(new GameIdentifier(AppConfig.Instance.MainSelectedGame!), AppConfig.Instance.MainActiveBundle);
        }
    }

    private void OnGameChanged()
    {
        var newGame = env?.Config.Game.name;
        if (AppConfig.Instance.MainSelectedGame.Get() != newGame) {
            AppConfig.Instance.MainSelectedGame.Set(newGame);
        }
        if (AppConfig.Instance.MainActiveBundle.Get() != workspace?.Data.ContentBundle) {
            AppConfig.Instance.MainActiveBundle.Set(workspace?.Data.ContentBundle);
        }
    }
}