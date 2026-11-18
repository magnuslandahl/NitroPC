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
    private readonly IQuietModePlatform _platform;
    private readonly Action<QuietModeRecoveryState?> _persistRecoveryState;
    private QuietModeRecoveryState? _activeState;
    private QuietModeRecoveryState? _pendingRecovery;
    private bool _disposed;

    public QuietModeService(
        QuietModeRecoveryState? pendingRecovery,
        Action<QuietModeRecoveryState?> persistRecoveryState)
        : this(new WindowsQuietModePlatform(), pendingRecovery, persistRecoveryState)
    {
    }

    internal QuietModeService(
        IQuietModePlatform platform,
        QuietModeRecoveryState? pendingRecovery,
        Action<QuietModeRecoveryState?> persistRecoveryState)
    {
        _platform = platform;
        _pendingRecovery = pendingRecovery;
        _persistRecoveryState = persistRecoveryState;
    }

    public bool IsActive => _activeState is not null;

    public event Action? StateChanged;

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

    public void Enable()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
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
        StateChanged?.Invoke();
    }

    public void Disable()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_activeState is not { } state)
        {
            return;
        }

        _platform.ApplyBoostState(state);
        _activeState = null;
        StateChanged?.Invoke();
        _persistRecoveryState(null);
    }

    public void Toggle(bool enabled)
    {
        if (enabled)
        {
            Enable();
        }
        else
        {
            Disable();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            Disable();
        }
        finally
        {
            _disposed = true;
        }
    }
}
