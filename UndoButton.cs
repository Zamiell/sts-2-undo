using Godot;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes;

namespace Undo;

internal static class UndoButton
{
    public static void AddTo(Control topBar)
    {
        var button = new Button
        {
            Name = "UndoButton",
            Text = "Undo",
            FocusMode = Control.FocusModeEnum.None,
            Visible = false
        };
        button.AddThemeFontSizeOverride("font_size", 22);
        button.Pressed += () => TaskHelper.RunSafely(UndoService.Undo());
        topBar.AddChild(button);
        button.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopRight);
        button.OffsetLeft = -130;
        button.OffsetRight = -24;
        button.OffsetTop = 96;
        button.OffsetBottom = 142;

        var refresh = new Godot.Timer { WaitTime = 0.25, Autostart = true };
        refresh.Timeout += () =>
        {
            button.Visible = UndoService.IsSingleplayer;
            button.Disabled = !UndoService.CanUndo;
        };
        button.AddChild(refresh);
    }

    /// <summary>Hides the screen and blocks input while the run is rebuilt. The caller frees it.</summary>
    public static CanvasLayer ShowCover()
    {
        var cover = new CanvasLayer { Layer = 200 };
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
        NGame.Instance!.AddChild(cover);
        background.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        label.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        return cover;
    }
}
