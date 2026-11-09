namespace Inceptus.DiagramEngine.UnitTests.Architecture;

/// <summary>
/// Exact project files authorized by the N10.1 leaf-plugin gate. The production
/// reference graph remains asserted independently by SolutionArchitectureTests.
/// </summary>
internal static class ApprovedOrganizationalProjectChanges
{
    internal static bool IsApproved(string path) => path is
        "src/Inceptus.DiagramEngine.Blazor/Inceptus.DiagramEngine.Blazor.csproj" or
        "src/Inceptus.DiagramEngine.Organizational/Inceptus.DiagramEngine.Organizational.csproj" or
        "tests/Inceptus.DiagramEngine.UnitTests/Inceptus.DiagramEngine.UnitTests.csproj" or
        "tests/Inceptus.DiagramEngine.IntegrationTests/Inceptus.DiagramEngine.IntegrationTests.csproj";
}
