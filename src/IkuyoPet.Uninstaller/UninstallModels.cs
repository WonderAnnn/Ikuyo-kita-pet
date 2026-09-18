using System.IO;

namespace IkuyoPet.Uninstaller;

public enum UninstallDataChoice
{
    Preserve,
    Delete,
}

public sealed record UninstallValidationResult(
    bool IsValid,
    string InstallRoot,
    string DataRoot,
    string? Error)
{
    public static UninstallValidationResult Invalid(
        string installRoot,
        string dataRoot,
        string error) => new(false, installRoot, dataRoot, error);

    public static UninstallValidationResult Valid(
        string installRoot,
        string dataRoot) => new(true, installRoot, dataRoot, null);
}

public sealed record UninstallPlan(
    string InstallRoot,
    string AppExecutablePath,
    string DataRoot,
    string? DataRootToDelete,
    string ShortcutPath)
{
    public static UninstallPlan Create(
        UninstallValidationResult validation,
        UninstallDataChoice dataChoice)
    {
        ArgumentNullException.ThrowIfNull(validation);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException(
                validation.Error ?? "卸载目标未通过安全校验。");
        }

        return new UninstallPlan(
            validation.InstallRoot,
            Path.Combine(validation.InstallRoot, "IkuyoPet.exe"),
            validation.DataRoot,
            dataChoice == UninstallDataChoice.Delete ? validation.DataRoot : null,
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                "Ikuyo Pet.lnk"));
    }
}
