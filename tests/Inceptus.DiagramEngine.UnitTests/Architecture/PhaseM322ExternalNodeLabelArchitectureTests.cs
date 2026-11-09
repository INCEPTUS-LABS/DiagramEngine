using Inceptus.DiagramEngine.Contracts.Projection;

namespace Inceptus.DiagramEngine.UnitTests.Architecture;

public sealed class PhaseM322ExternalNodeLabelArchitectureTests
{
    [Fact]
    public void PlacementContractIsPublicTransientNotationNeutralAndIntentionallySmall()
    {
        Assert.Equal(
            [NodeLabelPlacementKind.InsideCentered, NodeLabelPlacementKind.OutsideBelow],
            Enum.GetValues<NodeLabelPlacementKind>());
        Assert.Equal(
            typeof(ProjectedLabel).Assembly,
            typeof(NodeLabelPlacement).Assembly);

        var placementSource = ReadProductionFile(
            "Inceptus.DiagramEngine.Contracts",
            "Projection",
            "NodeLabelPlacement.cs");
        Assert.DoesNotContain("Bpmn", placementSource, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Gateway", placementSource, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("OutsideAbove", placementSource, StringComparison.Ordinal);
        Assert.DoesNotContain("OutsideLeft", placementSource, StringComparison.Ordinal);
        Assert.DoesNotContain("OutsideRight", placementSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Manual", placementSource, StringComparison.Ordinal);
    }

    [Fact]
    public void PlacementExistsOnlyInProjectionAndSceneInterpretationNotPersistentState()
    {
        var persistentSource = string.Concat(
            ReadProductionDirectory("Inceptus.DiagramEngine.Contracts", "Documents", "*.cs"),
            ReadProductionDirectory("Inceptus.DiagramEngine.Contracts", "Semantics", "*.cs"),
            ReadProductionDirectory("Inceptus.DiagramEngine.Contracts", "Visuals", "*.cs"),
            ReadProductionDirectory("Inceptus.DiagramEngine.Contracts", "Metadata", "*.cs"),
            ReadProductionDirectory("Inceptus.DiagramEngine.Contracts", "EditorState", "*.cs"),
            ReadProductionDirectory("Inceptus.DiagramEngine.Contracts", "History", "*.cs"));
        var sceneSource = string.Concat(
            ReadProductionFile(
                "Inceptus.DiagramEngine.Canvas2D",
                "Scene",
                "Canvas2DSceneBuilder.TextLayout.cs"),
            ReadProductionFile(
                "Inceptus.DiagramEngine.Canvas2D",
                "Scene",
                "Canvas2DSceneBuilder.Composition.cs"));

        Assert.DoesNotContain(nameof(NodeLabelPlacement), persistentSource, StringComparison.Ordinal);
        Assert.Contains(nameof(NodeLabelPlacementKind), sceneSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Bpmn", sceneSource, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RendererJavaScriptLayoutAndRoutingRemainPlacementUnaware()
    {
        var rendererSource = ReadProductionDirectory(
            "Inceptus.DiagramEngine.Canvas2D",
            "Rendering",
            "*.cs");
        var layoutRoutingSource = string.Concat(
            ReadProductionFile(
                "Inceptus.DiagramEngine.Bpmn",
                "Layout",
                "BpmnLayoutAlgorithm.cs"),
            ReadProductionFile(
                "Inceptus.DiagramEngine.Bpmn",
                "Routing",
                "BpmnRoutingAlgorithm.cs"));
        var javaScript = string.Concat(
            File.ReadAllText(Path.Combine(
                RepositoryRoot,
                "src",
                "Inceptus.DiagramEngine.Canvas2D",
                "wwwroot",
                "inceptus.canvas2d.js")),
            File.ReadAllText(Path.Combine(
                RepositoryRoot,
                "src",
                "Inceptus.DiagramEngine.Bpmn.Blazor",
                "wwwroot",
                "inceptus.presentation.js")));

        Assert.DoesNotContain(nameof(NodeLabelPlacementKind), rendererSource, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(NodeLabelPlacementKind), javaScript, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(NodeLabelPlacementKind), layoutRoutingSource, StringComparison.Ordinal);
        Assert.DoesNotContain("OutsideBelow", rendererSource, StringComparison.Ordinal);
        Assert.DoesNotContain("OutsideBelow", javaScript, StringComparison.Ordinal);
        Assert.DoesNotContain("OutsideBelow", layoutRoutingSource, StringComparison.Ordinal);
    }

    [Fact]
    public void BpmnContributesPlacementButDoesNotMeasureWrapOrCreateGatewayText()
    {
        var projectionSource = ReadProductionFile(
            "Inceptus.DiagramEngine.Bpmn",
            "Projection",
            "BpmnProjectionRules.cs");
        var registrationSource = ReadProductionFile(
            "Inceptus.DiagramEngine.Bpmn",
            string.Empty,
            "BpmnPluginRegistration.cs");
        var contributorSource = ReadProductionFile(
            "Inceptus.DiagramEngine.Bpmn",
            "Scene",
            "BpmnCanvas2DSceneContributor.cs");
        var placementPolicySource = ReadProductionFile(
            "Inceptus.DiagramEngine.Bpmn",
            "Visuals",
            "BpmnNodeLabelPlacementPolicy.cs");
        var previewProviderSource = ReadProductionFile(
            "Inceptus.DiagramEngine.Bpmn",
            "Placement",
            "BpmnToolboxPlacementPreviewProvider.cs");

        Assert.Contains(nameof(NodeLabelPlacement), projectionSource, StringComparison.Ordinal);
        Assert.Contains("BpmnNodeLabelPlacementPolicy.OutsideBelow", registrationSource,
            StringComparison.Ordinal);
        Assert.Contains("NodeLabelPlacementKind.OutsideBelow", placementPolicySource,
            StringComparison.Ordinal);
        Assert.Contains("BpmnNodeLabelPlacementPolicy.Resolve", previewProviderSource,
            StringComparison.Ordinal);
        var placementSources = projectionSource + registrationSource + placementPolicySource +
            previewProviderSource + contributorSource;
        Assert.DoesNotContain("MeasureText", placementSources, StringComparison.Ordinal);
        Assert.DoesNotContain("Wrap", placementSources,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Canvas2DSceneGeometry.Text", placementSources,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Approved?", contributorSource, StringComparison.Ordinal);
    }

    private static string ReadProductionDirectory(
        string project,
        string directory,
        string pattern)
    {
        var path = Path.Combine(RepositoryRoot, "src", project, directory);
        return string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(path, pattern, SearchOption.AllDirectories)
                .Order(StringComparer.Ordinal)
                .Select(File.ReadAllText));
    }

    private static string ReadProductionFile(
        string project,
        string directory,
        string fileName) =>
        File.ReadAllText(Path.Combine(RepositoryRoot, "src", project, directory, fileName));

    private static string RepositoryRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Inceptus.DiagramEngine.sln")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate the repository root.");
        }
    }
}
