using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace BlueAmp.Views;

/// Owner-drawn iOS-style toggle, matching the WPF settings toggles.
internal sealed class ToggleSwitch : Control
{
    private bool _checked;

    public event EventHandler? CheckedChanged;

    public bool Checked
    {
        get => _checked;
        set { if (_checked != value) { _checked = value; Invalidate(); CheckedChanged?.Invoke(this, EventArgs.Empty); } }
    }

    public ToggleSwitch()
    {
        Size = new Size(38, 22);
        Cursor = Cursors.Hand;
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
    }

    protected override void OnClick(EventArgs e) { Checked = !Checked; base.OnClick(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var bg = new SolidBrush(Parent?.BackColor ?? Theme.WindowBg)) g.FillRectangle(bg, ClientRectangle);

        var track = new RectangleF(0, 0, Width - 1, Height - 1);
        Theme.FillRounded(g, track, Height / 2f, _checked ? Theme.Accent : Theme.Surface3);

        float d = Height - 6;
        float x = _checked ? Width - d - 3 : 3;
        using var knob = new SolidBrush(Color.White);
        g.FillEllipse(knob, x, 3, d, d);
    }
}

/// Flat rounded dark button.
internal sealed class DarkButton : Button
{
    public Color Fill { get; set; } = Theme.Surface2;
    public Color TextColor { get; set; } = Theme.TextSecondary;

    public DarkButton()
    {
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        Cursor = Cursors.Hand;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var bg = new SolidBrush(Parent?.BackColor ?? Theme.WindowBg)) g.FillRectangle(bg, ClientRectangle);
        var r = new RectangleF(0, 0, Width - 1, Height - 1);
        Theme.FillRounded(g, r, 5, Fill);
        TextRenderer.DrawText(g, Text, Font ?? Theme.F(9f), ClientRectangle, TextColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}
