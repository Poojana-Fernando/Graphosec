using ResumableCopy.Core.Core;
using ResumableCopy.Core.Storage.Sqlite;

namespace ResumableCopy.Core.Tests;

public class ProductionGateTests
{
    [Fact]
    public void DatabaseSchema_HasPositiveCurrentVersion()
    {
        Assert.True(SqliteSchema.CurrentVersion >= 1);
    }

    [Fact]
    public void CoreAssembly_HasVersionMetadata()
    {
        var version = typeof(CopyEngine).Assembly.GetName().Version;
        Assert.NotNull(version);
        Assert.True(version!.Major >= 1);
    }

    [Theory]
    [InlineData(typeof(CopyEngineTests))]
    [InlineData(typeof(ResumeTests))]
    [InlineData(typeof(ConcurrencyTests))]
    [InlineData(typeof(SecurityCopyEngineTests))]
    [InlineData(typeof(DatabaseMigrationTests))]
    [InlineData(typeof(PerformanceBenchmarkTests))]
    public void ProductionMatrix_IncludesCoreScenarioSuite(Type testType)
    {
        var methodCount = testType.GetMethods()
            .Count(method => method.GetCustomAttributes(typeof(FactAttribute), false).Length > 0
                || method.GetCustomAttributes(typeof(TheoryAttribute), false).Length > 0);

        Assert.True(methodCount > 0, $"Expected tests in {testType.Name}.");
    }
}
