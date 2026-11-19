using System.ComponentModel;
using System.Diagnostics;

return await UpdaterMain.RunAsync(args);

internal static class UpdaterMain
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length != 5 ||
            !int.TryParse(args[0], out var processId) ||
            processId <= 0)
        {
            return 2;
        }

        var logPath = args[4];
        try
        {
            await InstallAsync(
                processId,
                Path.GetFullPath(args[1]),
                Path.GetFullPath(args[2]),
                args[3]);
            return 0;
        }
        catch (Exception exception) when (exception is
               IOException or
               UnauthorizedAccessException or
               ArgumentException or
               InvalidOperationException or
               Win32Exception)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
                await File.AppendAllTextAsync(
                    logPath,
                    $"[{DateTimeOffset.Now:O}] Update failed: {exception.Message}{Environment.NewLine}");
            }
            catch (IOException)
            {
                return 1;
            }
            catch (UnauthorizedAccessException)
            {
                return 1;
            }

            return 1;
        }
    }

    private static async Task InstallAsync(
        int processId,
        string stagedPath,
        string installPath,
        string executableName)
    {
        if (string.Equals(stagedPath, installPath, StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(Path.Combine(stagedPath, executableName)) ||
            !File.Exists(Path.Combine(stagedPath, "NitroPC.Updater.exe")))
        {
            throw new InvalidOperationException("The staged NitroPC update is incomplete.");
        }

        await WaitForExitAsync(processId);
        var installParent = Path.GetDirectoryName(installPath)
            ?? throw new InvalidOperationException("The NitroPC install folder has no parent.");
        var backupPath = Path.Combine(
            installParent,
            $".NitroPC-backup-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}");
        var movedOriginal = false;

        try
        {
            if (Directory.Exists(installPath))
            {
                await MoveDirectoryWithRetryAsync(installPath, backupPath);
                movedOriginal = true;
            }

            CopyDirectory(stagedPath, installPath);
            Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(installPath, executableName),
                WorkingDirectory = installPath,
                UseShellExecute = true
            });
        }
        catch (Exception exception) when (exception is
               IOException or
               UnauthorizedAccessException or
               Win32Exception)
        {
            if (movedOriginal)
            {
                if (Directory.Exists(installPath))
                {
                    Directory.Delete(installPath, recursive: true);
                }

                Directory.Move(backupPath, installPath);
            }

            throw new InvalidOperationException("NitroPC could not be replaced; the previous version was restored.", exception);
        }

        if (movedOriginal && Directory.Exists(backupPath))
        {
            Directory.Delete(backupPath, recursive: true);
        }

        var stagedParent = Path.GetDirectoryName(stagedPath);
        if (stagedParent is not null && Directory.Exists(stagedParent))
        {
            Directory.Delete(stagedParent, recursive: true);
        }
    }

    private static async Task WaitForExitAsync(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (ArgumentException)
        {
        }
        catch (OperationCanceledException exception)
        {
            throw new InvalidOperationException("NitroPC did not exit in time for the update.", exception);
        }
    }

    private static async Task MoveDirectoryWithRetryAsync(string sourcePath, string destinationPath)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                Directory.Move(sourcePath, destinationPath);
                return;
            }
            catch (Exception exception) when (
                attempt < 12 &&
                exception is IOException or UnauthorizedAccessException)
            {
                await Task.Delay(250);
            }
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(
                destination,
                Path.GetRelativePath(source, directory)));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }
}
