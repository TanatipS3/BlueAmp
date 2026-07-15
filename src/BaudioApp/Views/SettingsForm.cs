using System.Drawing;
using System.Windows.Forms;
using BaudioApp.Services;
using BaudioApp.ViewModels;

namespace BaudioApp.Views;

internal sealed class SettingsForm : Form
{
    private const string NoEarbudLabel = "(None)";

    private readonly SettingsViewModel _vm;
    private readonly Action _onSaved;

    private DarkComboBox? _earbudCombo;
    private IReadOnlyList<EarbudDeviceOption> _earbudOptions = [];

    public SettingsForm(SettingsViewModel vm, Action onSaved)
    {
        _vm = vm;
        _onSaved = onSaved;

        Text            = "Settings — BlueAmp";
        ClientSize      = new Size(460, 520);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox     = false;
        MinimizeBox     = false;
        StartPosition   = FormStartPosition.CenterScreen;
        ShowInTaskbar   = false;
        BackColor       = Theme.WindowBg;
        Font            = Theme.F(9f);

        BuildUi();
    }

    private void BuildUi()
    {
        var content = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(20, 16, 20, 16),
            BackColor = Theme.WindowBg
        };

        content.Controls.Add(SectionLabel("GENERAL"));
        content.Controls.Add(Row("Launch at startup",
            "Start BlueAmp automatically when Windows starts",
            _vm.LaunchAtStartup, v => _vm.LaunchAtStartup = v));
        content.Controls.Add(Row("Remember last device",
            "Auto-connect to the last used Bluetooth device on launch",
            _vm.RememberLastDevice, v => _vm.RememberLastDevice = v));
        content.Controls.Add(Row("Auto-reconnect on drop",
            "Retry connection automatically if the audio stream drops",
            _vm.AutoReconnect, v => _vm.AutoReconnect = v));

        content.Controls.Add(SectionLabel("DIAGNOSTICS"));
        content.Controls.Add(Row("Enable diagnostic logging",
            "Off by default to stay lightweight. Errors are always recorded; turn this on to capture full activity.",
            _vm.KeepLogHistory, v => _vm.KeepLogHistory = v));

        content.Controls.Add(SectionLabel("PRESENCE DETECTION"));
        content.Controls.Add(Row("Lock PC when phone leaves",
            "Use Bluetooth signal strength to detect when you've walked away. May increase CPU usage.",
            _vm.PresenceDetection, v => _vm.PresenceDetection = v));

        content.Controls.Add(SectionLabel("EARBUD HANDOFF"));
        _earbudCombo = new DarkComboBox();
        content.Controls.Add(ComboRow("Earbuds",
            "The Bluetooth earbuds or headphones to hand off between this PC and your phone.",
            _earbudCombo));
        content.Controls.Add(Row("Single-point earbuds",
            "Turn on if your earbuds connect to only one device at a time. BlueAmp will then disconnect them from the PC before the phone claims them. Leave off for multipoint earbuds.",
            _vm.SinglePointEarbuds, v => _vm.SinglePointEarbuds = v));
        content.Controls.Add(Row("Automatic handoff",
            "Switch earbuds to the phone automatically when you walk out of range, and back to the PC when you return.",
            _vm.AutoHandoff, v => _vm.AutoHandoff = v));

        var footer = new Panel { Dock = DockStyle.Bottom, Height = 52, BackColor = Theme.FromHex("#1E1E1E") };
        var save = new DarkButton
        {
            Text = "Save", Fill = Theme.Accent, TextColor = Color.Black,
            Size = new Size(90, 30), Font = Theme.F(9.5f, FontStyle.Bold),
            Location = new Point(footer.Width - 110, 11), Anchor = AnchorStyles.Right | AnchorStyles.Top
        };
        save.Click += (_, _) => { _vm.SaveCommand.Execute(null); _onSaved(); Close(); };
        footer.Controls.Add(save);

        Controls.Add(content);
        Controls.Add(footer);

        // Rows grow with their wrapped descriptions, so size the window to the
        // content instead of a fixed height (capped so it can't outgrow the screen).
        int wanted = content.GetPreferredSize(new Size(ClientSize.Width, 0)).Height + footer.Height;
        ClientSize = new Size(ClientSize.Width, Math.Min(wanted, 680));

