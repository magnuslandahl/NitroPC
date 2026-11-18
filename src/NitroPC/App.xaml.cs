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

        _settingsService = new SettingsService();
        _settings = _settingsService.Load();
        _startupService = new StartupService();
        _metricsService = new SystemMetricsService();
        _keepAwakeService = new KeepAwakeService();
        _quietModeService = new QuietModeService(
            _settings.QuietModeRecoveryState,
            PersistQuietModeRecoveryState);
        _updateService = new UpdateService();
        _stripWindow = new TaskbarStripWindow(_settings);
        _flyoutWindow = new ToolboxFlyoutWindow(_settings);
        _trayIconService = new TrayIconService(_startupService.IsEnabled());

        WireEvents();
        RecoverQuietMode();
        _stripWindow.ApplySettings(_settings);
        _stripWindow.Show();
        RefreshKeepAwakeState();
        RefreshQuietModeState();

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

        if (_settings.AutoStartKeepAwakeOnLaunch)
        {
            StartKeepAwake(
                _settings.KeepAwakeMinutes <= 0
                    ? null
                    : TimeSpan.FromMinutes(_settings.KeepAwakeMinutes));
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
        _stripWindow.DockingHidden += () => _flyoutWindow!.Hide();

        _flyoutWindow!.SettingsChanged += OnSettingsChanged;
        _flyoutWindow.KeepAwakeStartRequested += StartKeepAwake;
        _flyoutWindow.KeepAwakeStopRequested += StopKeepAwake;
        _flyoutWindow.QuietModeChangedRequested += SetQuietModeEnabled;
        _flyoutWindow.StartupChangedRequested += SetStartupEnabled;
        _flyoutWindow.CheckForUpdatesRequested += () => _ = CheckForUpdatesAsync(interactive: true);
        _flyoutWindow.QuitRequested += Shutdown;

        _trayIconService!.OpenRequested += () => _flyoutWindow!.ToggleNearStrip(_stripWindow!.PhysicalBounds);
        _trayIconService.StartKeepAwakeRequested += StartKeepAwake;
        _trayIconService.StopKeepAwakeRequested += StopKeepAwake;
        _trayIconService.QuietModeChangedRequested += SetQuietModeEnabled;
        _trayIconService.StartupChangedRequested += SetStartupEnabled;
        _trayIconService.CheckForUpdatesRequested += () => _ = CheckForUpdatesAsync(interactive: true);
        _trayIconService.ExitRequested += Shutdown;
    }

    private void OnMetricsUpdated(MetricsSnapshot snapshot)
    {
        _stripWindow?.UpdateMetrics(snapshot);
        _flyoutWindow?.UpdateMetrics(snapshot);
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
        try
        {
            _keepAwakeService!.Toggle(_settings!.KeepAwakeMinutes);
        }
        catch (Win32Exception exception)
        {
            ReportError("NitroPC could not toggle Keep Awake.", exception);
        }
    }

    private void StartKeepAwake(TimeSpan? duration)
    {
        try
        {
            _keepAwakeService!.Start(duration);
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
        }
        catch (Win32Exception exception)
        {
            ReportError("NitroPC could not stop Keep Awake.", exception);
        }
    }

    private void SetQuietModeEnabled(bool enabled)
    {
        try
        {
            _quietModeService!.Toggle(enabled);
            RefreshQuietModeState();
        }
        catch (Win32Exception exception)
        {
            RefreshQuietModeState();
            ReportError("NitroPC could not change the Windows processor boost policy.", exception);
        }
        catch (InvalidOperationException exception)
        {
            RefreshQuietModeState();
            ReportError("Quiet Mode is not available in the current power state.", exception);
        }
        catch (IOException exception)
        {
            RefreshQuietModeState();
            ReportError("NitroPC could not persist the Quiet Mode recovery state.", exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            RefreshQuietModeState();
            ReportError("NitroPC does not have permission to save the Quiet Mode state.", exception);
        }
        catch (AggregateException exception)
        {
            RefreshQuietModeState();
            ReportError("Quiet Mode could not restore the Windows processor boost policy.", exception);
        }
    }

    private void RefreshQuietModeState()
    {
        var enabled = _quietModeService?.IsActive == true;
        _flyoutWindow?.SetQuietModeEnabled(enabled);
        _trayIconService?.SetQuietModeEnabled(enabled);
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

        _stripWindow?.UpdateKeepAwake(_keepAwakeService);
        _flyoutWindow?.UpdateKeepAwake(_keepAwakeService);
        _trayIconService?.UpdateKeepAwake(
            _keepAwakeService.IsAwake,
            _keepAwakeService.Remaining);
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
        _flyoutWindow?.SetUpdateStatus(interactive
            ? "Checking for updates…"
            : "Checking for updates in the background…");
        try
        {
            var result = await _updateService.CheckAsync();
            _flyoutWindow?.SetUpdateStatus(result.Message);
            if (!result.Installable)
            {
                if (interactive && result.Checked)
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

    private static void ReportError(string title, Exception exception)
    {
        WpfMessageBox.Show(
            $"{title}\n\n{exception.Message}",
            "NitroPC",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    protected override void OnExit(ExitEventArgs e)
    {
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
