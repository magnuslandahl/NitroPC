using NitroPC.Interop;
using NitroPC.Models;
using Drawing = System.Drawing;
using Drawing2D = System.Drawing.Drawing2D;
using WinForms = System.Windows.Forms;

namespace NitroPC.Services;

public sealed class TrayIconService : IDisposable
{
    private const int MaxNotifyIconTextLength = 63;

    private readonly WinForms.NotifyIcon _notifyIcon = new();
    private readonly WinForms.ToolStripMenuItem _stopItem;
    private readonly WinForms.ToolStripMenuItem _quietOffItem;
    private readonly WinForms.ToolStripMenuItem _quietAutoItem;
    private readonly WinForms.ToolStripMenuItem _quietAlwaysItem;
    private readonly WinForms.ToolStripMenuItem _startupItem;
    private MetricsSnapshot? _latestMetrics;
    private bool _isAwake;
    private TimeSpan? _awakeRemaining;
    private bool _isBoostDisabled;
    private bool? _renderedAwake;
    private bool? _renderedBoostDisabled;
    private bool _disposed;

    public TrayIconService(bool startupEnabled)
    {
        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add(Item("Open NitroPC Toolbox", () => OpenRequested?.Invoke(WinForms.Cursor.Position), bold: true));
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(Item("Keep Awake — 1h", () => StartKeepAwakeRequested?.Invoke(TimeSpan.FromHours(1))));
        menu.Items.Add(Item("Keep Awake — 2h", () => StartKeepAwakeRequested?.Invoke(TimeSpan.FromHours(2))));
        menu.Items.Add(Item("Keep Awake — 4h", () => StartKeepAwakeRequested?.Invoke(TimeSpan.FromHours(4))));
        menu.Items.Add(Item("Keep Awake — 8h", () => StartKeepAwakeRequested?.Invoke(TimeSpan.FromHours(8))));
        menu.Items.Add(Item("Keep Awake — Infinite (∞)", () => StartKeepAwakeRequested?.Invoke(null)));
        _stopItem = Item("Stop Keep Awake", () => StopKeepAwakeRequested?.Invoke());
        menu.Items.Add(_stopItem);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        _quietOffItem = Item("Quiet Mode — Off", () => QuietModeChangedRequested?.Invoke(QuietModeLevel.Off));
        _quietAutoItem = Item("Quiet Mode — Auto (by CPU temp)", () => QuietModeChangedRequested?.Invoke(QuietModeLevel.Auto));
        _quietAlwaysItem = Item("Quiet Mode — Always Quiet", () => QuietModeChangedRequested?.Invoke(QuietModeLevel.Quiet));
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
        _notifyIcon.Text = "NitroPC — Click to open toolbox";
        _notifyIcon.MouseClick += NotifyIconOnMouseClick;
        _notifyIcon.MouseDoubleClick += NotifyIconOnMouseDoubleClick;
        RefreshIconIfNeeded();
        _notifyIcon.Visible = true;
    }

    public event Action<Drawing.Point>? OpenRequested;

    public event Action<TimeSpan?>? StartKeepAwakeRequested;

    public event Action? StopKeepAwakeRequested;

    public event Action<QuietModeLevel>? QuietModeChangedRequested;

    public event Action<bool>? StartupChangedRequested;

    public event Action? CheckForUpdatesRequested;

    public event Action? ExitRequested;

    public void UpdateKeepAwake(bool awake, TimeSpan? remaining)
    {
        _isAwake = awake;
        _awakeRemaining = remaining;
        _stopItem.Enabled = awake;
        RefreshIconIfNeeded();
        RefreshTooltipText();
    }

    public void UpdateMetrics(MetricsSnapshot metrics)
    {
        _latestMetrics = metrics;
        RefreshTooltipText();
    }

    public void SetStartupEnabled(bool enabled)
    {
        _startupItem.Checked = enabled;
    }

    public void SetQuietMode(QuietModeLevel mode, bool boostDisabled)
    {
        _isBoostDisabled = boostDisabled;
        _quietOffItem.Checked = mode == QuietModeLevel.Off;
        _quietAutoItem.Checked = mode == QuietModeLevel.Auto;
        _quietAlwaysItem.Checked = mode == QuietModeLevel.Quiet;
        RefreshIconIfNeeded();
        RefreshTooltipText();
    }

    public void ShowContextMenu()
    {
        if (_notifyIcon.ContextMenuStrip is { } menu)
        {
            NativeMethods.SetForegroundWindow(menu.Handle);
            menu.Show(WinForms.Cursor.Position);
        }
    }

    private void NotifyIconOnMouseClick(object? sender, WinForms.MouseEventArgs e)
    {
        if (e.Button == WinForms.MouseButtons.Left)
        {
            OpenRequested?.Invoke(WinForms.Cursor.Position);
        }
    }

    private void NotifyIconOnMouseDoubleClick(object? sender, WinForms.MouseEventArgs e)
    {
        if (e.Button == WinForms.MouseButtons.Left)
        {
            OpenRequested?.Invoke(WinForms.Cursor.Position);
        }
    }

