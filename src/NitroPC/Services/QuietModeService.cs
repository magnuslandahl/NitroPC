using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using NitroPC.Interop;
using NitroPC.Models;

namespace NitroPC.Services;

internal interface IQuietModePlatform
{
    QuietModeRecoveryState CaptureActiveBoostState();

    void ApplyBoostState(QuietModeRecoveryState state);
}

internal sealed class WindowsQuietModePlatform : IQuietModePlatform
{
    private static readonly Guid ProcessorPowerGroup =
        new("54533251-82be-4824-96c1-47b60b740d00");
    private static readonly Guid ProcessorBoostMode =
        new("be337238-0d82-4146-a960-4f3749d470c7");

    public QuietModeRecoveryState CaptureActiveBoostState()
    {
        var schemeId = ReadActiveScheme();
        var processorGroup = ProcessorPowerGroup;
        var boostMode = ProcessorBoostMode;
        var acStatus = NativeMethods.PowerReadACValueIndex(
            IntPtr.Zero,
            ref schemeId,
            ref processorGroup,
            ref boostMode,
            out var acValue);
        ThrowIfFailed(acStatus, "Could not read the AC processor boost policy.");

        var dcStatus = NativeMethods.PowerReadDCValueIndex(
            IntPtr.Zero,
            ref schemeId,
            ref processorGroup,
            ref boostMode,
            out var dcValue);
        ThrowIfFailed(dcStatus, "Could not read the battery processor boost policy.");

        return new QuietModeRecoveryState(schemeId, acValue, dcValue);
    }

    public void ApplyBoostState(QuietModeRecoveryState state)
    {
        var schemeId = state.SchemeId;
        var processorGroup = ProcessorPowerGroup;
        var boostMode = ProcessorBoostMode;
        var acStatus = NativeMethods.PowerWriteACValueIndex(
            IntPtr.Zero,
            ref schemeId,
            ref processorGroup,
            ref boostMode,
            state.AcProcessorBoostMode);
        ThrowIfFailed(acStatus, "Could not update the AC processor boost policy.");

        var dcStatus = NativeMethods.PowerWriteDCValueIndex(
            IntPtr.Zero,
            ref schemeId,
            ref processorGroup,
            ref boostMode,
            state.DcProcessorBoostMode);
        ThrowIfFailed(dcStatus, "Could not update the battery processor boost policy.");

        if (ReadActiveScheme() == schemeId)
        {
            var applyStatus = NativeMethods.PowerSetActiveScheme(IntPtr.Zero, ref schemeId);
            ThrowIfFailed(applyStatus, "Could not apply the processor boost policy.");
        }
    }

    private static Guid ReadActiveScheme()
    {
        var status = NativeMethods.PowerGetActiveScheme(IntPtr.Zero, out var pointer);
        ThrowIfFailed(status, "Could not read the active Windows power plan.");
        if (pointer == IntPtr.Zero)
        {
            throw new InvalidOperationException("Windows returned an empty active power plan.");
        }

        try
        {
            return Marshal.PtrToStructure<Guid>(pointer);
        }
        finally
        {
            NativeMethods.LocalFree(pointer);
        }
    }

    private static void ThrowIfFailed(uint status, string message)
    {
        if (status != 0)
        {
            throw new Win32Exception(unchecked((int)status), message);
        }
    }
}

public sealed class QuietModeService : IDisposable
{
    public static readonly TimeSpan AutoMinimumHoldDuration = TimeSpan.FromSeconds(15);
    public const int HysteresisBandCelsius = 10;

    private readonly IQuietModePlatform _platform;
    private readonly Action<QuietModeRecoveryState?> _persistRecoveryState;
    private readonly Func<DateTime> _utcNow;
    private QuietModeRecoveryState? _activeState;
    private QuietModeRecoveryState? _pendingRecovery;
    private QuietModeLevel _mode = QuietModeLevel.Off;
    private int _autoThresholdCelsius = 75;
    private DateTime _lastAutoTransitionUtc = DateTime.MinValue;
    private double? _latestTemperatureCelsius;
    private double _latestCpuPercent;
    private int _highLoadStreak;
    private bool _disposed;

    public QuietModeService(
        QuietModeRecoveryState? pendingRecovery,
        Action<QuietModeRecoveryState?> persistRecoveryState,
        int autoThresholdCelsius = 75)
        : this(
            new WindowsQuietModePlatform(),
            pendingRecovery,
            persistRecoveryState,
            () => DateTime.UtcNow,
            autoThresholdCelsius)
    {
    }

    internal QuietModeService(
        IQuietModePlatform platform,
        QuietModeRecoveryState? pendingRecovery,
        Action<QuietModeRecoveryState?> persistRecoveryState,
        Func<DateTime>? utcNow = null,
        int autoThresholdCelsius = 75)
    {
        _platform = platform;
        _pendingRecovery = pendingRecovery;
        _persistRecoveryState = persistRecoveryState;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _autoThresholdCelsius = NormalizeThreshold(autoThresholdCelsius);
    }

    public QuietModeLevel Mode => _mode;

    public bool IsActive => _activeState is not null;

    public bool IsBoostDisabled => _activeState is not null;

    public int AutoThresholdCelsius => _autoThresholdCelsius;

    public int AutoResumeCelsius => _autoThresholdCelsius - HysteresisBandCelsius;

    public event Action? StateChanged;

    public static int NormalizeThreshold(int thresholdCelsius) =>
        Math.Clamp(thresholdCelsius, 60, 95);

