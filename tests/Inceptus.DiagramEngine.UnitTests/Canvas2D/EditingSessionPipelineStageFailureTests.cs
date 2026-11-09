using Inceptus.DiagramEngine.Blazor.Demo;
using Inceptus.DiagramEngine.Canvas2D.EditingSession;
using Inceptus.DiagramEngine.Canvas2D.Scene;
using Inceptus.DiagramEngine.Contracts.Canvas2D;
using Inceptus.DiagramEngine.Contracts.Diagnostics;
using Inceptus.DiagramEngine.Contracts.Documents;
using Inceptus.DiagramEngine.Contracts.Layout;
using Inceptus.DiagramEngine.Contracts.Primitives;
using Inceptus.DiagramEngine.Contracts.Projection;
using Inceptus.DiagramEngine.Contracts.Routing;
using Inceptus.DiagramEngine.Runtime.Layout;
using Inceptus.DiagramEngine.Runtime.Projection;
using Inceptus.DiagramEngine.Runtime.Routing;

namespace Inceptus.DiagramEngine.UnitTests.Canvas2D;

public sealed class EditingSessionPipelineStageFailureTests
{
    [Theory]
    [InlineData(PipelineStage.Projection, ProjectionDiagnosticCodes.UnsupportedSemanticType)]
    [InlineData(PipelineStage.Layout, LayoutDiagnosticCodes.MissingAlgorithm)]
    [InlineData(PipelineStage.Routing, RoutingDiagnosticCodes.MissingAlgorithm)]
    [InlineData(PipelineStage.RoutingPreparation, RoutingDiagnosticCodes.InputPreparationFailure)]
    [InlineData(PipelineStage.InvalidRoutingPreparation, RoutingDiagnosticCodes.InvalidInput)]
    [InlineData(PipelineStage.Scene, Canvas2DSceneDiagnosticCodes.ContributorFailure)]
    public async Task ConcretePipelineClassifiesEachStageFailureWithoutPartialRuntimeState(
        PipelineStage stage,
        string expectedDiagnosticCode)
    {
        var composition = NeutralDemoPipeline.CreateComposition();
        var before = composition.Document.CaptureSnapshot();
        var configuration = WithFailure(composition.Configuration, stage);
        var (renderer, _) = await EditingSessionTestHarness.CreateInitializedRendererAsync();

        var attachment = await EditingSession.AttachAsync(
            composition.Document,
            renderer,
            configuration);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);
        var state = session.CaptureState();

        Assert.Equal(EditingSessionAttachStatus.RuntimeFaulted, attachment.Status);
        Assert.Equal(EditingSessionStatus.RuntimeFaulted, state.Status);
        Assert.Null(state.CurrentScene);
        Assert.Null(state.LastKnownGoodScene);
        Assert.Null(state.ProjectedGraph);
        Assert.Null(state.LayoutResult);
        Assert.Null(state.RoutingResult);
        Assert.Contains(state.RuntimeDiagnostics, diagnostic =>
            diagnostic.Code == expectedDiagnosticCode);
        Assert.Equal(before, composition.Document.CaptureSnapshot());
    }

    private static EditingSessionConfiguration WithFailure(
        EditingSessionConfiguration source,
        PipelineStage stage)
    {
        var projectionEngine = stage == PipelineStage.Projection
            ? new ProjectionEngine()
            : source.ProjectionEngine;
        var layoutAlgorithmId = stage == PipelineStage.Layout
            ? new AlgorithmId("test:missing-layout")
            : source.LayoutAlgorithmId;
        var routingAlgorithmId = stage == PipelineStage.Routing
            ? new AlgorithmId("test:missing-routing")
            : source.RoutingAlgorithmId;
        var sceneBuilder = stage == PipelineStage.Scene
            ? new Canvas2DSceneBuilder(
                contributors:
                [
                new Canvas2DSceneContributorRegistration(
                    new Canvas2DSceneContributorDescriptor(
                        new Canvas2DSceneContributorId("test:failing-scene-contributor"),
                        "1"),
                    new FailingSceneContributor()),
                ])
            : source.SceneBuilder;

        return new EditingSessionConfiguration(
            projectionEngine,
            source.LayoutEngine,
            layoutAlgorithmId,
            source.RoutingEngine,
            routingAlgorithmId,
            sceneBuilder,
            source.ProjectionContext,
            source.LayoutContext,
            source.RoutingContext,
            source.InitialEditorState,
            routingInputPreparer: stage is PipelineStage.RoutingPreparation or PipelineStage.InvalidRoutingPreparation
                ? new FailingRoutingPreparer(stage == PipelineStage.RoutingPreparation)
                : null);
    }

    public enum PipelineStage
    {
        Projection,
        Layout,
        Routing,
        RoutingPreparation,
        InvalidRoutingPreparation,
        Scene,
    }

    private sealed class FailingRoutingPreparer(bool throws) : IRoutingInputPreparer
    {
        public PreparedRoutingInput? Prepare(DocumentSnapshot document, DocumentScopeId activeScopeId,
            ProjectedGraph graph, LayoutResult layout, CancellationToken cancellationToken) =>
            throws ? throw new InvalidOperationException("Test provider failed.") : new PreparedRoutingInput(graph, layout, []);
    }

    private sealed class FailingSceneContributor : ICanvas2DSceneContributor
    {
        public Canvas2DSceneContributionResult Contribute(
            Canvas2DSceneContributionContext context) =>
            Canvas2DSceneContributionResult.Failure(
            [
                new Diagnostic(
                    Canvas2DSceneDiagnosticCodes.ContributorFailure,
                    DiagnosticSeverity.Error,
                    "Test scene contribution failed."),
            ]);
    }
}
