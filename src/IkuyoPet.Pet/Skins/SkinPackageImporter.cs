using System.IO;

namespace IkuyoPet.Pet.Skins;

public sealed record SkinImportResult(
    bool IsImported,
    string? InstalledDirectory,
    SkinValidationResult Validation);

public sealed class SkinPackageImporter(
    SkinPackageValidator validator,
    string skinsRoot)
{
    public Task<SkinImportResult> ImportAsync(
        string sourceDirectory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(validator);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(skinsRoot);
        cancellationToken.ThrowIfCancellationRequested();

        if (!Directory.Exists(sourceDirectory))
        {
            return Task.FromResult(new SkinImportResult(
                false,
                null,
                new SkinValidationResult(
                    false,
                    null,
                    [$"Source directory does not exist: {sourceDirectory}"])));
        }

        var stagingRoot = Path.Combine(skinsRoot, ".staging");
        var stagingDirectory = Path.Combine(stagingRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagingDirectory);

        try
        {
            CopyDirectory(sourceDirectory, stagingDirectory, cancellationToken);
            var validation = validator.Validate(stagingDirectory);
            if (!validation.IsValid || validation.Manifest is null)
            {
                DeleteDirectory(stagingDirectory);
                return Task.FromResult(new SkinImportResult(false, null, validation));
            }

            if (!IsSafePathSegment(validation.Manifest.Id) ||
                !IsSafePathSegment(validation.Manifest.Version))
            {
                var errors = validation.Errors
                    .Append("manifest.id and manifest.version must be safe path segments.")
                    .ToArray();
                DeleteDirectory(stagingDirectory);
                return Task.FromResult(new SkinImportResult(
                    false,
                    null,
                    new SkinValidationResult(false, validation.Manifest, errors)));
            }

            var installedDirectory = ChooseInstallDirectory(
                validation.Manifest.Id,
                validation.Manifest.Version);
            Directory.CreateDirectory(Path.GetDirectoryName(installedDirectory)!);
            Directory.Move(stagingDirectory, installedDirectory);
            return Task.FromResult(new SkinImportResult(true, installedDirectory, validation));
        }
        catch (IOException exception)
        {
            DeleteDirectory(stagingDirectory);
            return Task.FromResult(FailedImport($"Skin package could not be imported: {exception.Message}"));
        }
        catch (UnauthorizedAccessException exception)
        {
            DeleteDirectory(stagingDirectory);
            return Task.FromResult(FailedImport($"Skin package access was denied: {exception.Message}"));
        }
        catch (ArgumentException exception)
        {
            DeleteDirectory(stagingDirectory);
            return Task.FromResult(FailedImport($"Skin package path is invalid: {exception.Message}"));
        }
    }

    private string ChooseInstallDirectory(string id, string version)
    {
        var versionRoot = Path.Combine(skinsRoot, id);
        var candidate = Path.Combine(versionRoot, version);
        while (Directory.Exists(candidate))
        {
            candidate = Path.Combine(versionRoot, $"{version}-{Guid.NewGuid():N}"[..(version.Length + 9)]);
        }

        return Path.GetFullPath(candidate);
    }

    private static void CopyDirectory(
        string sourceDirectory,
        string destinationDirectory,
        CancellationToken cancellationToken)
    {
        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = Path.GetRelativePath(sourceDirectory, file);
            var destinationFile = Path.Combine(destinationDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);
            File.Copy(file, destinationFile, overwrite: false);
        }
    }

    private static SkinImportResult FailedImport(string error) =>
        new(false, null, new SkinValidationResult(false, null, [error]));

    private static bool IsSafePathSegment(string value) =>
        value is not ("." or "..") &&
        value.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 &&
        !value.Contains(Path.DirectorySeparatorChar) &&
        !value.Contains(Path.AltDirectorySeparatorChar);

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
