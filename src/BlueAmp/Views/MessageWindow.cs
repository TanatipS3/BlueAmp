using System.Drawing;
using System.Windows.Forms;

namespace BlueAmp.Views;

/// A hidden top-level window. Two jobs: it is the handle the Ui helper marshals
/// onto, and it receives WM_POWERBROADCAST so we can reconnect after sleep/wake.
/// (Must be a real top-level window — message-only windows don't get broadcasts.)
internal sealed class MessageWindow : Form
{
    private const int WM_POWERBROADCAST       = 0x0218;
    private const int PBT_APMRESUMEAUTOMATIC  = 0x0012;
    private const int PBT_APMRESUMESUSPEND    = 0x0007;

    public event Action? Resumed;

    public MessageWindow()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar   = false;
        StartPosition   = FormStartPosition.Manual;
        Location        = new Point(-32000, -32000);
        Size            = new Size(1, 1);
        _ = Handle; // force handle creation so it can marshal + receive broadcasts
    }

    // Never actually display this window.
    protected override void SetVisibleCore(bool value) => base.SetVisibleCore(false);

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_POWERBROADCAST)
        {
            int ev = m.WParam.ToInt32();
            if (ev == PBT_APMRESUMEAUTOMATIC || ev == PBT_APMRESUMESUSPEND)
                Resumed?.Invoke();
        }
        base.WndProc(ref m);
    }
}
