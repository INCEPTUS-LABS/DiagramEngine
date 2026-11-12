using System.Collections.Concurrent;
using System.Collections.Immutable;
using Inceptus.DiagramEngine.Blazor.Demo;
using Inceptus.DiagramEngine.Bpmn;
using Inceptus.DiagramEngine.Bpmn.Semantics;
using Inceptus.DiagramEngine.Canvas2D.EditingSession;
using Inceptus.DiagramEngine.Canvas2D.Rendering;
using Inceptus.DiagramEngine.Contracts.Commands;
using Inceptus.DiagramEngine.Contracts.Geometry;
using Inceptus.DiagramEngine.Contracts.History;
using Inceptus.DiagramEngine.Contracts.Layout;
using Inceptus.DiagramEngine.Contracts.Primitives;
using Inceptus.DiagramEngine.Contracts.Projection;
using Inceptus.DiagramEngine.Contracts.Routing;
using Inceptus.DiagramEngine.Runtime.Commands;
using Inceptus.DiagramEngine.Runtime.Documents;
using Inceptus.DiagramEngine.Runtime.Routing;

namespace Inceptus.DiagramEngine.IntegrationTests;

public sealed class EditingSessionAccessIntegrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AccessPolicyControlsPersistentEditsThroughTheRealBpmnPipeline(bool readOnly)
    {
        var composition = await BpmnModelerTestComposition.DemoFactory.CreateAsync();
        var execution = new PhaseM31BpmnPropertiesIntegrationTests.RecordingRenderExecution();
        await using var renderer = new Canvas2DRenderer(execution,
            new Canvas2DRendererConfiguration(
                fontResources:
                [
                    new Canvas2DFontResource("org.dejavu.DejaVuSans", "2.37", "DejaVu Sans",
                        "fonts/DejaVuSans-2.37.ttf"),
                ],
                defaultFontFamily: "DejaVu Sans"));
        Assert.True((await renderer.InitializeAsync("session-access", new Canvas2DSurfaceSize(1400d, 900d, 1d))).Succeeded);
        var document = await BpmnModelerTestComposition.PrepareFreshDocumentAsync(
            composition.Document, composition.Configuration, renderer);
        var events = new RecordingSubscriber();
        var source = composition.Configuration;
        var routingRegistration = Assert.Single(BpmnPluginRegistration.N100.RoutingAlgorithms,
            item => item.AlgorithmId == source.RoutingAlgorithmId);
        var routingWork = new CountingRoutingAlgorithm(routingRegistration.Algorithm);
        var configuration = new EditingSessionConfiguration(
            source.ProjectionEngine, source.LayoutEngine, source.LayoutAlgorithmId,
            new RoutingEngine([new RoutingAlgorithmRegistration(source.RoutingAlgorithmId, routingWork)]),
            source.RoutingAlgorithmId, source.SceneBuilder,
            source.ProjectionContext, source.LayoutContext, source.RoutingContext, source.InitialEditorState,
            source.CommandHandlers, source.CommandValidators, source.HistoryPolicies,
            source.DocumentChangedSubscribers.Append(events), source.ConnectorAnchorPolicyProvider,
            source.ModelProfileCatalog, source.InitialModelProfileViewState, source.RoutingInputPreparer,
            source.GridSettings).WithAccessMode(readOnly ? EditingSessionAccessMode.ReadOnly : EditingSessionAccessMode.Editable);
        var attached = await EditingSession.AttachAsync(document, renderer, configuration);
        Assert.Equal(EditingSessionAttachStatus.Ready, attached.Status);
        await using var session = Assert.IsType<EditingSession>(attached.Session);
        var original = document.CaptureSnapshot();
        var originalBytes = NativeDocumentSerializer.Export(document);
        var initialState = session.CaptureState();
        var initialMeasurements = execution.MeasurementRequests.Count;
        var initialRoutingCalls = routingWork.CallCount;
        Assert.NotNull(original.VisualModel.RoutingScopes);
        Assert.NotEmpty(initialState.CurrentScene!.Items);
        var visual = Assert.Single(document.VisualModel.VisualStates,
            item => item.Id == BpmnDemoPipeline.TaskVisualId);
        Func<ICommand>[] createCommands =
        [
            () => new MoveVisualStateCommand(document.DocumentId, document.Revision, visual.Id,
                new PointD(visual.Position.X + 20d, visual.Position.Y + 10d)),
            () => new UpdateSemanticElementNameCommand(document.DocumentId, document.Revision,
                BpmnDemoPipeline.TaskId, BpmnSemanticProperties.Name, "Session access review"),
            () => new UpdateDocumentPublicationCommand(document.DocumentId, document.Revision,
                "session-access", "Session access review", "Persistent metadata regression"),
        ];

        foreach (var createCommand in createCommands)
        {
            var before = session.CaptureState();
            var result = await session.ExecuteAsync(createCommand());
            await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
            if (readOnly)
            {
                AssertDenied(result);
                Assert.Same(original, document.CaptureSnapshot());
                Assert.Equal(before.HistoryStatus, session.CaptureState().HistoryStatus);
                Assert.Same(initialState.CurrentScene, session.CaptureState().CurrentScene);
                Assert.Equal(initialMeasurements, execution.MeasurementRequests.Count);
                Assert.Equal(initialRoutingCalls, routingWork.CallCount);
                Assert.True(originalBytes.AsSpan().SequenceEqual(NativeDocumentSerializer.Export(document).AsSpan()));
            }
            else
            {
                Assert.True(result.IsCommitted, Diagnostics(result));
                Assert.Equal(before.DocumentRevision.Value + 1, document.Revision.Value);
                Assert.Equal(before.HistoryStatus.EntryCount + 1, session.CaptureState().HistoryStatus.EntryCount);
                Assert.Equal(EditingSessionStatus.Ready, session.CaptureState().Status);
            }
        }

        var beforeRestoration = document.CaptureSnapshot();
        var undo = await session.UndoAsync();
        await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        var redo = await session.RedoAsync();
        await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        await CommandProcessor.WaitForEventDispatchIdleAsync(document).WaitAsync(TimeSpan.FromSeconds(10));

        if (readOnly)
        {
            AssertDenied(undo);
            AssertDenied(redo);
            Assert.Same(original, document.CaptureSnapshot());
            Assert.Equal(initialState.HistoryStatus, session.CaptureState().HistoryStatus);
            Assert.Empty(events.Events);
        }
        else
        {
            Assert.True(undo.IsCommitted, Diagnostics(undo));
            Assert.True(redo.IsCommitted, Diagnostics(redo));
            Assert.Equal(beforeRestoration.Publication, document.Publication);
            Assert.Equal(beforeRestoration.Revision.Value + 2, document.Revision.Value);
            Assert.Equal(5, events.Events.Count);
            Assert.True(routingWork.CallCount > initialRoutingCalls,
                "The editable control must exercise saved-route assessment or preparation.");
        }
    }

    private static void AssertDenied(HistoryOperationResult result)
    {
        Assert.False(result.IsCommitted);
        Assert.False(result.IsApplied);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == EditingSessionDiagnosticCodes.ReadOnly);
    }

    private static string Diagnostics(HistoryOperationResult result) =>
        string.Join("; ", result.Diagnostics.Select(diagnostic => diagnostic.Message));

    private sealed class RecordingSubscriber : IDocumentChangedSubscriber
    {
        internal ConcurrentQueue<DocumentChangedEvent> Events { get; } = new();

        public ValueTask OnDocumentChangedAsync(DocumentChangedEvent change)
        {
            Events.Enqueue(change);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class CountingRoutingAlgorithm(IRoutingAlgorithm inner) : IRoutingAlgorithm, IStableConnectorRoutingPolicy
    {
        private readonly IStableConnectorRoutingPolicy _stable = Assert.IsAssignableFrom<IStableConnectorRoutingPolicy>(inner);

        internal int CallCount { get; private set; }

        public RoutingAlgorithmResult Route(ProjectedGraph graph, LayoutResult layout, RoutingContext context,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return inner.Route(graph, layout, context, cancellationToken);
        }

        public ConnectorRoutingAssessment Assess(ProjectedGraph graph, LayoutResult logicalLayout,
            RoutingContext context, ProjectedObjectId edgeId, ConnectorRoutingRecord? candidate,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return _stable.Assess(graph, logicalLayout, context, edgeId, candidate, cancellationToken);
        }

        public ConnectorRoutingWorkResult RouteAffected(ProjectedGraph graph, LayoutResult logicalLayout,
            RoutingContext context, ImmutableArray<ProjectedObjectId> orderedEdgeIds,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return _stable.RouteAffected(graph, logicalLayout, context, orderedEdgeIds, cancellationToken);
        }
    }
}
