using System.Reflection;
using Inceptus.DiagramEngine.Canvas2D.Interaction;
using Inceptus.DiagramEngine.Contracts.Commands;
using Inceptus.DiagramEngine.Contracts.Projection;
using Inceptus.DiagramEngine.Contracts.Visuals;

namespace Inceptus.DiagramEngine.UnitTests.Architecture;

public sealed class PhaseM323ManualNodeLabelArchitectureTests
{
    [Fact]
    public void ManualOverrideIsAVisualModelValueWithoutAbsoluteCoordinatesOrStyleScope()
    {
        Assert.Equal(
            typeof(VisualStateSnapshot).Assembly,
            typeof(NodeLabelVisualOverride).Assembly);
        Assert.Equal(1d, NodeLabelVisualOverride.MinimumWidth);
        Assert.Equal(1d, NodeLabelVisualOverride.MinimumHeight);

        var source = ReadProductionFile(
            "Inceptus.DiagramEngine.Contracts",
            "Visuals",
            "NodeLabelVisualOverride.cs");
        Assert.Contains("bounds center", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("OffsetX", source, StringComparison.Ordinal);
        Assert.Contains("OffsetY", source, StringComparison.Ordinal);
        Assert.Contains("Width", source, StringComparison.Ordinal);
        Assert.Contains("Height", source, StringComparison.Ordinal);
        Assert.DoesNotContain("LabelX", source, StringComparison.Ordinal);
        Assert.DoesNotContain("LabelY", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PlacementMode", source, StringComparison.Ordinal);
        Assert.DoesNotContain("FontSize", source, StringComparison.Ordinal);
        Assert.DoesNotContain("FontFamily", source, StringComparison.Ordinal);
        Assert.DoesNotContain("FontWeight", source, StringComparison.Ordinal);
    }

    [Fact]
    public void OneGenericVisualCommandOwnsMoveResizeAndResetPersistence()
    {
        var command = new UpdateNodeLabelVisualOverrideCommand(
            new("document"),
            new(4),
            new("visual"),
            targetOverride: null);
        Assert.Equal(CommandCategory.Visual, command.Category);
        Assert.Equal(
            AuthoritativeDocumentComponent.VisualModel,
            command.AffectedComponents);
        Assert.Null(command.TargetOverride);

        var publicCommands = typeof(ICommand).Assembly
            .GetExportedTypes()
            .Where(type => typeof(ICommand).IsAssignableFrom(type) &&
                type.Name.Contains("NodeLabel", StringComparison.Ordinal))
            .ToArray();
        Assert.Equal([typeof(UpdateNodeLabelVisualOverrideCommand)], publicCommands);

        var constructor = Assert.Single(typeof(UpdateNodeLabelVisualOverrideCommand)
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance));
        Assert.Equal(
            typeof(NodeLabelVisualOverride),
            Nullable.GetUnderlyingType(
                constructor.GetParameters().Single(parameter =>
                    parameter.Name == "targetOverride").ParameterType) ??
                constructor.GetParameters().Single(parameter =>
                    parameter.Name == "targetOverride").ParameterType);
    }

    [Fact]
    public void InteractionPolicyIsTypedSmallAndDefaultsToFixed()
    {
        Assert.Equal(
            [NodeLabelInteractionPolicy.Fixed, NodeLabelInteractionPolicy.MoveAndResize],
            Enum.GetValues<NodeLabelInteractionPolicy>());

        var constructor = Assert.Single(typeof(ProjectedLabel)
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance));
        var parameter = Assert.Single(constructor.GetParameters(), candidate =>
            candidate.Name == "nodeInteractionPolicy");
        Assert.Equal(typeof(NodeLabelInteractionPolicy), parameter.ParameterType);
        Assert.Equal(NodeLabelInteractionPolicy.Fixed, parameter.DefaultValue);
    }

