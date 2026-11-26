using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Security;
using System.Threading;
using System.Windows;
using NitroPC.Models;
using NitroPC.Services;
using NitroPC.Views;
using WpfApplication = System.Windows.Application;
using WpfMessageBox = System.Windows.MessageBox;

namespace NitroPC;

public partial class App : WpfApplication
{
    private Mutex? _singleInstance;
    private SettingsService? _settingsService;
    private StartupService? _startupService;
    private SystemMetricsService? _metricsService;
    private KeepAwakeService? _keepAwakeService;
    private QuietModeService? _quietModeService;
    private UpdateService? _updateService;
    private TrayIconService? _trayIconService;
    private TaskbarStripWindow? _stripWindow;
    private ToolboxFlyoutWindow? _flyoutWindow;
    private NitroSettings? _settings;
    private bool _updateCheckInProgress;
    private bool _startupCompleted;
    private bool _isShuttingDown;

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstance = new Mutex(
            initiallyOwned: true,
            name: @"Local\NitroPC_SingleInstance_Mutex",
            createdNew: out var createdNew);
        if (!createdNew)
        {
            _singleInstance.Dispose();
            _singleInstance = null;
            Shutdown();
            return;
        }

        base.OnStartup(e);
        SessionEnding += OnSessionEnding;

        _settingsService = new SettingsService();
        _settings = _settingsService.Load();
        SaveSettingsSafely(_settings);

        _startupService = new StartupService();
        EnsureStartupPathSafely();

        _metricsService = new SystemMetricsService();
        _keepAwakeService = new KeepAwakeService();
        _quietModeService = new QuietModeService(
            _settings.QuietModeRecoveryState,
            PersistQuietModeRecoveryState,
            _settings.AutoQuietThresholdCelsius);
        _updateService = new UpdateService();
        _stripWindow = new TaskbarStripWindow(_settings);
        _flyoutWindow = new ToolboxFlyoutWindow(_settings);
        _trayIconService = new TrayIconService(_startupService.IsEnabled());

        WireEvents();
        RecoverQuietMode();
        if (_settings.QuietMode != QuietModeLevel.Off)
        {
            SetQuietMode(_settings.QuietMode);
        }

        _stripWindow.ApplySettings(_settings);
        _stripWindow.Show();

        if (_settings.AutoStartKeepAwakeOnLaunch)
        {
            StartKeepAwake(
                _settings.KeepAwakeMinutes <= 0
                    ? null
                    : TimeSpan.FromMinutes(_settings.KeepAwakeMinutes));
        }

        RefreshKeepAwakeState();
        RefreshQuietModeState();
        _startupCompleted = true;

        try
        {
            _metricsService.Start();
        }
        catch (InvalidOperationException exception)
        {
            ReportError("NitroPC could not read CPU telemetry.", exception);
        }
        catch (Win32Exception exception)
        {
            ReportError("NitroPC could not read Windows memory telemetry.", exception);
        }

