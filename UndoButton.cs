using Godot;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes;

namespace Undo;

internal static class UndoButton
{
    private const float ButtonScale = 0.75f;

    public static void AddTo(Control topBar)
    {
        var button = CreateButton(topBar, "UndoButton", "Undo", 184, UndoService.Undo);
        var restartTurn = CreateButton(topBar, "RestartTurnButton", "Restart Turn", 238,
            UndoService.RestartTurn);
        var restart = CreateButton(topBar, "RestartCombatButton", "Restart Combat", 292,
            UndoService.RestartCombat);

        var refresh = new Godot.Timer { WaitTime = 0.25, Autostart = true };
        refresh.Timeout += () =>
        {
            button.Visible = UndoService.IsSingleplayer;
            button.Disabled = !UndoService.CanUndo;
            restartTurn.Visible = UndoService.IsSingleplayer;
            restartTurn.Disabled = !UndoService.CanRestartTurn;
            restart.Visible = UndoService.IsSingleplayer;
            restart.Disabled = !UndoService.CanRestartCombat;
        };
        button.AddChild(refresh);
    }

    private static Button CreateButton(Control topBar, string name, string text, float top, Func<Task> action)
    {
        var button = new Button
        {
            Name = name,
            Text = text,
            FocusMode = Control.FocusModeEnum.None,
            Visible = false,
            Scale = Vector2.One * ButtonScale
        };
        button.AddThemeFontSizeOverride("font_size", 22);
        button.AddThemeColorOverride("font_color", new Color("#f2e6cf"));
        button.AddThemeColorOverride("font_hover_color", Colors.White);
        button.AddThemeColorOverride("font_pressed_color", new Color("#f2e6cf"));
        button.AddThemeColorOverride("font_disabled_color", new Color("#8b969b"));
        button.AddThemeStyleboxOverride("normal", CreateStyle("#243b46", "#83958d"));
        button.AddThemeStyleboxOverride("hover", CreateStyle("#36535f", "#e1c994"));
        button.AddThemeStyleboxOverride("pressed", CreateStyle("#172a33", "#c2aa77"));
        button.AddThemeStyleboxOverride("disabled", CreateStyle("#202d34", "#536169"));
        button.Pressed += () => TaskHelper.RunSafely(action());
        topBar.AddChild(button);
        button.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopRight);
        button.OffsetLeft = -244;
        button.OffsetRight = -24;
        button.OffsetTop = 184 + (top - 184) * ButtonScale;
        button.OffsetBottom = button.OffsetTop + 46;
        button.PivotOffset = new Vector2(button.Size.X, 0);
        return button;
    }

    private static StyleBoxFlat CreateStyle(string background, string border) => new()
    {
        BgColor = new Color(background),
        BorderColor = new Color(border),
        BorderWidthLeft = 2,
        BorderWidthTop = 2,
        BorderWidthRight = 2,
        BorderWidthBottom = 2,
        CornerRadiusTopLeft = 6,
        CornerRadiusTopRight = 6,
        CornerRadiusBottomLeft = 6,
        CornerRadiusBottomRight = 6,
        ContentMarginLeft = 12,
        ContentMarginRight = 12,
        ContentMarginTop = 6,
        ContentMarginBottom = 6
    };

    /// <summary>Hides the screen and blocks input while the run is rebuilt. The caller frees it.</summary>
    public static CanvasLayer ShowCover()
    {
        // Keep the cover independent of the game scene and above embedded windows (layer 1024).
        var cover = new CanvasLayer { Layer = 1025 };
        var background = new ColorRect { Color = Colors.Black };
        var label = new Label
        {
            Text = "Undoing...",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        label.AddThemeFontSizeOverride("font_size", 36);
        cover.AddChild(background);
        cover.AddChild(label);
        NGame.Instance!.GetViewport().AddChild(cover);
        background.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        label.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        return cover;
    }
}