    [Fact]
    public void GenericImplementationIsNotationNeutralAndHasNoParallelSelectionAuthority()
    {
        var genericSource = string.Concat(
            ReadProductionDirectory("Inceptus.DiagramEngine.Contracts", "*.cs"),
            ReadProductionDirectory("Inceptus.DiagramEngine.Runtime", "*.cs"),
            ReadProductionDirectory("Inceptus.DiagramEngine.Canvas2D", "*.cs"),
            ReadProductionDirectory(
                "Inceptus.DiagramEngine.Bpmn.Blazor",
                "Components",
                "*.razor"),
            ReadProductionDirectory(
                "Inceptus.DiagramEngine.Bpmn.Blazor",
                "Presentation",
                "*.cs",
                ApprovedN104Changes.IsPublishingApplicationCompositionPath));

        // P1.15 permits only the explicit Event exclusion in BPMN element Properties.
        Assert.Equal(1, genericSource.Split("!BpmnSemanticTypes.IsEvent(TypeId)", StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("BpmnSemanticTypes", genericSource.Replace(
            "!BpmnSemanticTypes.IsEvent(TypeId)", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.DoesNotContain("BpmnPluginRegistration", genericSource,
            StringComparison.Ordinal);
        Assert.DoesNotContain("CreateBpmn", genericSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Gateway", genericSource, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SelectedLabelId", genericSource, StringComparison.Ordinal);
        Assert.DoesNotContain("MoveBpmn", genericSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ResizeGateway", genericSource, StringComparison.Ordinal);
    }

    [Fact]
    public void PreviewSceneDoesNotExecuteCommandsAndLayoutRoutingRemainOverrideUnaware()
    {
        var scenePreviewSource = string.Concat(
            ReadProductionFile(
                "Inceptus.DiagramEngine.Canvas2D",
                "Scene",
                "Canvas2DSceneBuilder.Gestures.cs"),
            ReadProductionFile(
                "Inceptus.DiagramEngine.Canvas2D",
                "Scene",
                "Canvas2DSceneBuilder.TextLayout.cs"));
        Assert.DoesNotContain("ExecuteAsync", scenePreviewSource, StringComparison.Ordinal);
        Assert.DoesNotContain("CommandProcessor", scenePreviewSource, StringComparison.Ordinal);
        Assert.DoesNotContain(
            nameof(UpdateNodeLabelVisualOverrideCommand),
            scenePreviewSource,
            StringComparison.Ordinal);

        var layoutRoutingSource = string.Concat(
            ReadProductionDirectory(
                "Inceptus.DiagramEngine.Runtime",
                "Layout",
                "*.cs"),
            ReadProductionDirectory(
                "Inceptus.DiagramEngine.Runtime",
                "Routing",
                "*.cs"),
            ReadProductionDirectory(
                "Inceptus.DiagramEngine.Bpmn",
                "Layout",
                "*.cs"),
            ReadProductionDirectory(
                "Inceptus.DiagramEngine.Bpmn",
                "Routing",
                "*.cs"));
        Assert.DoesNotContain(
            nameof(NodeLabelVisualOverride),
            layoutRoutingSource,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ContextActionIsTransientAndRendererJavaScriptRemainUnaware()
    {
        Assert.Equal(
            typeof(Canvas2DInteractionController).Assembly,
            typeof(Canvas2DNodeLabelContextAction).Assembly);

        var rendererAndJavaScript = string.Concat(
            ReadProductionDirectory(
                "Inceptus.DiagramEngine.Canvas2D",
                "Rendering",
                "*.cs"),
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
        Assert.DoesNotContain(
            nameof(NodeLabelVisualOverride),
            rendererAndJavaScript,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            nameof(NodeLabelInteractionPolicy),
            rendererAndJavaScript,
            StringComparison.Ordinal);
    }

    private static string ReadProductionDirectory(string project, string pattern) =>
        ReadProductionDirectory(project, string.Empty, pattern);

    private static string ReadProductionDirectory(
        string project,
        string directory,
        string pattern,
        Func<string, bool>? excludedPath = null)
    {
        var path = Path.Combine(RepositoryRoot, "src", project, directory);
        return Directory.Exists(path)
            ? string.Join(
                Environment.NewLine,
                Directory.EnumerateFiles(path, pattern, SearchOption.AllDirectories)
                    .Where(candidate => excludedPath is null || !excludedPath(candidate))
                    .Order(StringComparer.Ordinal)
                    .Select(File.ReadAllText))
            : string.Empty;
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
