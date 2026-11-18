namespace Inceptus.DiagramEngine.UnitTests.Architecture;

public sealed class ApprovedLocalInvestigationSeparationTests
{
    [Theory]
    [InlineData("App.razor")]
    [InlineData("App.razor.cs")]
    [InlineData("App.razor.css")]
    [InlineData("B4ApplicationActions.cs")]
    [InlineData("Inceptus.DiagramEngine.TestExample.csproj")]
    [InlineData("Program.cs")]
    [InlineData("Properties/AssemblyInfo.cs")]
    [InlineData("README.md")]
    [InlineData("_Imports.razor")]
    [InlineData("wwwroot/catalogue.json")]
    [InlineData("wwwroot/fixtures/process.inceptus.json")]
    [InlineData("wwwroot/index.html")]
    public void ApprovedIndexRemovalNeedsNoLocalApplication(string relativePath) =>
        Assert.True(ApprovedLocalInvestigationSeparation.IsIndexOnlyRemoval(
            $"D  tests/Inceptus.DiagramEngine.TestExample/{relativePath}"));

    [Theory]
    [InlineData(" D tests/Inceptus.DiagramEngine.TestExample/README.md")]
    [InlineData(" M tests/Inceptus.DiagramEngine.TestExample/README.md")]
    [InlineData("M  tests/Inceptus.DiagramEngine.TestExample/README.md")]
    [InlineData("A  tests/Inceptus.DiagramEngine.TestExample/README.md")]
    [InlineData("?? tests/Inceptus.DiagramEngine.TestExample/README.md")]
    [InlineData("D  tests/Inceptus.DiagramEngine.TestExample/Unknown.csproj")]
    [InlineData("D  tests/Inceptus.DiagramEngine.TestExample/../Other.csproj")]
    [InlineData("D  tests/Other/README.md")]
    [InlineData("D  src/Inceptus.DiagramEngine.Runtime/Inceptus.DiagramEngine.Runtime.csproj")]
    [InlineData("D  README.md")]
    [InlineData("")]
    public void OtherChangesRemainSubjectToHistoricalGuards(string statusLine) =>
        Assert.False(ApprovedLocalInvestigationSeparation.IsIndexOnlyRemoval(statusLine));
}
