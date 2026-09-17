using IkuyoPet.Infrastructure.Tests.TestSupport;
using Xunit;

namespace IkuyoPet.Infrastructure.Tests.App;

public sealed class RuntimeMeasurementScriptContractTests
{
    [Fact]
    public void MeasurementScriptRecordsRequiredMetricsWithoutWindowContent()
    {
        var script = File.ReadAllText(Path.Combine(
            RepositoryPaths.Root, "scripts", "measure-runtime.ps1"));

        Assert.Contains("startupMilliseconds", script, StringComparison.Ordinal);
        Assert.Contains("averageCpuPercent", script, StringComparison.Ordinal);
        Assert.Contains("privateWorkingSetBytes", script, StringComparison.Ordinal);
        Assert.Contains("sampleCount", script, StringComparison.Ordinal);
        Assert.DoesNotContain("MainWindowTitle", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("WindowTitle", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Get-Process |", script, StringComparison.Ordinal);
    }
}
