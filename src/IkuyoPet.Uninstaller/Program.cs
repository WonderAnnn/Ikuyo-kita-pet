using System.IO;
using System.Windows;

namespace IkuyoPet.Uninstaller;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length > 0 && string.Equals(args[0], "--cleanup", StringComparison.Ordinal))
        {
            return UninstallExecutor.RunCleanup(args);
        }

        var installRoot = AppContext.BaseDirectory;
        var dataRoot = UninstallTargetValidator.GetCurrentDataRoot();
        var validation = UninstallTargetValidator.Validate(installRoot, dataRoot);
        if (!validation.IsValid)
        {
            MessageBox.Show(
                validation.Error ?? "当前目录不是有效的 Ikuyo Pet 发布目录。",
                "Ikuyo Pet 卸载",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return 2;
        }

        if (UninstallExecutor.IsPetRunning())
        {
            MessageBox.Show(
                "Ikuyo Pet 正在运行，请先退出桌宠后再运行卸载程序。",
                "Ikuyo Pet 卸载",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return 3;
        }

        var choice = MessageBox.Show(
            $"将卸载以下发布目录：\n{validation.InstallRoot}\n\n" +
            $"当前数据目录：\n{validation.DataRoot}\n\n" +
            "点击“是”保留设置、日志、数据库和备份；点击“否”同时删除这些数据；点击“取消”退出。",
            "Ikuyo Pet 卸载",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);

        if (choice == MessageBoxResult.Cancel) return 0;

        var dataChoice = choice == MessageBoxResult.Yes
            ? UninstallDataChoice.Preserve
            : UninstallDataChoice.Delete;
        var plan = UninstallPlan.Create(validation, dataChoice);
        try
        {
            UninstallExecutor.StartCleanup(plan, Environment.ProcessId);
            MessageBox.Show(
                dataChoice == UninstallDataChoice.Delete
                    ? "卸载已开始，程序和本地数据将被删除。"
                    : "卸载已开始，程序已删除，本地数据会保留。",
                "Ikuyo Pet 卸载",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(
                $"无法启动卸载清理：{exception.Message}",
                "Ikuyo Pet 卸载",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return 1;
        }
    }
}
