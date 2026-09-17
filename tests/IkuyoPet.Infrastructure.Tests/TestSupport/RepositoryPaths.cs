using System;
using System.IO;
namespace IkuyoPet.Infrastructure.Tests.TestSupport;

internal static class RepositoryPaths
{
    public static string Root => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
}
