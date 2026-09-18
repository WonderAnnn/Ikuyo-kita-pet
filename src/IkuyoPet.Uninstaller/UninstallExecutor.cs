using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

namespace IkuyoPet.Uninstaller;

public static class UninstallExecutor
{
    private const string CleanupArgument = "--cleanup";

    public static bool IsPetRunning()
    {
        foreach (var process in Process.GetProcessesByName("IkuyoPet"))
        {
            try
            {
                if (!process.HasExited) return true;
            }
            catch (InvalidOperationException)
            {
                return true;
            }
            finally
            {
                process.Dispose();
            }
        }

        return false;
    }

    public static void StartCleanup(UninstallPlan plan, int parentProcessId)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var helperDirectory = Path.Combine(
            Path.GetTempPath(),
            $"IkuyoPet-uninstall-{Guid.NewGuid():N}");
        Directory.CreateDirectory(helperDirectory);
        var helperPath = Path.Combine(helperDirectory, "IkuyoPet.Uninstaller.exe");
        var currentPath = Environment.ProcessPath
            ?? throw new InvalidOperationException("无法定位卸载程序自身路径。");
        var currentDirectory = Path.GetDirectoryName(currentPath)
            ?? throw new InvalidOperationException("无法定位卸载程序目录。");
        foreach (var file in Directory.EnumerateFiles(currentDirectory))
        {
            File.Copy(file, Path.Combine(helperDirectory, Path.GetFileName(file)), overwrite: true);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = helperPath,
            UseShellExecute = true,
            WorkingDirectory = helperDirectory,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        startInfo.ArgumentList.Add(CleanupArgument);
        startInfo.ArgumentList.Add(plan.InstallRoot);
        startInfo.ArgumentList.Add(plan.DataRoot);
        startInfo.ArgumentList.Add(plan.DataRootToDelete is null
            ? UninstallDataChoice.Preserve.ToString()
            : UninstallDataChoice.Delete.ToString());
        startInfo.ArgumentList.Add(parentProcessId.ToString(CultureInfo.InvariantCulture));
        Process.Start(startInfo)?.Dispose();
    }

    public static int RunCleanup(string[] args)
    {
        if (args.Length != 5 || !string.Equals(args[0], CleanupArgument, StringComparison.Ordinal))
        {
            return 2;
        }

        var installRoot = args[1];
        var dataRoot = args[2];
        if (!Enum.TryParse<UninstallDataChoice>(args[3], ignoreCase: true, out var dataChoice) ||
            !int.TryParse(args[4], out var parentProcessId))
        {
            return 2;
        }

        var validation = UninstallTargetValidator.Validate(installRoot, dataRoot);
        if (!validation.IsValid) return WriteFailure(validation.Error ?? "卸载目标未通过安全校验。");
        WaitForParentExit(parentProcessId);

        var plan = UninstallPlan.Create(validation, dataChoice);
        try
        {
            DeleteMatchingShortcut(plan);
            Directory.Delete(plan.InstallRoot, recursive: true);
            if (plan.DataRootToDelete is not null && Directory.Exists(plan.DataRootToDelete))
            {
                Directory.Delete(plan.DataRootToDelete, recursive: true);
            }

            ScheduleHelperCleanup();
            return 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return WriteFailure(exception.ToString());
        }
    }

    private static void WaitForParentExit(int parentProcessId)
    {
        if (parentProcessId <= 0) return;
        try
        {
            using var parent = Process.GetProcessById(parentProcessId);
            while (!parent.HasExited) Thread.Sleep(100);
        }
        catch (ArgumentException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static void DeleteMatchingShortcut(UninstallPlan plan)
    {
        if (!File.Exists(plan.ShortcutPath)) return;

        object? shell = null;
        object? shortcut = null;
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null) return;
            shell = Activator.CreateInstance(shellType);
            if (shell is null) return;
            shortcut = shellType.InvokeMember(
                "CreateShortcut",
                System.Reflection.BindingFlags.InvokeMethod,
                binder: null,
                target: shell,
                args: [plan.ShortcutPath],
                modifiers: null,
                culture: CultureInfo.InvariantCulture,
                namedParameters: null);
            var targetPath = shortcut?.GetType().InvokeMember(
                "TargetPath",
                System.Reflection.BindingFlags.GetProperty,
                binder: null,
                target: shortcut,
                args: null,
                modifiers: null,
                culture: CultureInfo.InvariantCulture,
                namedParameters: null) as string;
            if (PathsEqual(targetPath, plan.AppExecutablePath))
            {
                File.Delete(plan.ShortcutPath);
            }
        }
        finally
        {
            if (shortcut is not null && Marshal.IsComObject(shortcut))
                Marshal.FinalReleaseComObject(shortcut);
            if (shell is not null && Marshal.IsComObject(shell))
                Marshal.FinalReleaseComObject(shell);
        }
    }

    private static bool PathsEqual(string? left, string right)
    {
        if (string.IsNullOrWhiteSpace(left)) return false;
        return string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            StringComparison.OrdinalIgnoreCase);
    }

    private static int WriteFailure(string message)
    {
        var logPath = Path.Combine(Path.GetTempPath(), "IkuyoPet-uninstall-error.log");
        try { File.AppendAllText(logPath, $"[{DateTimeOffset.Now:O}] {message}{Environment.NewLine}"); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return 1;
    }

    private static void ScheduleHelperCleanup()
    {
        var helperDirectory = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        var command = $"/d /c timeout /t 1 /nobreak >nul & rmdir /s /q \"{helperDirectory}\"";
        Process.Start(new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
            Arguments = command,
            CreateNoWindow = true,
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Hidden,
        })?.Dispose();
    }
}
