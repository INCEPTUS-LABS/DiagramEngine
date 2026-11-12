using System.Text.RegularExpressions;
using Inceptus.DiagramEngine.Canvas2D.EditingSession;
using Inceptus.DiagramEngine.Contracts.Commands;
using Inceptus.DiagramEngine.Contracts.Documents;
using Inceptus.DiagramEngine.Contracts.EditorState;
using Inceptus.DiagramEngine.Contracts.Geometry;

namespace Inceptus.DiagramEngine.UnitTests.Architecture;

public sealed class PhaseN41InteractiveViewportPanningArchitectureTests
{
    [Fact]
    public void PanIsOneGenericRuntimeOnlyEditingSessionOperationAndNotACommand()
    {
        var method = typeof(EditingSession).GetMethod(nameof(EditingSession.PanViewportAsync));
        Assert.NotNull(method);
        Assert.Equal(typeof(VectorD), method!.GetParameters()[0].ParameterType);
        Assert.DoesNotContain(method.GetParameters(), parameter =>
            typeof(ICommand).IsAssignableFrom(parameter.ParameterType));

        var viewport = ReadProductionFile(
            "Inceptus.DiagramEngine.Canvas2D",
            "EditingSession",
            "EditingSession.Viewport.cs");
        Assert.Contains("current.Pan + canvasTranslation", viewport, StringComparison.Ordinal);
        Assert.DoesNotContain("CommandProcessor", viewport, StringComparison.Ordinal);
        Assert.DoesNotContain("AttachedDocument.Replace", viewport, StringComparison.Ordinal);
        Assert.DoesNotContain("History", viewport, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(DocumentSnapshot), viewport, StringComparison.Ordinal);
    }

