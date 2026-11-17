using System.Collections.Specialized;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using BaudioApp.Models;
using BaudioApp.ViewModels;

namespace BaudioApp.Views;

internal sealed class DiagnosticsForm : Form
{
    private readonly DiagnosticViewModel _vm;
    private readonly ListBox _list;
    private readonly Label _count;
    private readonly Dictionary<string, DarkButton> _filterButtons = new();

    // One shared font for the timestamp column (was created anew for every row drawn).
    private static readonly Font TimestampFont = new("Consolas", 8f);

    public DiagnosticsForm(DiagnosticViewModel vm)
    {
        _vm = vm;

        Text          = "BlueAmp — Diagnostics";
        ClientSize    = new Size(580, 480);
        MinimumSize   = new Size(420, 320);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor     = Theme.WindowBg;
        Font          = Theme.F(9f);

        var toolbar = new Panel { Dock = DockStyle.Top, Height = 44, BackColor = Theme.Surface };
        int x = 12;
        foreach (var f in DiagnosticViewModel.Filters)
        {
            var btn = new DarkButton
            {
                Text = f, Size = new Size(TextRenderer.MeasureText(f, Theme.F(9f)).Width + 20, 26),
                Location = new Point(x, 9), Font = Theme.F(9f),
                Fill = f == "All" ? Theme.Accent : Theme.Surface2,
                TextColor = f == "All" ? Color.Black : Theme.TextSecondary
            };
            string filter = f;
            btn.Click += (_, _) => ApplyFilter(filter);
            _filterButtons[f] = btn;
            toolbar.Controls.Add(btn);
            x += btn.Width + 6;
        }

        var clear = new DarkButton { Text = "Clear", Size = new Size(60, 26), Anchor = AnchorStyles.Top | AnchorStyles.Right, Location = new Point(toolbar.Width - 72, 9) };
        clear.Click += (_, _) => _vm.ClearCommand.Execute(null);
        var copy = new DarkButton { Text = "Copy", Size = new Size(60, 26), Anchor = AnchorStyles.Top | AnchorStyles.Right, Location = new Point(toolbar.Width - 138, 9) };
        copy.Click += (_, _) => _vm.CopyCommand.Execute(null);
        toolbar.Controls.Add(clear);
        toolbar.Controls.Add(copy);

        _list = new ListBox
        {
            Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, BackColor = Theme.WindowBg,
            ForeColor = Theme.TextPrimary, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 46,
            IntegralHeight = false
        };
        _list.DrawItem += OnDrawItem;

        var footer = new Panel { Dock = DockStyle.Bottom, Height = 30, BackColor = Theme.FromHex("#1E1E1E") };
        _count = new Label { Text = "0 events", ForeColor = Theme.TextFaint, Font = Theme.F(9f), Location = new Point(12, 7), AutoSize = true };
        footer.Controls.Add(_count);

        Controls.Add(_list);
        Controls.Add(toolbar);
        Controls.Add(footer);

        _vm.PropertyChanged += OnVmChanged;
        _vm.Entries.CollectionChanged += OnEntriesChanged;
        Reload();
    }

    /// The log gains one entry at a time and trims one from the front when full, so
    /// apply just that change instead of rebuilding up to 500 rows per log line.
    private void OnEntriesChanged(object? s, NotifyCollectionChangedEventArgs e)
    {
        if (IsDisposed) return;

        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add when e.NewItems is not null:
                foreach (DiagnosticEntry entry in e.NewItems)
                    if (_vm.Matches(entry)) _list.Items.Add(entry);
                if (_list.Items.Count > 0) _list.TopIndex = _list.Items.Count - 1;
                break;

            case NotifyCollectionChangedAction.Remove when e.OldItems is not null:
                foreach (DiagnosticEntry entry in e.OldItems)
                    _list.Items.Remove(entry);
                break;

            default:   // cleared, or anything unusual
                Reload();
                return;
        }
        _count.Text = $"{_vm.Entries.Count} events";
    }

    private void ApplyFilter(string filter)
    {
        _vm.FilterCommand.Execute(filter);
        foreach (var (name, btn) in _filterButtons)
        {
            bool active = name == filter;
            btn.Fill = active ? Theme.Accent : Theme.Surface2;
            btn.TextColor = active ? Color.Black : Theme.TextSecondary;
            btn.Invalidate();
        }
    }

    private void OnVmChanged(object? s, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DiagnosticViewModel.FilteredEntries)) Reload();
    }

    private void Reload()
    {
        if (IsDisposed) return;
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var entry in _vm.FilteredEntries)
            _list.Items.Add(entry);
        _list.EndUpdate();
        _count.Text = $"{_vm.Entries.Count} events";
        if (_list.Items.Count > 0) _list.TopIndex = _list.Items.Count - 1;
    }

    private void OnDrawItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _list.Items.Count) return;
        var entry = (DiagnosticEntry)_list.Items[e.Index];
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using (var bg = new SolidBrush(Theme.WindowBg)) g.FillRectangle(bg, e.Bounds);

        TextRenderer.DrawText(g, entry.Timestamp.ToString("HH:mm:ss"), TimestampFont,
            new Rectangle(e.Bounds.X + 10, e.Bounds.Y + 6, 56, 14), Theme.TextFaint, TextFormatFlags.Left);

        var dot = entry.Level switch
        {
            DiagnosticLevel.Success => Theme.Accent,
            DiagnosticLevel.Warning => Theme.Warn,
            DiagnosticLevel.Error   => Theme.Error,
            _                       => Theme.TextMuted
        };
        using (var b = new SolidBrush(dot)) g.FillEllipse(b, e.Bounds.X + 72, e.Bounds.Y + 8, 7, 7);

        int tx = e.Bounds.X + 88;
        int tw = e.Bounds.Width - 88 - 12;
        TextRenderer.DrawText(g, entry.Message, Theme.F(9.5f),
            new Rectangle(tx, e.Bounds.Y + 5, tw, 16), Theme.FromHex("#E0E0E0"),
            TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        if (!string.IsNullOrEmpty(entry.Detail))
            TextRenderer.DrawText(g, entry.Detail, Theme.F(8.5f),
                new Rectangle(tx, e.Bounds.Y + 24, tw, 16), Theme.TextMuted,
                TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // The log lives as long as the app — detach, or every Diagnostics window
            // ever opened would stay in memory and keep being updated.
            _vm.PropertyChanged -= OnVmChanged;
            _vm.Entries.CollectionChanged -= OnEntriesChanged;
        }
        base.Dispose(disposing);
    }
}
