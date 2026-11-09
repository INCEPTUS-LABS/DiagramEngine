namespace Inceptus.DiagramEngine.UnitTests.Architecture;

/// <summary>
/// Identifies the narrowly bounded project-configuration changes approved by Phase P1.2.
/// The resulting dependency graph, package identity, and packability remain asserted
/// independently by <see cref="SolutionArchitectureTests"/>.
/// </summary>
internal static class ApprovedP12ReusableBpmnBlazorChanges
{
    internal static bool IsApprovedProjectPath(string path) => path is
        "src/Inceptus.DiagramEngine.Blazor/Inceptus.DiagramEngine.Blazor.csproj" or
        "src/Inceptus.DiagramEngine.Bpmn.Blazor/Inceptus.DiagramEngine.Bpmn.Blazor.csproj" or
        "tests/Inceptus.DiagramEngine.IntegrationTests/Inceptus.DiagramEngine.IntegrationTests.csproj" or
        "tests/Inceptus.DiagramEngine.UnitTests/Inceptus.DiagramEngine.UnitTests.csproj";
}
