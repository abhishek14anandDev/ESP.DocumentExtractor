using FluentAssertions;

namespace ESP.DocumentExtractor.IntegrationTests;

public sealed class ArchitectureSmokeTests
{
    [Fact]
    public void SqlScript_ShouldExist()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var path = Path.Combine(root, "src", "Infrastructure", "Scripts", "001_CreateTables.sql");
        File.Exists(path).Should().BeTrue();
    }
}
