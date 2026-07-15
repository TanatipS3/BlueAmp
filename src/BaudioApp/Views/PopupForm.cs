using System.Collections.Specialized;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using BaudioApp.Models;
using BaudioApp.Services;
using BaudioApp.ViewModels;

namespace BaudioApp.Views;

/// The tray popup, rendered entirely with GDI+ on one borderless form. No child
/// controls — keeps the memory footprint tiny and gives full control over the
/// dark, rounded look the WPF version had.
///
/// Fonts are point-sized and therefore scale with monitor DPI, so every layout
/// value must scale with them — S() maps design pixels (96 DPI) to device pixels.
internal sealed class PopupForm : Form
{
    private const int WaveSlots = 24;

    private float _scale = 1f;

    private int S(int v)    => (int)MathF.Round(v * _scale);
    private int W           => S(320);
    private int Pad         => S(12);
    private int InnerW      => W - 2 * Pad;
    private int Radius      => S(10);
    private int RowH        => S(42);
    private int IconCol     => S(28);

    private readonly TrayPopupViewModel _vm;
    private readonly DiagnosticService _diag;
    private readonly System.Windows.Forms.Timer _anim;
    private readonly List<(Rectangle bounds, string label, Action onClick)> _hotspots = new();

    private int _spinAngle;
    private double _blinkPhase;

