using System.IO;

namespace IkuyoPet.Uninstaller;

public static class UninstallTargetValidator
{
    public static string GetCurrentDataRoot() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IkuyoPet");

    public static UninstallValidationResult Validate(
        string installRoot,
        string dataRoot)
    {
        if (string.IsNullOrWhiteSpace(installRoot))
        {
            return UninstallValidationResult.Invalid(
                installRoot ?? string.Empty,
                dataRoot ?? string.Empty,
                "发布目录为空。");
        }

        string normalizedInstallRoot;
        string normalizedDataRoot;
        try
        {
            normalizedInstallRoot = NormalizeDirectory(installRoot);
            normalizedDataRoot = NormalizeDirectory(dataRoot);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException)
        {
            return UninstallValidationResult.Invalid(
                installRoot,
                dataRoot,
                $"路径无法解析：{exception.Message}");
        }

        var expectedDataRoot = NormalizeDirectory(GetCurrentDataRoot());
        if (!PathsEqual(normalizedDataRoot, expectedDataRoot))
        {
            return UninstallValidationResult.Invalid(
                normalizedInstallRoot,
                normalizedDataRoot,
                "数据目录不是当前用户的 IkuyoPet 数据目录。");
        }

        if (!Directory.Exists(normalizedInstallRoot))
        {
            return UninstallValidationResult.Invalid(
                normalizedInstallRoot,
                normalizedDataRoot,
                "发布目录不存在。");
        }

        if (PathsEqual(normalizedInstallRoot, normalizedDataRoot))
        {
            return UninstallValidationResult.Invalid(
                normalizedInstallRoot,
                normalizedDataRoot,
                "发布目录不能与用户数据目录相同。");
        }

        var appExecutable = Path.Combine(normalizedInstallRoot, "IkuyoPet.exe");
        var buildInfo = Path.Combine(normalizedInstallRoot, "build-info.json");
        if (!File.Exists(appExecutable) || !File.Exists(buildInfo))
        {
            return UninstallValidationResult.Invalid(
                normalizedInstallRoot,
                normalizedDataRoot,
                "目录缺少 IkuyoPet.exe 或 build-info.json，不是受支持的发布目录。");
        }

        return UninstallValidationResult.Valid(normalizedInstallRoot, normalizedDataRoot);
    }

    private static string NormalizeDirectory(string path) => Path.GetFullPath(
        path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

    private static bool PathsEqual(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
