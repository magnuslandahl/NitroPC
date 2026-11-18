using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace NitroPC.Services;

internal interface IKeepAwakePlatform
{
    void EnableExecutionState();

    void SendActivityPulse();

    void ClearExecutionState();
}

internal sealed class KeepAwakePlatform : IKeepAwakePlatform
{
    public void EnableExecutionState()
    {
        var state = Interop.NativeMethods.EXECUTION_STATE.ES_CONTINUOUS
            | Interop.NativeMethods.EXECUTION_STATE.ES_SYSTEM_REQUIRED
            | Interop.NativeMethods.EXECUTION_STATE.ES_DISPLAY_REQUIRED;
        if (Interop.NativeMethods.SetThreadExecutionState(state) == 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Unable to enable Keep Awake.");
        }
    }

    public void SendActivityPulse()
    {
        var inputs = new[]
        {
            new Interop.NativeMethods.INPUT
            {
                type = Interop.NativeMethods.INPUT_KEYBOARD,
                U = new Interop.NativeMethods.InputUnion
                {
                    ki = new Interop.NativeMethods.KEYBDINPUT
                    {
                        wVk = Interop.NativeMethods.VK_F15
                    }
                }
            },
            new Interop.NativeMethods.INPUT
            {
                type = Interop.NativeMethods.INPUT_KEYBOARD,
                U = new Interop.NativeMethods.InputUnion
                {
                    ki = new Interop.NativeMethods.KEYBDINPUT
                    {
                        wVk = Interop.NativeMethods.VK_F15,
                        dwFlags = Interop.NativeMethods.KEYEVENTF_KEYUP
                    }
                }
            }
        };

        if (Interop.NativeMethods.SendInput(
                (uint)inputs.Length,
                inputs,
                Marshal.SizeOf<Interop.NativeMethods.INPUT>()) != inputs.Length)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Unable to send the Keep Awake pulse.");
        }
    }

    public void ClearExecutionState()
    {
        if (Interop.NativeMethods.SetThreadExecutionState(
                Interop.NativeMethods.EXECUTION_STATE.ES_CONTINUOUS) == 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Unable to clear Keep Awake.");
        }
    }
}

public sealed class KeepAwakeService : IDisposable
{
    private readonly IKeepAwakePlatform _platform;
    private readonly Func<DateTime> _utcNow;
    private readonly DispatcherTimer _pulseTimer;
    private readonly DispatcherTimer _tickTimer;
    private DateTime? _endTimeUtc;
    private TimeSpan? _totalDuration;
    private bool _isAwake;
    private bool _disposed;

    public KeepAwakeService()
        : this(new KeepAwakePlatform(), () => DateTime.UtcNow)
    {
    }

    internal KeepAwakeService(IKeepAwakePlatform platform, Func<DateTime> utcNow)
    {
        _platform = platform;
        _utcNow = utcNow;
        _pulseTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _tickTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _pulseTimer.Tick += PulseTimerOnTick;
        _tickTimer.Tick += TickTimerOnTick;
    }

    public bool IsAwake => _isAwake;

    public bool IsIndefinite => _isAwake && _endTimeUtc is null;

    public TimeSpan? Remaining
    {
        get
        {
            if (_endTimeUtc is null)
            {
                return null;
            }

            return TimeSpan.FromTicks(Math.Max(0, (_endTimeUtc.Value - _utcNow()).Ticks));
        }
    }

    public TimeSpan? TotalDuration => _totalDuration;

    public event Action? StateChanged;

    public event Action<Exception>? OperationFailed;

    public void Start(TimeSpan? duration)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_isAwake)
        {
            Stop();
        }

        TimeSpan? activeDuration = duration is { } value && value > TimeSpan.Zero ? value : null;
        _platform.EnableExecutionState();
        try
        {
            _platform.SendActivityPulse();
        }
        catch (Win32Exception)
        {
            _platform.ClearExecutionState();
            throw;
        }

        _totalDuration = activeDuration;
        _endTimeUtc = activeDuration is null ? null : _utcNow().Add(activeDuration.Value);
        _isAwake = true;
        _pulseTimer.Start();
        _tickTimer.Start();
        StateChanged?.Invoke();
    }

    public void Stop()
    {
        _pulseTimer.Stop();
        _tickTimer.Stop();

        var wasAwake = _isAwake;
        _isAwake = false;
        _endTimeUtc = null;
        _totalDuration = null;
        _platform.ClearExecutionState();

        if (wasAwake)
        {
            StateChanged?.Invoke();
        }
    }

    public void Toggle(int defaultMinutes)
    {
        if (_isAwake)
        {
            Stop();
        }
        else
        {
            Start(defaultMinutes <= 0 ? null : TimeSpan.FromMinutes(defaultMinutes));
        }
    }

    internal void EvaluateExpiration()
    {
        if (!_isAwake || _endTimeUtc is null)
        {
            return;
        }

        if (_utcNow() >= _endTimeUtc.Value)
        {
            Stop();
        }
        else
        {
            StateChanged?.Invoke();
        }
    }

    private void PulseTimerOnTick(object? sender, EventArgs e)
    {
        try
        {
            _platform.SendActivityPulse();
        }
        catch (Win32Exception exception)
        {
            Stop();
            OperationFailed?.Invoke(exception);
        }
    }

    private void TickTimerOnTick(object? sender, EventArgs e)
    {
        EvaluateExpiration();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            Stop();
        }
        finally
        {
            _pulseTimer.Tick -= PulseTimerOnTick;
            _tickTimer.Tick -= TickTimerOnTick;
            _disposed = true;
        }
    }
}