    public void RecoverInterruptedSession()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_pendingRecovery is not { } state)
        {
            return;
        }

        _platform.ApplyBoostState(state);
        _persistRecoveryState(null);
        _pendingRecovery = null;
    }

    public void SetMode(QuietModeLevel mode)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_mode == mode && (mode != QuietModeLevel.Quiet || _activeState is not null))
        {
            return;
        }

        _highLoadStreak = 0;

        switch (mode)
        {
            case QuietModeLevel.Off:
                _mode = QuietModeLevel.Off;
                RestoreBoostCore(notify: false);
                StateChanged?.Invoke();
                break;

            case QuietModeLevel.Quiet:
                DisableBoostCore(notify: false);
                _mode = QuietModeLevel.Quiet;
                StateChanged?.Invoke();
                break;

            case QuietModeLevel.Auto:
                var shouldHoldBoostOff = _latestTemperatureCelsius is { } temp
                    && temp >= _autoThresholdCelsius;
                if (!shouldHoldBoostOff && _activeState is not null)
                {
                    RestoreBoostCore(notify: false);
                }

                _mode = QuietModeLevel.Auto;
                _lastAutoTransitionUtc = DateTime.MinValue;
                if (shouldHoldBoostOff && _activeState is null)
                {
                    DisableBoostCore(notify: false);
                    _lastAutoTransitionUtc = _utcNow();
                }

                StateChanged?.Invoke();
                break;
        }
    }

    public void SetAutoThreshold(int thresholdCelsius)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var normalized = NormalizeThreshold(thresholdCelsius);
        if (_autoThresholdCelsius == normalized)
        {
            return;
        }

        _autoThresholdCelsius = normalized;
        if (_mode == QuietModeLevel.Auto)
        {
            EvaluateMetrics(_latestTemperatureCelsius, _latestCpuPercent, ignoreHoldTimer: true);
        }

        StateChanged?.Invoke();
    }

    public void EvaluateMetrics(double? temperatureCelsius, double cpuTotalPercent)
    {
        EvaluateMetrics(temperatureCelsius, cpuTotalPercent, ignoreHoldTimer: false);
    }

    private void EvaluateMetrics(
        double? temperatureCelsius,
        double cpuTotalPercent,
        bool ignoreHoldTimer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _latestTemperatureCelsius = temperatureCelsius;
        _latestCpuPercent = cpuTotalPercent;

        if (_mode != QuietModeLevel.Auto)
        {
            return;
        }

        var now = _utcNow();
        var holdSatisfied = ignoreHoldTimer ||
            _lastAutoTransitionUtc == DateTime.MinValue ||
            now - _lastAutoTransitionUtc >= AutoMinimumHoldDuration;

        if (temperatureCelsius is { } tempC)
        {
            _highLoadStreak = 0;
            if (_activeState is null)
            {
                if (tempC >= _autoThresholdCelsius && holdSatisfied)
                {
                    DisableBoostCore(notify: true);
                    _lastAutoTransitionUtc = now;
                }
            }
            else if (tempC <= AutoResumeCelsius && holdSatisfied)
            {
                RestoreBoostCore(notify: true);
                _lastAutoTransitionUtc = now;
            }

            return;
        }

        if (_activeState is null)
        {
            _highLoadStreak = cpuTotalPercent >= 80.0 ? _highLoadStreak + 1 : 0;
            if (_highLoadStreak >= 3 && holdSatisfied)
            {
                _highLoadStreak = 0;
                DisableBoostCore(notify: true);
                _lastAutoTransitionUtc = now;
            }
        }
        else if (cpuTotalPercent <= 35.0 && holdSatisfied)
        {
            _highLoadStreak = 0;
            RestoreBoostCore(notify: true);
            _lastAutoTransitionUtc = now;
        }
    }

    public void Enable() => SetMode(QuietModeLevel.Quiet);

    public void Disable() => SetMode(QuietModeLevel.Off);

    public void Toggle(bool enabled) =>
        SetMode(enabled ? QuietModeLevel.Quiet : QuietModeLevel.Off);

    private void DisableBoostCore(bool notify)
    {
        if (_activeState is not null)
        {
            return;
        }

        if (_pendingRecovery is not null)
        {
            throw new InvalidOperationException(
                "Restore the previous processor boost policy before enabling Quiet Mode again.");
        }

        var originalState = _platform.CaptureActiveBoostState();
        _persistRecoveryState(originalState);
        var quietState = originalState with
        {
            AcProcessorBoostMode = 0,
            DcProcessorBoostMode = 0
        };

        try
        {
            _platform.ApplyBoostState(quietState);
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            try
            {
                _platform.ApplyBoostState(originalState);
                _persistRecoveryState(null);
            }
            catch (Exception rollbackException) when (
                rollbackException is Win32Exception or
                InvalidOperationException or
                IOException or
                UnauthorizedAccessException)
            {
                throw new AggregateException(
                    "Quiet Mode failed and the original processor boost policy could not be restored.",
                    exception,
                    rollbackException);
            }

            throw;
        }

        _activeState = originalState;
        if (notify)
        {
            StateChanged?.Invoke();
        }
    }

    private void RestoreBoostCore(bool notify)
    {
        if (_activeState is not { } state)
        {
            return;
        }

        _platform.ApplyBoostState(state);
        _activeState = null;
        if (notify)
        {
            StateChanged?.Invoke();
        }

        _persistRecoveryState(null);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            RestoreBoostCore(notify: false);
            _mode = QuietModeLevel.Off;
        }
        finally
        {
            _disposed = true;
        }
    }
}