    [Fact]
    public void BrowserBoundaryOwnsOnlyScalarCanvasInputAndNoTransformOrSpaceMode()
    {
        var javascript = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "src",
            "Inceptus.DiagramEngine.Bpmn.Blazor",
            "wwwroot",
            "inceptus.presentation.js"));

        Assert.Contains("addEventListener(\"wheel\", this.#onWheel, { passive: false })",
            javascript, StringComparison.Ordinal);
        Assert.Contains("event.ctrlKey || event.metaKey", javascript, StringComparison.Ordinal);
        Assert.Contains("event.preventDefault", javascript, StringComparison.Ordinal);
        Assert.Contains("OnCanvasWheelInput", javascript, StringComparison.Ordinal);
        // Stage 4 permits only opt-in browser-default suppression for the managed
        // context-menu shortcut. It does not add a keyboard pan mode or JS action authority.
        var contextMenuKey = Between(javascript, "this.#onKeyDown = event => {", "        };");
        const string expectedContextMenuKey = """
            this.#onKeyDown = event => {
                if (this.#canvas.dataset.applicationActionsEnabled === "true" &&
                    (event.key === "ContextMenu" || event.key === "F10" && event.shiftKey) &&
                    !event.ctrlKey && !event.altKey && !event.metaKey) event.preventDefault();
            """;
        Assert.Equal(Regex.Replace(expectedContextMenuKey, @"\s+", string.Empty),
            Regex.Replace(contextMenuKey, @"\s+", string.Empty));
        const string addContextMenuKey = "this.#canvas.addEventListener(\"keydown\", this.#onKeyDown);";
        const string removeContextMenuKey = "this.#canvas.removeEventListener(\"keydown\", this.#onKeyDown);";
        Assert.Contains(addContextMenuKey, javascript, StringComparison.Ordinal);
        Assert.Contains(removeContextMenuKey, javascript, StringComparison.Ordinal);
        var otherInput = javascript.Replace(contextMenuKey, string.Empty, StringComparison.Ordinal)
            .Replace(addContextMenuKey, string.Empty, StringComparison.Ordinal)
            .Replace(removeContextMenuKey, string.Empty, StringComparison.Ordinal)
            .Replace("#onKeyDown;", string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain("keydown", otherInput, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Space", javascript, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("matrix", javascript, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("inverse", javascript, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("documentPoint", javascript, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("zoom", javascript, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MiddlePanStructurallyPrecedesDiagramGestureDispatch()
    {
        var host = ReadProductionFile(
            "Inceptus.DiagramEngine.Bpmn.Blazor",
            "Presentation",
            "DocumentCanvasHost.cs");
        var middle = host.IndexOf(
            "TryHandleViewportPanPointerUnderGateAsync(",
            StringComparison.Ordinal);
        var diagram = host.IndexOf(
            "controller.PointerPressedAsync(",
            StringComparison.Ordinal);

        Assert.True(middle >= 0 && diagram > middle);
        Assert.Contains("input.Button == MiddleMouseButton", host, StringComparison.Ordinal);
        Assert.Contains("state.EditorState.ActiveGesture is not null", host,
            StringComparison.Ordinal);
        Assert.Contains("\"grabbing\"", host, StringComparison.Ordinal);
    }

    [Fact]
    public void PanUsesCentralOriginNormalizationAndPreservesNotationNeutralTransformAuthority()
    {
        var host = ReadProductionFile(
            "Inceptus.DiagramEngine.Bpmn.Blazor",
            "Presentation",
            "DocumentCanvasHost.cs");
        var viewport = ReadProductionFile(
            "Inceptus.DiagramEngine.Canvas2D",
            "EditingSession",
            "EditingSession.Viewport.cs");
        var panSource = Between(
            host,
            "private async Task OnWheelInputAsync(",
            "private bool TryConsumePlacementPointerBoundary(") + viewport;
        string[] forbidden =
        [
            "Bpmn",
            "SequenceFlow",
            "MessageCatchEvent",
            "DocumentGeometryBoundary",
            "MinimumX",
            "MinimumY",
        ];
        Assert.All(forbidden, token =>
            Assert.DoesNotContain(token, panSource, StringComparison.OrdinalIgnoreCase));

        Assert.Contains("Canvas2DViewportNormalizer.Normalize", viewport,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Matrix2D", viewport, StringComparison.Ordinal);
        var normalizer = ReadProductionFile("Inceptus.DiagramEngine.Canvas2D", "Scene", "Canvas2DViewportNormalizer.cs");
        Assert.Contains("Canvas2DSceneBuilder.CalculateVisibleDocumentRegion", normalizer, StringComparison.Ordinal);
        Assert.Contains("DocumentGeometryBoundary.MinimumX", normalizer, StringComparison.Ordinal);
        Assert.Contains("DocumentGeometryBoundary.MinimumY", normalizer, StringComparison.Ordinal);
        Assert.Contains("TransformVector", normalizer, StringComparison.Ordinal);
        Assert.DoesNotContain("VisualModel", normalizer, StringComparison.Ordinal);
        Assert.DoesNotContain("Bpmn", normalizer, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CanvasOnlyListenerCannotOwnToolboxPropertiesOrContextMenuInput()
    {
        var javascript = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "src",
            "Inceptus.DiagramEngine.Bpmn.Blazor",
            "wwwroot",
            "inceptus.presentation.js"));
        var razor = ReadProductionFile(
            "Inceptus.DiagramEngine.Bpmn.Blazor",
            "Components",
            "DocumentCanvas.razor");

        Assert.Contains("this.#canvas.addEventListener(\"wheel\"", javascript,
            StringComparison.Ordinal);
        Assert.DoesNotContain("document.addEventListener(\"wheel\"", javascript,
            StringComparison.Ordinal);
        Assert.DoesNotContain("window.addEventListener(\"wheel\"", javascript,
            StringComparison.Ordinal);
        Assert.Contains("<ToolboxPanel", razor, StringComparison.Ordinal);
        Assert.Contains("<canvas", razor, StringComparison.Ordinal);
        var contextMenu = Between(razor, "@if (_state?.ContextMenu is { } contextMenu", "@if (_modelViewPropertiesDraft is { } modelViewDraft)");
        Assert.Contains("class=\"@(contextMenu.ApplicationContext is null ? \"object-context-menu\" : \"object-context-menu application-context-menu\")\"",
            contextMenu, StringComparison.Ordinal);
        Assert.Contains("@onwheel:stopPropagation", contextMenu, StringComparison.Ordinal);
        Assert.Contains("@onpointerdown:stopPropagation", contextMenu, StringComparison.Ordinal);
        Assert.Contains("@onpointerup:stopPropagation", contextMenu, StringComparison.Ordinal);
        Assert.Contains("@onpointermove:stopPropagation", contextMenu, StringComparison.Ordinal);
        Assert.Contains("class=\"properties-form ", razor, StringComparison.Ordinal);
    }

    [Fact]
    public void ViewportRemainsOutsidePersistentDocumentContracts()
    {
        var contracts = ReadProductionProject("Inceptus.DiagramEngine.Contracts");
        var persistent = string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(
                    Path.Combine(RepositoryRoot, "src", "Inceptus.DiagramEngine.Contracts"),
                    "*.cs",
                    SearchOption.AllDirectories)
                .Where(path =>
                    path.Contains($"{Path.DirectorySeparatorChar}Documents{Path.DirectorySeparatorChar}",
                        StringComparison.Ordinal) ||
                    path.Contains($"{Path.DirectorySeparatorChar}Visuals{Path.DirectorySeparatorChar}",
                        StringComparison.Ordinal) ||
                    path.Contains($"{Path.DirectorySeparatorChar}Semantics{Path.DirectorySeparatorChar}",
                        StringComparison.Ordinal))
                .Order(StringComparer.Ordinal)
                .Select(File.ReadAllText));

        Assert.Contains(nameof(ViewportSnapshot), contracts, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(ViewportSnapshot), persistent, StringComparison.Ordinal);
        Assert.DoesNotContain("PanViewport", persistent, StringComparison.Ordinal);
    }

    private static string ReadProductionProject(string project) =>
        string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(
                    Path.Combine(RepositoryRoot, "src", project),
                    "*.cs",
                    SearchOption.AllDirectories)
                .Order(StringComparer.Ordinal)
                .Select(File.ReadAllText));

    private static string ReadProductionFile(
        string project,
        string directory,
        string fileName) =>
        File.ReadAllText(Path.Combine(RepositoryRoot, "src", project, directory, fileName));

    private static string Between(string source, string start, string end)
    {
        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        var endIndex = source.IndexOf(end, startIndex, StringComparison.Ordinal);
        Assert.True(startIndex >= 0 && endIndex > startIndex);
        return source[startIndex..endIndex];
    }

    private static string RepositoryRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName,
                        "Inceptus.DiagramEngine.sln")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate the repository root.");
        }
    }
}
