namespace Inceptus.DiagramEngine.UnitTests.Architecture;

/// <summary>
/// Identifies the exact project and application-composition files approved by Phase N10.4.
/// Legacy phase guards continue to inspect their original generic surfaces and reject
/// unrelated dependency or notation-specific changes.
/// </summary>
internal static class ApprovedN104Changes
{
    internal static bool IsApprovedProjectPath(string path) => path is
        "src/Inceptus.DiagramEngine.Canvas2D/Inceptus.DiagramEngine.Canvas2D.csproj";

    internal static bool IsPublishingApplicationCompositionPath(string path) =>
        path.Replace('\\', '/').EndsWith(
            "src/Inceptus.DiagramEngine.Bpmn.Blazor/Presentation/DocumentCanvasHost.Publishing.cs",
            StringComparison.OrdinalIgnoreCase);
}
