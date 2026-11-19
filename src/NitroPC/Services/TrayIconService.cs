using NitroPC.Interop;
using Drawing = System.Drawing;
using WinForms = System.Windows.Forms;

namespace NitroPC.Services;

public sealed class TrayIconService : IDisposable
{
    private readonly WinForms.NotifyIcon _notifyIcon = new();
    private readonly WinForms.ToolStripMenuItem _stopItem;
    private readonly WinForms.ToolStripMenuItem _quietOffItem;
    private readonly WinForms.ToolStripMenuItem _quietAutoItem;
    private readonly WinForms.ToolStripMenuItem _quietAlwaysItem;
    private readonly WinForms.ToolStripMenuItem _startupItem;
    private bool _disposed;

    public TrayIconService(bool startupEnabled)
    {
        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add(Item("Open NitroPC Toolbox", () => OpenRequested?.Invoke(), bold: true));
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(Item("Keep Awake — 1h", () => StartKeepAwakeRequested?.Invoke(TimeSpan.FromHours(1))));
        menu.Items.Add(Item("Keep Awake — 2h", () => StartKeepAwakeRequested?.Invoke(TimeSpan.FromHours(2))));
        menu.Items.Add(Item("Keep Awake — 4h", () => StartKeepAwakeRequested?.Invoke(TimeSpan.FromHours(4))));
        menu.Items.Add(Item("Keep Awake — 8h", () => StartKeepAwakeRequested?.Invoke(TimeSpan.FromHours(8))));
        menu.Items.Add(Item("Keep Awake — Infinite (∞)", () => StartKeepAwakeRequested?.Invoke(null)));
        _stopItem = Item("Stop Keep Awake", () => StopKeepAwakeRequested?.Invoke());
        menu.Items.Add(_stopItem);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        _quietOffItem = Item("Quiet Mode — Off", () => QuietModeChangedRequested?.Invoke(Models.QuietModeLevel.Off));
        _quietAutoItem = Item("Quiet Mode — Auto (by CPU temp)", () => QuietModeChangedRequested?.Invoke(Models.QuietModeLevel.Auto));
        _quietAlwaysItem = Item("Quiet Mode — Always Quiet", () => QuietModeChangedRequested?.Invoke(Models.QuietModeLevel.Quiet));
        menu.Items.Add(_quietOffItem);
        menu.Items.Add(_quietAutoItem);
        menu.Items.Add(_quietAlwaysItem);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        _startupItem = Item("Start with Windows", () =>
        {
            var enabled = _startupItem is not { Checked: true };
            StartupChangedRequested?.Invoke(enabled);
        });
        _startupItem.CheckOnClick = false;
        _startupItem.Checked = startupEnabled;
        menu.Items.Add(_startupItem);
        menu.Items.Add(Item("Check for Updates…", () => CheckForUpdatesRequested?.Invoke()));
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(Item("Exit NitroPC", () => ExitRequested?.Invoke()));

        _notifyIcon.ContextMenuStrip = menu;
        _notifyIcon.Text = "NitroPC";
        _notifyIcon.MouseClick += NotifyIconOnMouseClick;
        _notifyIcon.Icon = CreateIcon(awake: false);
        _notifyIcon.Visible = true;
    }

    public event Action? OpenRequested;

    public event Action<TimeSpan?>? StartKeepAwakeRequested;

    public event Action? StopKeepAwakeRequested;

    public event Action<Models.QuietModeLevel>? QuietModeChangedRequested;

    public event Action<bool>? StartupChangedRequested;

    public event Action? CheckForUpdatesRequested;

    public event Action? ExitRequested;

    public void UpdateKeepAwake(bool awake, TimeSpan? remaining)
    {
        var oldIcon = _notifyIcon.Icon;
        _notifyIcon.Icon = CreateIcon(awake);
        oldIcon?.Dispose();
        _stopItem.Enabled = awake;
        _notifyIcon.Text = awake
            ? remaining is { } value
                ? $"NitroPC — Awake, {Math.Ceiling(value.TotalMinutes):0}m remaining"
                : "NitroPC — Awake indefinitely"
            : "NitroPC — Idle";
    }

    public void SetStartupEnabled(bool enabled)
    {
        _startupItem.Checked = enabled;
    }

    public void SetQuietMode(Models.QuietModeLevel mode)
    {
        _quietOffItem.Checked = mode == Models.QuietModeLevel.Off;
        _quietAutoItem.Checked = mode == Models.QuietModeLevel.Auto;
        _quietAlwaysItem.Checked = mode == Models.QuietModeLevel.Quiet;
    }

    public void ShowContextMenu()
    {
        if (_notifyIcon.ContextMenuStrip is { } menu)
        {
            menu.Show(WinForms.Cursor.Position);
        }
    }

    private void NotifyIconOnMouseClick(object? sender, WinForms.MouseEventArgs e)
    {
        if (e.Button == WinForms.MouseButtons.Left)
        {
            OpenRequested?.Invoke();
        }
    }

    private static WinForms.ToolStripMenuItem Item(string text, Action action, bool bold = false)
    {
        var item = new WinForms.ToolStripMenuItem(text)
        {
            Font = new Drawing.Font(
                WinForms.SystemInformation.MenuFont,
                bold ? Drawing.FontStyle.Bold : Drawing.FontStyle.Regular)
        };
        item.Click += (_, _) => action();
        return item;
    }

    private static Drawing.Icon CreateIcon(bool awake)
    {
        using var bitmap = new Drawing.Bitmap(32, 32);
        using var graphics = Drawing.Graphics.FromImage(bitmap);
        graphics.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var background = new Drawing.SolidBrush(Drawing.Color.FromArgb(255, 35, 56, 64));
        using var border = new Drawing.Pen(Drawing.Color.FromArgb(90, 110, 198, 255), 1);
        graphics.FillEllipse(background, 1, 1, 30, 30);
        graphics.DrawEllipse(border, 1, 1, 30, 30);
        using var nBrush = new Drawing.SolidBrush(Drawing.Color.FromArgb(255, 243, 166, 131));
        using var font = new Drawing.Font(Drawing.FontFamily.GenericSansSerif, 15, Drawing.FontStyle.Bold);
        var format = Drawing.StringFormat.GenericTypographic;
        var textSize = graphics.MeasureString("N", font, new Drawing.PointF(0, 0), format);
        graphics.DrawString(
            "N",
            font,
            nBrush,
            (32 - textSize.Width) / 2,
            (32 - textSize.Height) / 2,
            format);

        using var dot = new Drawing.SolidBrush(
            awake
                ? Drawing.Color.FromArgb(255, 251, 191, 36)
                : Drawing.Color.FromArgb(255, 126, 150, 159));
        graphics.FillEllipse(dot, 22, 21, 7, 7);

        var handle = bitmap.GetHicon();
        try
        {
            return (Drawing.Icon)Drawing.Icon.FromHandle(handle).Clone();
        }
        finally
        {
            NativeMethods.DestroyIcon(handle);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _disposed = true;
    }
}