    public PopupForm(TrayPopupViewModel vm, DiagnosticService diag)
    {
        _vm = vm;
        _diag = diag;
        _scale = DeviceDpi / 96f;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar   = false;
        StartPosition   = FormStartPosition.Manual;
        BackColor       = Theme.PopupBg;
        DoubleBuffered  = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        ClientSize      = new Size(W, S(200));

        _anim = new System.Windows.Forms.Timer { Interval = 60 };
        _anim.Tick += OnAnimTick;

        _vm.PropertyChanged += OnVmChanged;
        _vm.Devices.CollectionChanged += OnDevicesChanged;
        _vm.PeakHistory.CollectionChanged += OnPeakChanged;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            const int CS_DROPSHADOW = 0x00020000;
            var cp = base.CreateParams;
            cp.ClassStyle |= CS_DROPSHADOW;
            cp.ExStyle |= 0x80; // WS_EX_TOOLWINDOW — keep out of Alt+Tab
            return cp;
        }
    }

    public void ShowNearTray()
    {
        Relayout();
        var screen = Screen.FromPoint(Cursor.Position).WorkingArea;
        Location = new Point(screen.Right - Width - S(12), screen.Bottom - Height - S(12));
        Show();
        Activate();
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        _vm.SetPopupVisible(Visible);
        if (Visible) _anim.Start(); else _anim.Stop();
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        Hide();
    }

    // ── Reactive repaint ───────────────────────────────────────────────────────

    private void OnVmChanged(object? s, PropertyChangedEventArgs e) => RelayoutAndRepaint();
    private void OnDevicesChanged(object? s, NotifyCollectionChangedEventArgs e) => RelayoutAndRepaint();
    private void OnPeakChanged(object? s, NotifyCollectionChangedEventArgs e) { if (Visible) Invalidate(); }

    private void RelayoutAndRepaint()
    {
        if (IsDisposed) return;
        Relayout();
        Invalidate();
    }

    private void OnAnimTick(object? s, EventArgs e)
    {
        if (_vm.OverallState == DeviceConnectionState.Connecting ||
            _vm.Devices.Any(d => d.State == DeviceConnectionState.Connecting))
        {
            _spinAngle = (_spinAngle + 30) % 360;
            _blinkPhase += 0.18;
            Invalidate();
        }
    }

    // ── Layout (height depends on state) ───────────────────────────────────────

    private void Relayout()
    {
        _scale = DeviceDpi / 96f;  // popup may have moved to a monitor with different DPI
        int h = MeasureHeight();
        var size = new Size(W, h);
        if (ClientSize != size) ClientSize = size;
        Region?.Dispose();
        using var path = Theme.RoundedRect(new RectangleF(0, 0, W, h), Radius);
        Region = new Region(path);
    }

    private int MeasureHeight()
    {
        int y = Pad;
        y += S(20) + S(10);    // header
        y += 1 + S(6);         // divider

        if (_vm.Devices.Count == 0) y += S(44);
        else y += _vm.Devices.Count * RowH;

        if (_vm.HasEarbud) y += RowH;   // earbud handoff row

        if (_vm.OverallState == DeviceConnectionState.Connected)
            y += S(10) + S(50) + S(6) + S(64);   // stats boxes + waveform
        else if (_vm.OverallState == DeviceConnectionState.Error)
            y += S(10) + MeasureErrorHeight();

        y += S(12) + S(18);    // footer
        y += Pad;
        return y;
    }

    private int MeasureErrorHeight()
    {
        var msg = _vm.ErrorMessage ?? "Connection failed";
        var sz = TextRenderer.MeasureText(msg, Theme.F(9.5f), new Size(InnerW - S(20), 0), TextFormatFlags.WordBreak);
        return Math.Max(S(36), sz.Height + S(16));
    }

    // ── Paint ──────────────────────────────────────────────────────────────────

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        _hotspots.Clear();

        // Background + subtle border
        var full = new RectangleF(0.5f, 0.5f, W - 1, ClientSize.Height - 1);
        Theme.FillRounded(g, new RectangleF(0, 0, W, ClientSize.Height), Radius, Theme.PopupBg);
        Theme.DrawRoundedBorder(g, full, Radius, Theme.FromHex("#18FFFFFF"));

        int y = Pad;
        y = DrawHeader(g, y);
        y = DrawDivider(g, y);

        if (_vm.Devices.Count == 0) y = DrawEmpty(g, y);
        else foreach (var d in _vm.Devices.ToList()) y = DrawDeviceRow(g, y, d);

        if (_vm.HasEarbud) y = DrawEarbudRow(g, y);

        if (_vm.OverallState == DeviceConnectionState.Connected) y = DrawStats(g, y);
        else if (_vm.OverallState == DeviceConnectionState.Error)  y = DrawError(g, y);

        DrawFooter(g, y);
    }

    private int DrawHeader(Graphics g, int y)
    {
        // Status dot
        var dotColor = _vm.OverallState switch
        {
            DeviceConnectionState.Connected  => Theme.Accent,
            DeviceConnectionState.Connecting => Theme.Warn,
            DeviceConnectionState.Error      => Theme.Error,
            _                                => Theme.TextMuted
        };
        int alpha = 255;
        if (_vm.OverallState == DeviceConnectionState.Connecting)
            alpha = (int)(140 + 115 * Math.Abs(Math.Sin(_blinkPhase)));
        using (var b = new SolidBrush(Color.FromArgb(alpha, dotColor)))
            g.FillEllipse(b, Pad, y + S(6), S(8), S(8));

        var titleFont = Theme.F(10.5f, FontStyle.Bold);
        int titleW = TextRenderer.MeasureText("BlueAmp", titleFont).Width;
        TextRenderer.DrawText(g, "BlueAmp", titleFont,
            new Rectangle(Pad + S(16), y, titleW + S(4), S(20)), Theme.TextPrimary,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

        if (_vm.OverallState == DeviceConnectionState.Connected && _vm.ConnectedDevice is not null)
        {
            int nameX = Pad + S(16) + titleW + S(10);
            TextRenderer.DrawText(g, _vm.ConnectedDevice.Name, Theme.F(9f),
                new Rectangle(nameX, y, W - Pad - nameX, S(20)), Theme.TextMuted,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        return y + S(20) + S(10);
    }

    private int DrawDivider(Graphics g, int y)
    {
        using var p = new Pen(Theme.Divider);
        g.DrawLine(p, Pad, y, W - Pad, y);
        return y + 1 + S(6);
    }

    private int DrawEmpty(Graphics g, int y)
    {
        TextRenderer.DrawText(g, "No paired Bluetooth audio devices found", Theme.F(9.5f),
            new Rectangle(Pad, y, InnerW, S(44)), Theme.TextFaint,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
        return y + S(44);
    }

    private int DrawDeviceRow(Graphics g, int y, BluetoothDeviceInfo d)
    {
        var stateColor = d.State switch
        {
            DeviceConnectionState.Connected  => Theme.Accent,
            DeviceConnectionState.Connecting => Theme.Warn,
            DeviceConnectionState.Error      => Theme.Error,
            _                                => Theme.TextMuted
        };

        DrawBluetoothGlyph(g, new Rectangle(Pad, y + (RowH - S(16)) / 2, IconCol, S(16)), stateColor);

        int textX = Pad + IconCol + S(8);
        int btnW = DrawRowButton(g, y, d); // returns width consumed on the right
        int nameW = InnerW - IconCol - S(8) - btnW - S(8);

        TextRenderer.DrawText(g, d.Name, Theme.F(10.5f, FontStyle.Regular),
            new Rectangle(textX, y + S(5), nameW, S(18)), Theme.TextPrimary,
            TextFormatFlags.Left | TextFormatFlags.EndEllipsis);

        (string sub, Color subColor) = d.State switch
        {
            DeviceConnectionState.Connecting => ("Connecting…", Theme.Warn),
            DeviceConnectionState.Connected  => ("Connected · A2DP", Theme.Accent),
            DeviceConnectionState.Error      => ("Connection failed", Theme.Error),
            _                                => ("Available", Theme.TextSecondary)
        };
        TextRenderer.DrawText(g, sub, Theme.F(9f),
            new Rectangle(textX, y + S(22), nameW, S(15)), subColor, TextFormatFlags.Left | TextFormatFlags.EndEllipsis);

        return y + RowH;
    }

    /// Draws the right-aligned action for a row and returns the width it occupies.
    private int DrawRowButton(Graphics g, int rowY, BluetoothDeviceInfo d)
    {
        int cy = rowY + RowH / 2;
        switch (d.State)
        {
            case DeviceConnectionState.Available:
                return Button(g, "Connect", cy, Theme.Accent, Color.Black, filled: true,
                    () => _vm.ConnectCommand.Execute(d));

            case DeviceConnectionState.Connected:
                return Button(g, "Disconnect", cy, Theme.Surface3, Theme.TextSecondary, filled: true,
                    () => _vm.DisconnectCommand.Execute(null));

            case DeviceConnectionState.Error:
                return Button(g, "Retry", cy, Color.FromArgb(30, Theme.Error), Theme.Error, filled: true,
                    () => _vm.ConnectCommand.Execute(d));

            case DeviceConnectionState.Connecting:
                // spinner + Cancel
                int w = Button(g, "Cancel", cy, Color.Transparent, Theme.TextSecondary, filled: false,
                    () => _vm.CancelCommand.Execute(null));
                int spinX = W - Pad - w - S(18);
                DrawSpinner(g, new Rectangle(spinX, cy - S(7), S(14), S(14)));
                return w + S(20);
        }
        return 0;
    }

    // ── Earbud handoff row ─────────────────────────────────────────────────────

    private int DrawEarbudRow(Graphics g, int y)
    {
        var loc = _vm.EarbudLocation;
        var stateColor = loc switch
        {
            EarbudLocation.OnPc    => Theme.Accent,
            EarbudLocation.OnPhone => Theme.TextSecondary,
            _                      => Theme.TextMuted
        };

        DrawHeadphoneGlyph(g, new Rectangle(Pad, y + (RowH - S(16)) / 2, IconCol, S(16)), stateColor);

        int textX = Pad + IconCol + S(8);
        int btnW  = DrawEarbudButton(g, y);
        int nameW = InnerW - IconCol - S(8) - btnW - S(8);

        TextRenderer.DrawText(g, _vm.EarbudName, Theme.F(10.5f, FontStyle.Regular),
            new Rectangle(textX, y + S(5), nameW, S(18)), Theme.TextPrimary,
            TextFormatFlags.Left | TextFormatFlags.EndEllipsis);

        TextRenderer.DrawText(g, _vm.EarbudStatusText, Theme.F(9f),
            new Rectangle(textX, y + S(22), nameW, S(15)), stateColor,
            TextFormatFlags.Left | TextFormatFlags.EndEllipsis);

        return y + RowH;
    }

    private int DrawEarbudButton(Graphics g, int rowY)
    {
        int cy = rowY + RowH / 2;
        bool onPc = _vm.EarbudLocation == EarbudLocation.OnPc;
        return Button(g, _vm.EarbudButtonLabel, cy,
            onPc ? Theme.Surface3 : Theme.Accent,
            onPc ? Theme.TextSecondary : Color.Black,
            filled: true,
            () => _vm.EarbudToggleCommand.Execute(null));
    }

    private void DrawHeadphoneGlyph(Graphics g, Rectangle box, Color color)
    {
        // Headband arc over two earcups, defined on a 24x24 grid.
        float s = box.Width / 24f;
        float X(float v) => box.X + v * s;
        float Y(float v) => box.Y + v * s;
        using var pen = new Pen(color, 1.8f * _scale) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawArc(pen, X(5), Y(4), 14 * s, 14 * s, 180, 180);
        Theme.FillRounded(g, new RectangleF(X(4), Y(11), 3.5f * s, 7 * s), 1.5f * s, color);
        Theme.FillRounded(g, new RectangleF(X(16.5f), Y(11), 3.5f * s, 7 * s), 1.5f * s, color);
    }

    private int Button(Graphics g, string text, int centerY, Color bg, Color fg, bool filled, Action onClick)
    {
        var font = Theme.F(9f, FontStyle.Bold);
        var sz = TextRenderer.MeasureText(text, font);
        int w = sz.Width + S(22), h = S(24);
        int x = W - Pad - w;
        var rect = new Rectangle(x, centerY - h / 2, w, h);
        if (filled) Theme.FillRounded(g, rect, S(4), bg);
        else Theme.DrawRoundedBorder(g, rect, S(4), Theme.FromHex("#22FFFFFF"));
        TextRenderer.DrawText(g, text, font, rect, fg,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        _hotspots.Add((rect, text, onClick));
        return w;
    }

    private void DrawSpinner(Graphics g, Rectangle r)
    {
        using var pen = new Pen(Theme.TextSecondary, 1.8f * _scale) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawArc(pen, r, _spinAngle, 280);
    }

    private void DrawBluetoothGlyph(Graphics g, Rectangle box, Color color)
    {
        // Classic single-stroke Bluetooth rune, defined on a 24x24 grid.
        float s = box.Width / 24f;
        PointF P(float px, float py) => new(box.X + px * s, box.Y + py * s);
        var pts = new[] { P(8.5f,7), P(15.5f,17), P(12,20.5f), P(12,3.5f), P(15.5f,7), P(8.5f,17) };
        using var pen = new Pen(color, 1.8f * _scale) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLines(pen, pts);
    }

    private int DrawStats(Graphics g, int y)
    {
        y += S(10);
        int boxW = (InnerW - S(8)) / 3;
        int boxH = S(50);
        DrawStatBox(g, new Rectangle(Pad, y, boxW, boxH), "SAMPLE RATE", _vm.SampleRateText, null);
        DrawStatBox(g, new Rectangle(Pad + boxW + S(4), y, boxW, boxH), "BIT DEPTH", _vm.BitDepthText, null);
        DrawStatBox(g, new Rectangle(Pad + 2 * (boxW + S(4)), y, InnerW - 2 * (boxW + S(4)), boxH), "QUALITY", null, _vm.SignalBars);
        y += boxH + S(6);

        // Waveform box
        var waveBox = new Rectangle(Pad, y, InnerW, S(64));
        Theme.FillRounded(g, waveBox, S(6), Theme.Surface);
        TextRenderer.DrawText(g, "Audio stream", Theme.F(8.5f),
            new Rectangle(waveBox.X + S(10), waveBox.Y + S(8), S(120), S(14)), Theme.TextMuted, TextFormatFlags.Left);
        bool active = _vm.ConnectedDevice?.IsAudioActive == true;
        TextRenderer.DrawText(g, active ? "Active" : "Silent", Theme.F(8.5f),
            new Rectangle(waveBox.Right - S(70), waveBox.Y + S(8), S(60), S(14)),
            active ? Theme.Accent : Theme.TextFaint, TextFormatFlags.Right);
        DrawWaveform(g, new Rectangle(waveBox.X + S(10), waveBox.Y + S(28), waveBox.Width - S(20), S(28)));
        return y + S(64);
    }

    private void DrawStatBox(Graphics g, Rectangle r, string label, string? value, int? signalBars)
    {
        Theme.FillRounded(g, r, S(6), Theme.Surface);
        TextRenderer.DrawText(g, label, Theme.F(7.5f, FontStyle.Bold),
            new Rectangle(r.X + S(10), r.Y + S(7), r.Width - S(12), S(12)), Theme.TextFaint,
            TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        if (value is not null)
            TextRenderer.DrawText(g, value, Theme.F(12f, FontStyle.Bold),
                new Rectangle(r.X + S(10), r.Y + S(20), r.Width - S(12), S(22)), Theme.TextPrimary,
                TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        else if (signalBars is int bars)
        {
            int bx = r.X + S(10), by = r.Y + S(40);
            int[] heights = { S(6), S(10), S(14), S(18) };
            for (int i = 0; i < 4; i++)
            {
                var on = i < bars;
                var rect = new Rectangle(bx + i * S(8), by - heights[i], S(5), heights[i]);
                Theme.FillRounded(g, rect, 1, on ? Theme.Accent : Theme.BarTrack);
            }
        }
        else
        {
            // No real signal data from Windows — show a dash instead of fake bars.
            TextRenderer.DrawText(g, "—", Theme.F(12f, FontStyle.Bold),
                new Rectangle(r.X + S(10), r.Y + S(20), r.Width - S(12), S(22)), Theme.TextMuted,
                TextFormatFlags.Left);
        }
    }

    private void DrawWaveform(Graphics g, Rectangle area)
    {
        var peaks = _vm.PeakHistory.ToList();
        int n = Math.Min(WaveSlots, peaks.Count);
        if (n == 0) return;
        float barW = 9, gap = 2;
        float totalW = n * (barW + gap);
        float scale = area.Width / totalW;
        barW *= scale; gap *= scale;
        float x = area.X;
        for (int i = peaks.Count - n; i < peaks.Count; i++)
        {
            double peak = peaks[i];
            float bh = Math.Max(2f * _scale, (float)peak * area.Height);
            var rect = new RectangleF(x, area.Bottom - bh, barW, bh);
            using var brush = new LinearGradientBrush(
                new PointF(rect.X, rect.Bottom), new PointF(rect.X, rect.Top),
                Theme.Accent, Theme.AccentLight);
            using var path = Theme.RoundedRect(rect, 2);
            g.FillPath(brush, path);
            x += barW + gap;
        }
    }

    private int DrawError(Graphics g, int y)
    {
        y += S(10);
        int h = MeasureErrorHeight();
        var rect = new Rectangle(Pad, y, InnerW, h);
        Theme.FillRounded(g, rect, S(6), Color.FromArgb(26, Theme.Error));
        Theme.DrawRoundedBorder(g, rect, S(6), Color.FromArgb(56, Theme.Error));
        TextRenderer.DrawText(g, _vm.ErrorMessage ?? "Connection failed", Theme.F(9.5f),
            new Rectangle(rect.X + S(10), rect.Y + S(8), rect.Width - S(20), h - S(16)), Theme.Error,
            TextFormatFlags.Left | TextFormatFlags.WordBreak);
        return y + h;
    }

    private void DrawFooter(Graphics g, int y)
    {
        y += S(12);
        int sx = Pad;
        sx += FooterLink(g, "Settings", sx, y, () => _vm.OpenSettingsCommand.Execute(null));
        TextRenderer.DrawText(g, "·", Theme.F(9f), new Rectangle(sx + S(2), y, S(10), S(16)), Theme.BarTrack, TextFormatFlags.Left);
        sx += S(12);
        FooterLink(g, "Diagnostics", sx, y, () => _vm.OpenDiagnosticsCommand.Execute(null));

        TextRenderer.DrawText(g, "BlueAmp v1", Theme.F(8f),
            new Rectangle(W - Pad - S(80), y, S(80), S(16)), Theme.BarTrack, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
    }

    private int FooterLink(Graphics g, string text, int x, int y, Action onClick)
    {
        var font = Theme.F(9f);
        var sz = TextRenderer.MeasureText(text, font);
        var rect = new Rectangle(x, y, sz.Width, S(16));
        TextRenderer.DrawText(g, text, font, rect, Theme.TextMuted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        _hotspots.Add((rect, text, onClick));
        return sz.Width;
    }

    // ── Mouse ──────────────────────────────────────────────────────────────────

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        foreach (var (bounds, label, onClick) in _hotspots)
            if (bounds.Contains(e.Location))
            {
                _diag.Info($"Popup: clicked '{label}'", tags: ["UI"]);
                onClick();
                return;
            }
        _diag.Info($"Popup: click at {e.Location} hit no target ({_hotspots.Count} targets)", tags: ["UI"]);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        Cursor = _hotspots.Any(h => h.bounds.Contains(e.Location)) ? Cursors.Hand : Cursors.Default;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _vm.PropertyChanged -= OnVmChanged;
            _vm.Devices.CollectionChanged -= OnDevicesChanged;
            _vm.PeakHistory.CollectionChanged -= OnPeakChanged;
            _anim.Dispose();
        }
        base.Dispose(disposing);
    }
}