        if (_settings.AutomaticallyCheckForUpdates)
        {
            Dispatcher.BeginInvoke(async () => await CheckForUpdatesAsync(interactive: false));
        }
    }

    private void WireEvents()
    {
        _metricsService!.MetricsUpdated += OnMetricsUpdated;
        _metricsService.SamplingFailed += exception =>
            ReportError("NitroPC stopped refreshing system telemetry.", exception);

        _keepAwakeService!.StateChanged += RefreshKeepAwakeState;
        _keepAwakeService.OperationFailed += exception =>
            ReportError("NitroPC could not maintain Keep Awake.", exception);
        _quietModeService!.StateChanged += RefreshQuietModeState;

        _stripWindow!.ToggleFlyoutRequested += () => _flyoutWindow!.ToggleNearStrip(_stripWindow.PhysicalBounds);
        _stripWindow.ToggleKeepAwakeRequested += ToggleKeepAwake;
        _stripWindow.ContextMenuRequested += () => _trayIconService!.ShowContextMenu();
        _stripWindow.FullscreenEntered += () =>
        {
            if (_flyoutWindow is { IsVisible: true, IsActive: false })
            {
                _flyoutWindow.Hide();
            }
        };
        _stripWindow.Closed += (_, _) =>
        {
            if (!_isShuttingDown)
            {
                Shutdown();
            }
        };

        _flyoutWindow!.SettingsChanged += OnSettingsChanged;
        _flyoutWindow.KeepAwakeStartRequested += StartKeepAwake;
        _flyoutWindow.KeepAwakeStopRequested += StopKeepAwake;
        _flyoutWindow.QuietModeChangedRequested += SetQuietMode;
        _flyoutWindow.AutoQuietThresholdChangedRequested += SetAutoQuietThreshold;
        _flyoutWindow.StartupChangedRequested += SetStartupEnabled;
        _flyoutWindow.CheckForUpdatesRequested += () => _ = CheckForUpdatesAsync(interactive: true);
        _flyoutWindow.QuitRequested += Shutdown;

        _trayIconService!.OpenRequested += clickPoint =>
            Dispatcher.BeginInvoke(() => _flyoutWindow!.ToggleFromTray(clickPoint));
        _trayIconService.StartKeepAwakeRequested += StartKeepAwake;
        _trayIconService.StopKeepAwakeRequested += StopKeepAwake;
        _trayIconService.QuietModeChangedRequested += mode =>
        {
            _settings!.QuietMode = mode;
            OnSettingsChanged(_settings);
            SetQuietMode(mode);
        };
        _trayIconService.StartupChangedRequested += SetStartupEnabled;
        _trayIconService.CheckForUpdatesRequested += () => _ = CheckForUpdatesAsync(interactive: true);
        _trayIconService.ExitRequested += Shutdown;
    }

    private void OnMetricsUpdated(MetricsSnapshot snapshot)
    {
        if (_quietModeService is { Mode: QuietModeLevel.Auto })
        {
            try
            {
                _quietModeService.EvaluateMetrics(
                    snapshot.CpuTemperatureCelsius,
                    snapshot.CpuTotalPercent);
            }
            catch (Win32Exception exception)
            {
                _quietModeService.Disable();
                RefreshQuietModeState();
                ReportError("Auto Quiet Mode could not update the Windows processor boost policy.", exception);
            }
            catch (InvalidOperationException exception)
            {
                _quietModeService.Disable();
                RefreshQuietModeState();
                ReportError("Auto Quiet Mode is not available in the current power state.", exception);
            }
            catch (IOException exception)
            {
                _quietModeService.Disable();
                RefreshQuietModeState();
                ReportError("NitroPC could not save the Auto Quiet Mode recovery state.", exception);
            }
            catch (UnauthorizedAccessException exception)
            {
                _quietModeService.Disable();
                RefreshQuietModeState();
                ReportError("NitroPC does not have permission to save the Auto Quiet Mode state.", exception);
            }
            catch (AggregateException exception)
            {
                RefreshQuietModeState();
                ReportError("Auto Quiet Mode could not restore the Windows processor boost policy.", exception);
            }
        }

        _stripWindow?.UpdateMetrics(snapshot);
        _flyoutWindow?.UpdateMetrics(snapshot);
        _trayIconService?.UpdateMetrics(snapshot);
    }

    private void OnSettingsChanged(NitroSettings settings)
    {
        try
        {
            _settingsService!.Save(settings);
            _stripWindow!.ApplySettings(settings);
        }
        catch (IOException exception)
        {
            ReportError("NitroPC could not save its settings.", exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            ReportError("NitroPC could not save its settings.", exception);
        }
    }

    private void ToggleKeepAwake()
    {
        if (_keepAwakeService is null || _settings is null)
        {
            return;
        }

        if (_keepAwakeService.IsAwake)
        {
            StopKeepAwake();
        }
        else
        {
            StartKeepAwake(
                _settings.KeepAwakeMinutes <= 0
                    ? null
                    : TimeSpan.FromMinutes(_settings.KeepAwakeMinutes));
        }
    }

    private void StartKeepAwake(TimeSpan? duration)
    {
        try
        {
            _keepAwakeService!.Start(duration);
            if (_settings is not null)
            {
                _settings.KeepAwakeMinutes = duration is null
                    ? 0
                    : NitroSettings.NormalizePresetMinutes((int)Math.Round(duration.Value.TotalMinutes));
                _settings.AutoStartKeepAwakeOnLaunch = true;
                SaveSettingsSafely(_settings);
            }
        }
        catch (Win32Exception exception)
        {
            ReportError("NitroPC could not start Keep Awake.", exception);
        }
    }

    private void StopKeepAwake()
    {
        try
        {
            _keepAwakeService!.Stop();
            if (_settings is not null && _settings.AutoStartKeepAwakeOnLaunch)
            {
                _settings.AutoStartKeepAwakeOnLaunch = false;
                SaveSettingsSafely(_settings);
            }
        }
        catch (Win32Exception exception)
        {
            ReportError("NitroPC could not stop Keep Awake.", exception);
        }
    }

    private void SetQuietMode(QuietModeLevel mode)
    {
        try
        {
            _quietModeService!.SetMode(mode);
            RefreshQuietModeState();
        }
        catch (Win32Exception exception)
        {
            _settings!.QuietMode = _quietModeService!.Mode;
            RefreshQuietModeState();
            ReportError("NitroPC could not change the Windows processor boost policy.", exception);
        }
        catch (InvalidOperationException exception)
        {
            _settings!.QuietMode = _quietModeService!.Mode;
            RefreshQuietModeState();
            ReportError("Quiet Mode is not available in the current power state.", exception);
        }
        catch (IOException exception)
        {
            _settings!.QuietMode = _quietModeService!.Mode;
            RefreshQuietModeState();
            ReportError("NitroPC could not persist the Quiet Mode recovery state.", exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            _settings!.QuietMode = _quietModeService!.Mode;
            RefreshQuietModeState();
            ReportError("NitroPC does not have permission to save the Quiet Mode state.", exception);
        }
        catch (AggregateException exception)
        {
            _settings!.QuietMode = _quietModeService!.Mode;
            RefreshQuietModeState();
            ReportError("Quiet Mode could not restore the Windows processor boost policy.", exception);
        }
    }

    private void SetAutoQuietThreshold(int thresholdCelsius)
    {
        try
        {
            _quietModeService!.SetAutoThreshold(thresholdCelsius);
            RefreshQuietModeState();
        }
        catch (Win32Exception exception)
        {
            RefreshQuietModeState();
            ReportError("NitroPC could not apply the Auto Quiet threshold.", exception);
        }
        catch (InvalidOperationException exception)
        {
            RefreshQuietModeState();
            ReportError("Auto Quiet Mode is not available in the current power state.", exception);
        }
        catch (IOException exception)
        {
            RefreshQuietModeState();
            ReportError("NitroPC could not save the Quiet Mode recovery state.", exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            RefreshQuietModeState();
            ReportError("NitroPC does not have permission to save the Quiet Mode state.", exception);
        }
        catch (AggregateException exception)
        {
            RefreshQuietModeState();
            ReportError("Auto Quiet Mode could not restore the Windows processor boost policy.", exception);
        }
    }

    private void RefreshQuietModeState()
    {
        if (_quietModeService is null)
        {
            return;
        }

        _stripWindow?.UpdateQuietMode(_quietModeService.IsBoostDisabled);
        _flyoutWindow?.UpdateQuietMode(
            _quietModeService.Mode,
            _quietModeService.IsBoostDisabled,
            _quietModeService.AutoThresholdCelsius);
        _trayIconService?.SetQuietMode(_quietModeService.Mode, _quietModeService.IsBoostDisabled);
    }

    private void RecoverQuietMode()
    {
        try
        {
            _quietModeService!.RecoverInterruptedSession();
        }
        catch (Win32Exception exception)
        {
            ReportError("NitroPC could not restore the previous processor boost policy.", exception);
        }
        catch (InvalidOperationException exception)
        {
            ReportError("NitroPC could not restore the previous processor boost policy.", exception);
        }
        catch (IOException exception)
        {
            ReportError("NitroPC restored Quiet Mode but could not clear its recovery marker.", exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            ReportError("NitroPC restored Quiet Mode but could not clear its recovery marker.", exception);
        }
    }

    private void PersistQuietModeRecoveryState(QuietModeRecoveryState? state)
    {
        _settings!.QuietModeRecoveryState = state;
        _settingsService!.Save(_settings);
    }

    private void RefreshKeepAwakeState()
    {
        if (_keepAwakeService is null)
        {
            return;
        }

        if (_startupCompleted &&
            !_isShuttingDown &&
            !_keepAwakeService.IsAwake &&
            _settings is { AutoStartKeepAwakeOnLaunch: true })
        {
            _settings.AutoStartKeepAwakeOnLaunch = false;
            SaveSettingsSafely(_settings);
        }

        _stripWindow?.UpdateKeepAwake(_keepAwakeService);
        _flyoutWindow?.UpdateKeepAwake(_keepAwakeService);
        _trayIconService?.UpdateKeepAwake(
            _keepAwakeService.IsAwake,
            _keepAwakeService.Remaining);
    }

    private void SaveSettingsSafely(NitroSettings settings)
    {
        try
        {
            _settingsService?.Save(settings);
        }
        catch (IOException exception)
        {
            ReportError("NitroPC could not save its settings.", exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            ReportError("NitroPC could not save its settings.", exception);
        }
    }

    private void EnsureStartupPathSafely()
    {
        try
        {
            _startupService?.EnsureCurrentExecutablePathIfEnabled();
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (SecurityException)
        {
        }
    }

    private void OnSessionEnding(object sender, SessionEndingCancelEventArgs e)
    {
        _isShuttingDown = true;
        if (_settings is not null)
        {
            SaveSettingsSafely(_settings);
        }
    }

    private void SetStartupEnabled(bool enabled)
    {
        try
        {
            _startupService!.SetEnabled(enabled);
            _trayIconService!.SetStartupEnabled(enabled);
            _flyoutWindow!.SetStartupEnabled(enabled);
        }
        catch (IOException exception)
        {
            ReportError("NitroPC could not change the Windows startup setting.", exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            ReportError("NitroPC could not change the Windows startup setting.", exception);
        }
        catch (SecurityException exception)
        {
            ReportError("Windows denied the startup setting change.", exception);
        }
    }

    private async Task CheckForUpdatesAsync(bool interactive)
    {
        if (_updateCheckInProgress || _updateService is null)
        {
            return;
        }

        _updateCheckInProgress = true;
        if (interactive)
        {
            _flyoutWindow?.SetUpdateStatus("Checking for updates…");
        }

        try
        {
            var result = await _updateService.CheckAsync();
            if (interactive || result.Available)
            {
                _flyoutWindow?.SetUpdateStatus(result.Message);
            }
            if (!result.Installable)
            {
                if (interactive && _flyoutWindow?.IsVisible != true)
                {
                    WpfMessageBox.Show(
                        result.Message,
                        "NitroPC Updates",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }

                return;
            }

            var answer = WpfMessageBox.Show(
                $"{result.Message}\n\nDownload the verified update and restart NitroPC now?",
                "NitroPC Update",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);
            if (answer != MessageBoxResult.Yes)
            {
                return;
            }

            var progress = new Progress<double>(value =>
                _flyoutWindow?.SetUpdateStatus($"Downloading update… {value:P0}"));
            await _updateService.DownloadAndLaunchUpdateAsync(result, progress);
            _flyoutWindow?.SetUpdateStatus("Update ready. Restarting NitroPC…");
            Shutdown();
        }
        catch (HttpRequestException exception)
        {
            ReportError("NitroPC could not download the update.", exception);
        }
        catch (TaskCanceledException exception)
        {
            ReportError("The update download timed out.", exception);
        }
        catch (IOException exception)
        {
            ReportError("NitroPC could not prepare the update.", exception);
        }
        catch (InvalidDataException exception)
        {
            ReportError("The downloaded update was rejected.", exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            ReportError("NitroPC does not have permission to install the update.", exception);
        }
        catch (InvalidOperationException exception)
        {
            ReportError("The update could not be started.", exception);
        }
        catch (Win32Exception exception)
        {
            ReportError("The updater process could not be started.", exception);
        }
        finally
        {
            _updateCheckInProgress = false;
        }
    }

    private void ReportError(string title, Exception exception)
    {
        if (_isShuttingDown)
        {
            return;
        }

        WpfMessageBox.Show(
            $"{title}\n\n{exception.Message}",
            "NitroPC",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _isShuttingDown = true;
        SessionEnding -= OnSessionEnding;
        try
        {
            try
            {
                _quietModeService?.Dispose();
            }
            catch (Win32Exception exception)
            {
                ReportError("NitroPC could not restore the Windows processor boost policy.", exception);
            }
            catch (InvalidOperationException exception)
            {
                ReportError("NitroPC could not restore the Windows processor boost policy.", exception);
            }
            catch (IOException exception)
            {
                ReportError("NitroPC restored Quiet Mode but could not clear its recovery marker.", exception);
            }
            catch (UnauthorizedAccessException exception)
            {
                ReportError("NitroPC restored Quiet Mode but could not clear its recovery marker.", exception);
            }
            finally
            {
                _metricsService?.Dispose();
            }
        }
        finally
        {
            try
            {
                _keepAwakeService?.Dispose();
            }
            finally
            {
                try
                {
                    _trayIconService?.Dispose();
                    _updateService?.Dispose();
                    _stripWindow?.Close();
                    _flyoutWindow?.Close();
                }
                finally
                {
                    _singleInstance?.ReleaseMutex();
                    _singleInstance?.Dispose();
                    base.OnExit(e);
                }
            }
        }
    }
}
