namespace Inceptus.DiagramEngine.UnitTests.Architecture;

/// <summary>
/// Identifies the exact production project files whose package metadata is approved by Phase P1.1.
/// Package identities, packability, dependencies, and the source reference graph remain asserted
/// independently by <see cref="SolutionArchitectureTests"/>.
/// </summary>
internal static class ApprovedP11PackageFoundationChanges
{
    internal static bool IsApprovedProjectPath(string path) => path is
        "src/Inceptus.DiagramEngine.Blazor/Inceptus.DiagramEngine.Blazor.csproj" or
        "src/Inceptus.DiagramEngine.Bpmn/Inceptus.DiagramEngine.Bpmn.csproj" or
        "src/Inceptus.DiagramEngine.Canvas2D/Inceptus.DiagramEngine.Canvas2D.csproj" or
        "src/Inceptus.DiagramEngine.Contracts/Inceptus.DiagramEngine.Contracts.csproj" or
        "src/Inceptus.DiagramEngine.Organizational/Inceptus.DiagramEngine.Organizational.csproj" or
        "src/Inceptus.DiagramEngine.Runtime/Inceptus.DiagramEngine.Runtime.csproj" or
        // Phase A also reports the deleted side of these six approved project renames.
        // SolutionArchitectureTests independently rejects extra/legacy projects and graph changes.
        "src/Inceptus.DocumentEngine.Contracts/Inceptus.DocumentEngine.Contracts.csproj" or
        "src/Inceptus.DocumentEngine.Runtime/Inceptus.DocumentEngine.Runtime.csproj" or
        "src/Inceptus.DocumentEngine.Bpmn/Inceptus.DocumentEngine.Bpmn.csproj" or
        "src/Inceptus.DocumentEngine.Organizational/Inceptus.DocumentEngine.Organizational.csproj" or
        "src/Inceptus.DocumentEngine.Canvas2D/Inceptus.DocumentEngine.Canvas2D.csproj" or
        "src/Inceptus.DocumentEngine.Bpmn.Blazor/Inceptus.DocumentEngine.Bpmn.Blazor.csproj";
}