    private void RefreshIconIfNeeded()
    {
        if (_renderedAwake == _isAwake && _renderedBoostDisabled == _isBoostDisabled)
        {
            return;
        }

        var oldIcon = _notifyIcon.Icon;
        _notifyIcon.Icon = CreateIcon(_isAwake, _isBoostDisabled);
        _renderedAwake = _isAwake;
        _renderedBoostDisabled = _isBoostDisabled;
        oldIcon?.Dispose();
    }

    private void RefreshTooltipText()
    {
        var text = FormatTooltipText(_latestMetrics, _isAwake, _awakeRemaining, _isBoostDisabled);
        if (!string.Equals(_notifyIcon.Text, text, StringComparison.Ordinal))
        {
            _notifyIcon.Text = text;
        }
    }

    internal static string FormatTooltipText(
        MetricsSnapshot? metrics,
        bool awake,
        TimeSpan? remaining,
        bool boostDisabled)
    {
        string text;
        if (metrics is null)
        {
            text = awake
                ? remaining is { } value
                    ? $"NitroPC · Awake ({FormatShortRemaining(value)})"
                    : "NitroPC · Awake (∞)"
                : "NitroPC — Click to open toolbox";
        }
        else
        {
            var cpuPart = metrics.CpuTemperatureCelsius is { } tempC
                ? $"CPU {Math.Round(metrics.CpuTotalPercent):0}% {Math.Round(tempC):0}°C"
                : $"CPU {Math.Round(metrics.CpuTotalPercent):0}%";
            var ramPart = $"RAM {Math.Round(metrics.RamPercent):0}%";
            var statusPart = awake
                ? remaining is { } value
                    ? $" · Awake {FormatShortRemaining(value)}"
                    : " · Awake ∞"
                : boostDisabled
                    ? " · Quiet"
                    : string.Empty;

            text = $"NitroPC · {cpuPart} · {ramPart}{statusPart}";
        }

        return text.Length <= MaxNotifyIconTextLength
            ? text
            : text[..MaxNotifyIconTextLength];
    }

    private static string FormatShortRemaining(TimeSpan remaining)
    {
        if (remaining.TotalMinutes <= 90)
        {
            return $"{Math.Ceiling(remaining.TotalMinutes):0}m";
        }

        return $"{remaining.Hours}h {remaining.Minutes}m";
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

    private static Drawing.Icon CreateIcon(bool awake, bool boostDisabled)
    {
        using var bitmap = new Drawing.Bitmap(32, 32);
        using var graphics = Drawing.Graphics.FromImage(bitmap);
        graphics.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = Drawing2D.PixelOffsetMode.HighQuality;
        graphics.TextRenderingHint = Drawing.Text.TextRenderingHint.AntiAliasGridFit;

        var badgeRect = new Drawing.RectangleF(2f, 2f, 28f, 28f);
        using var badgePath = CreateRoundedRectPath(badgeRect, 7f);
        using var gradient = new Drawing2D.LinearGradientBrush(
            new Drawing.PointF(2f, 2f),
            new Drawing.PointF(30f, 30f),
            Drawing.Color.FromArgb(255, 243, 166, 131),
            Drawing.Color.FromArgb(255, 110, 198, 255));
        graphics.FillPath(gradient, badgePath);

        using var border = new Drawing.Pen(Drawing.Color.FromArgb(70, 255, 255, 255), 1.1f);
        graphics.DrawPath(border, badgePath);

        using var glyphBrush = new Drawing.SolidBrush(Drawing.Color.FromArgb(255, 14, 25, 32));
        using var font = new Drawing.Font("Segoe UI", 16f, Drawing.FontStyle.Bold, Drawing.GraphicsUnit.Pixel);
        var format = new Drawing.StringFormat(Drawing.StringFormat.GenericTypographic)
        {
            Alignment = Drawing.StringAlignment.Center,
            LineAlignment = Drawing.StringAlignment.Center
        };
        graphics.DrawString("N", font, glyphBrush, new Drawing.RectangleF(2f, 2.5f, 28f, 28f), format);

        if (awake || boostDisabled)
        {
            var dotColor = awake
                ? Drawing.Color.FromArgb(255, 251, 191, 36)
                : Drawing.Color.FromArgb(255, 110, 198, 255);
            using var dotHalo = new Drawing.SolidBrush(Drawing.Color.FromArgb(255, 16, 26, 32));
            using var dotFill = new Drawing.SolidBrush(dotColor);
            graphics.FillEllipse(dotHalo, 19.5f, 19.5f, 11.5f, 11.5f);
            graphics.FillEllipse(dotFill, 21.2f, 21.2f, 8.1f, 8.1f);
        }

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

    private static Drawing2D.GraphicsPath CreateRoundedRectPath(Drawing.RectangleF rect, float radius)
    {
        var diameter = radius * 2f;
        var path = new Drawing2D.GraphicsPath();
        path.AddArc(rect.Left, rect.Top, diameter, diameter, 180f, 90f);
        path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270f, 90f);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0f, 90f);
        path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90f, 90f);
        path.CloseFigure();
        return path;
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