        _ = PopulateEarbudsAsync();
    }

    /// Enumerate paired audio devices and fill the earbud picker. Runs after the UI
    /// is built; the enumeration is async and resumes on the UI thread.
    private async Task PopulateEarbudsAsync()
    {
        if (_earbudCombo is null) return;

        _earbudCombo.Items.Add(NoEarbudLabel);

        try { _earbudOptions = await _vm.LoadEarbudDevicesAsync(); }
        catch { _earbudOptions = []; }

        if (_earbudCombo.IsDisposed) return;
        foreach (var d in _earbudOptions) _earbudCombo.Items.Add(d.Name);

        // Preselect the saved device if it's still paired; otherwise show "(None)".
        int index = 0;
        var savedId = _vm.SelectedEarbudId;
        if (!string.IsNullOrEmpty(savedId))
        {
            for (int i = 0; i < _earbudOptions.Count; i++)
                if (_earbudOptions[i].Id == savedId) { index = i + 1; break; }
        }
        _earbudCombo.SelectedIndex = index;

        // Attach after the initial selection so it doesn't fire spuriously.
        _earbudCombo.SelectedIndexChanged += OnEarbudSelectionChanged;
    }

    private void OnEarbudSelectionChanged(object? sender, EventArgs e)
    {
        int idx = _earbudCombo!.SelectedIndex;
        var option = (idx <= 0 || idx - 1 >= _earbudOptions.Count) ? null : _earbudOptions[idx - 1];
        _vm.SetEarbud(option);
    }

    private static Label SectionLabel(string text) => new()
    {
        Text = text, AutoSize = true, ForeColor = Theme.TextFaint,
        Font = new Font(Theme.FontFamily, 8f, FontStyle.Bold),
        Margin = new Padding(2, 10, 0, 6)
    };

    private Control Row(string title, string desc, bool initial, Action<bool> setter)
    {
        const int panelW = 400;
        const int leftPad = 14;
        const int descW   = 318;   // ends at x=332, leaving a clear gap before the toggle (x=346)
        const int descY   = 30;

        // Measure the wrapped description so the row grows to fit two lines instead
        // of clipping the second line (and colliding with the toggle).
        var descFont = Theme.F(8.5f);
        int descH = TextRenderer.MeasureText(desc, descFont,
            new Size(descW, 0), TextFormatFlags.WordBreak).Height;
        int panelH = descY + descH + 10;

        var panel = new Panel
        {
            Width = panelW, Height = panelH, BackColor = Theme.FromHex("#232323"),
            Margin = new Padding(0, 0, 0, 4)
        };

        panel.Controls.Add(new Label
        {
            Text = title, ForeColor = Theme.TextPrimary, Font = Theme.F(10f),
            Location = new Point(leftPad, 9), AutoSize = true
        });
        panel.Controls.Add(new Label
        {
            Text = desc, ForeColor = Theme.TextSecondary, Font = descFont,
            Location = new Point(leftPad, descY), Size = new Size(descW, descH)
        });

        var toggle = new ToggleSwitch
        {
            Checked = initial, BackColor = panel.BackColor,
            Location = new Point(panelW - 54, (panelH - 22) / 2),
            Anchor = AnchorStyles.Right | AnchorStyles.Top
        };
        toggle.CheckedChanged += (_, _) => setter(toggle.Checked);
        panel.Controls.Add(toggle);

        return panel;
    }

    /// A settings row whose control is a full-width drop-down beneath the description
    /// (rather than a right-aligned toggle).
    private Control ComboRow(string title, string desc, DarkComboBox combo)
    {
        const int panelW = 400;
        const int leftPad = 14;
        const int descW = 372;
        const int descY = 30;
        const int comboH = 26;

        var descFont = Theme.F(8.5f);
        int descH = TextRenderer.MeasureText(desc, descFont,
            new Size(descW, 0), TextFormatFlags.WordBreak).Height;
        int comboY = descY + descH + 8;
        int panelH = comboY + comboH + 10;

        var panel = new Panel
        {
            Width = panelW, Height = panelH, BackColor = Theme.FromHex("#232323"),
            Margin = new Padding(0, 0, 0, 4)
        };

        panel.Controls.Add(new Label
        {
            Text = title, ForeColor = Theme.TextPrimary, Font = Theme.F(10f),
            Location = new Point(leftPad, 9), AutoSize = true
        });
        panel.Controls.Add(new Label
        {
            Text = desc, ForeColor = Theme.TextSecondary, Font = descFont,
            Location = new Point(leftPad, descY), Size = new Size(descW, descH)
        });

        combo.Location = new Point(leftPad, comboY);
        combo.Width = panelW - 2 * leftPad;
        panel.Controls.Add(combo);

        return panel;
    }
}
