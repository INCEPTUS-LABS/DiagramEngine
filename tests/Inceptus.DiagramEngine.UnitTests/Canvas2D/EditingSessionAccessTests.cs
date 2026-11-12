using System.Collections.Concurrent;
using System.Collections.Immutable;
using Inceptus.DiagramEngine.Canvas2D.EditingSession;
using Inceptus.DiagramEngine.Canvas2D.Scene;
using Inceptus.DiagramEngine.Contracts.Canvas2D;
using Inceptus.DiagramEngine.Contracts.Commands;
using Inceptus.DiagramEngine.Contracts.ConnectionCreation;
using Inceptus.DiagramEngine.Contracts.Diagnostics;
using Inceptus.DiagramEngine.Contracts.Documents;
using Inceptus.DiagramEngine.Contracts.EditorState;
using Inceptus.DiagramEngine.Contracts.Geometry;
using Inceptus.DiagramEngine.Contracts.History;
using Inceptus.DiagramEngine.Contracts.Presentation;
using Inceptus.DiagramEngine.Contracts.Primitives;
using Inceptus.DiagramEngine.Contracts.Visuals;
using Inceptus.DiagramEngine.Runtime.Commands;
using Inceptus.DiagramEngine.Runtime.Documents;

namespace Inceptus.DiagramEngine.UnitTests.Canvas2D;

public sealed class EditingSessionAccessTests
{
    [Theory]
    [InlineData(Submission.Direct, true)]
    [InlineData(Submission.SelectedOverload, true)]
    [InlineData(Submission.SingleSelection, true)]
    [InlineData(Submission.SelectedMember, true)]
    [InlineData(Submission.SemanticSelection, true)]
    [InlineData(Submission.SceneTarget, true)]
    [InlineData(Submission.PersistentGesture, true)]
    [InlineData(Submission.ElementConnection, true)]
    [InlineData(Submission.Direct, false)]
    [InlineData(Submission.SelectedOverload, false)]
    [InlineData(Submission.SingleSelection, false)]
    [InlineData(Submission.SelectedMember, false)]
    [InlineData(Submission.SemanticSelection, false)]
    [InlineData(Submission.SceneTarget, false)]
    [InlineData(Submission.PersistentGesture, false)]
    [InlineData(Submission.ElementConnection, false)]
    public async Task RegisteredCommandCannotBypassAnyReadOnlySubmission(
        Submission submission, bool readOnly)
    {
        var inputs = Canvas2DSceneTestData.Create();
        var document = EditingSessionTestHarness.CreateDocument(inputs);
        var snapshot = document.CaptureSnapshot();
        var first = inputs.Graph.Nodes[0];
        var visualId = first.Source.VisualStateId!;
        var initial = submission == Submission.SemanticSelection
            ? new EditorStateSnapshot(semanticSceneSelection: first.Source.SemanticElementId)
            : new EditorStateSnapshot(selection: submission == Submission.SelectedMember
                ? inputs.Graph.Nodes.Select(node => node.Source.VisualStateId!) : [visualId]);
        var initialScene = CreateResult(inputs, snapshot, initial).Scene!;
        var source = Endpoint(initialScene, first.Id, document.SemanticModel.RootScopeId);
        var target = Endpoint(initialScene, inputs.Graph.Nodes[1].Id, document.SemanticModel.RootScopeId);
        if (submission == Submission.ElementConnection)
        {
            initial = ConnectionToolState.WithPendingSource(initial, new ElementConnectionPreview(
                snapshot.DocumentId, snapshot.Revision, initialScene.PresentationContext,
                new AnchorConnectionCreationId("test:access:connection"), source,
                new PointD(target.Bounds.X + target.Bounds.Width / 2d, target.Bounds.Y + target.Bounds.Height / 2d),
                ConnectorVisualDescriptor.Default));
        }

        if (submission == Submission.PersistentGesture)
        {
            initial = new EditorStateSnapshot(selection: [visualId], activeGesture:
                new EditorGestureSnapshot("test:access:gesture", "test:move", default, new PointD(20d, 20d)));
        }

        var probe = new CommandProbe();
        var pipeline = new ControlledEditingSessionPipeline();
        pipeline.EnqueueFull((current, editor, _) => ValueTask.FromResult(CreateResult(inputs, current, editor)));
        // Gesture completion retains the existing failed/no-change cleanup of transient previews.
        if (submission == Submission.PersistentGesture)
            pipeline.EnqueueScene((_, visuals, editor, _) => ValueTask.FromResult(CreateResult(inputs,
                new DocumentSnapshot(snapshot.SemanticModel, visuals, snapshot.Metadata), editor)));
        var configuration = Configuration(initial, probe);
        if (readOnly) configuration = configuration.WithAccessMode(EditingSessionAccessMode.ReadOnly);
        var (renderer, _) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        var attached = await EditingSession.AttachAsync(document, renderer, configuration, pipeline);
        await using var session = Assert.IsType<EditingSession>(attached.Session);
        var state = session.CaptureState();
        Assert.Equal(EditingSessionStatus.Ready, state.Status);
        var command = new ProbeCommand(snapshot.DocumentId, snapshot.Revision);

        var result = submission switch
        {
            Submission.Direct => await session.ExecuteAsync(command),
            Submission.SelectedOverload => await session.ExecuteAsync(command, visualId),
            Submission.SingleSelection => await session.ExecuteForSingleSelectionAsync(visualId, command),
            Submission.SelectedMember => await session.ExecuteForSelectedVisualStateAsync(visualId, command),
            Submission.SemanticSelection => await session.ExecuteForSemanticSceneSelectionAsync(
                first.Source.SemanticElementId, command),
            Submission.SceneTarget => await session.ExecuteForSceneTargetAsync(command, state.CurrentScene!,
                state.Generation, source.SceneId, expectedPresentation: null),
            Submission.PersistentGesture => await session.CompletePersistentGestureAsync(state.CurrentScene!,
                state.Generation, state.EditorState, new EditorStateSnapshot(selection: [visualId]), command),
            Submission.ElementConnection => await session.CompleteElementConnectionAsync(state, source, target,
                command, CancellationToken.None),
            _ => throw new ArgumentOutOfRangeException(nameof(submission)),
        };
        await session.WaitForIdleAsync();
        await CommandProcessor.WaitForEventDispatchIdleAsync(document);

        if (readOnly)
        {
            AssertReadOnly(result);
            Assert.Equal(0, probe.EnvelopeCalls);
            Assert.Equal(0, probe.ValidationCalls);
            Assert.Equal(0, probe.HandlerCalls);
        }
        else
        {
            // Proves every test request passes its targeting preconditions and reaches the handler
            // through the unchanged default configuration, rather than passing on an unrelated error.
            Assert.Equal(HistoryOperationStatus.NoChange, result.Status);
            Assert.True(probe.EnvelopeCalls > 0);
            Assert.Equal(1, probe.HandlerCalls);
            Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == EditingSessionDiagnosticCodes.ReadOnly);
        }

        Assert.Equal(0, probe.HistoryCalls);
        Assert.Empty(probe.Events);
        AssertUnchanged(snapshot, state.HistoryStatus, document, session);
        Assert.Equal(1, pipeline.DocumentRunCount);
        Assert.Equal(submission == Submission.PersistentGesture ? 1 : 0, pipeline.SceneRebuildCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UndoAndRedoAreDeniedBeforeHistoryEvenWhenRuntimeFaulted(bool runtimeFaulted)
    {
        var inputs = Canvas2DSceneTestData.Create();
        var document = EditingSessionTestHarness.CreateDocument(inputs);
        var snapshot = document.CaptureSnapshot();
        var probe = new CommandProbe();
        var pipeline = new ControlledEditingSessionPipeline();
        pipeline.EnqueueFull(runtimeFaulted
            ? ControlledEditingSessionPipeline.Failure("TEST_ACCESS_PIPELINE_FAILURE")
            : CreateResult(inputs, snapshot, EditorStateSnapshot.Empty));
        var (renderer, _) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        var attached = await EditingSession.AttachAsync(document, renderer,
            Configuration(EditorStateSnapshot.Empty, probe).WithAccessMode(EditingSessionAccessMode.ReadOnly), pipeline);
        await using var session = Assert.IsType<EditingSession>(attached.Session);
        var before = session.CaptureState();
        Assert.Equal(runtimeFaulted ? EditingSessionStatus.RuntimeFaulted : EditingSessionStatus.Ready, before.Status);

        var undo = await session.UndoAsync();
        var redo = await session.RedoAsync();

        // Empty History ordinarily returns NothingToUndo/NothingToRedo. Access denial must win.
        AssertReadOnly(undo);
        AssertReadOnly(redo);
        AssertUnchanged(snapshot, before.HistoryStatus, document, session);
        Assert.Equal(before.Status, session.CaptureState().Status);
        Assert.Equal(1, pipeline.DocumentRunCount);
        Assert.Equal(0, pipeline.SceneRebuildCount);
        Assert.Empty(probe.Events);
    }

    [Fact]
    public async Task CancelledAndClosedRequestsKeepTheirExistingFailureSemantics()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var document = EditingSessionTestHarness.CreateDocument(inputs);
        var snapshot = document.CaptureSnapshot();
        var probe = new CommandProbe();
        var pipeline = new ControlledEditingSessionPipeline();
        pipeline.EnqueueFull(CreateResult(inputs, snapshot, EditorStateSnapshot.Empty));
        var (renderer, _) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        var attached = await EditingSession.AttachAsync(document, renderer,
            Configuration(EditorStateSnapshot.Empty, probe).WithAccessMode(EditingSessionAccessMode.ReadOnly), pipeline);
        await using var session = Assert.IsType<EditingSession>(attached.Session);
        var command = new ProbeCommand(snapshot.DocumentId, snapshot.Revision);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        foreach (var result in new[]
        {
            await session.ExecuteAsync(command, cancellation.Token),
            await session.UndoAsync(cancellation.Token),
            await session.RedoAsync(cancellation.Token),
        })
        {
            Assert.Equal(HistoryOperationStatus.Cancelled, result.Status);
            Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == EditingSessionDiagnosticCodes.ReadOnly);
        }

        await session.CloseAsync();
        foreach (var result in new[] { await session.ExecuteAsync(command), await session.UndoAsync(), await session.RedoAsync() })
        {
            Assert.False(result.IsCommitted);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == EditingSessionDiagnosticCodes.Closed);
            Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == EditingSessionDiagnosticCodes.ReadOnly);
        }

        Assert.Equal(0, probe.HandlerCalls);
        Assert.Empty(probe.Events);
        Assert.Equal(snapshot.Revision, session.CaptureState().DocumentRevision);
        Assert.Equal(1, pipeline.DocumentRunCount);
    }

    private static void AssertReadOnly(HistoryOperationResult result)
    {
        Assert.Equal(HistoryOperationStatus.CommandFailed, result.Status);
        Assert.False(result.IsCommitted);
        Assert.False(result.IsApplied);
        Assert.Null(result.CommittedRevision);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == EditingSessionDiagnosticCodes.ReadOnly && diagnostic.Severity == DiagnosticSeverity.Error);
    }

    private static void AssertUnchanged(DocumentSnapshot before, HistoryStatus history,
        Document document, EditingSession session)
    {
        var after = document.CaptureSnapshot();
        Assert.Same(before, after);
        Assert.Equal(before.SemanticModel, after.SemanticModel);
        Assert.Equal(before.VisualModel, after.VisualModel);
        Assert.Equal(before.Metadata, after.Metadata);
        Assert.Equal(before.Publication, after.Publication);
        Assert.Equal(before.Revision, session.CaptureState().DocumentRevision);
        Assert.Equal(history, session.CaptureState().HistoryStatus);
    }

    private static EditingSessionConfiguration Configuration(EditorStateSnapshot initial, CommandProbe probe)
    {
        var baseline = EditingSessionTestHarness.Configuration();
        return new EditingSessionConfiguration(baseline.ProjectionEngine, baseline.LayoutEngine,
            baseline.LayoutAlgorithmId, baseline.RoutingEngine, baseline.RoutingAlgorithmId,
            baseline.SceneBuilder, initialEditorState: initial,
            commandHandlers: [new(ProbeCommand.Id, probe, probe)],
            commandValidators: [new(ProbeCommand.Id, new CommandValidatorId("test:access:validator"), probe)],
            historyPolicies: [new(ProbeCommand.Id, probe)], documentChangedSubscribers: [probe]);
    }

    private static EditingSessionPipelineResult CreateResult(Canvas2DSceneTestData inputs,
        DocumentSnapshot snapshot, EditorStateSnapshot editor)
    {
        var artifacts = EditingSessionTestHarness.CreateArtifacts(inputs, snapshot.Revision);
        var plan = new PresentationViewPlan(snapshot.DocumentId, snapshot.Revision, artifacts.PresentationContext,
            snapshot.SemanticModel.Elements.Select(element => element.Id),
            snapshot.SemanticModel.Relationships.Select(relationship => relationship.Id));
        artifacts = artifacts with { PresentationPlan = plan };
        var source = EditingSessionTestHarness.CreateScene(artifacts, snapshot.VisualModel, editor);
        var items = source.Items;
        if (editor.SemanticSceneSelection is { } semanticId)
        {
            items = items.Add(new Canvas2DSceneItem(new SceneObjectId("test:access:semantic-header"),
                Canvas2DSceneLayer.Content, 0, Canvas2DSceneGeometry.Rectangle(new RectD(0d, 0d, 20d, 20d)),
                new Canvas2DSceneOriginTrace(Canvas2DSceneOriginCategory.SemanticElement, semanticId),
                hitTestPolicy: new Canvas2DHitTestPolicy(Canvas2DHitTestMode.Bounds),
                metadata: [Canvas2DSemanticSceneInteractionMetadata.Enabled]));
        }

        var scene = new Canvas2DScene(source.DocumentId, source.SourceRevision,
            source.LayoutAlgorithmId, source.RoutingAlgorithmId, source.Configuration, source.Contributors,
            source.Viewport, source.ViewportTransform, source.ActiveToolId, source.FocusTargetId,
            source.ToolState, source.ContributorMetadata, items, presentationContext: artifacts.PresentationContext,
            presentationPlan: plan);
        return EditingSessionPipelineResult.Success(artifacts, scene);
    }

    private static ElementConnectionEndpoint Endpoint(Canvas2DScene scene, ProjectedObjectId id, DocumentScopeId scope)
    {
        var item = scene.Items.Single(candidate => candidate.Id == Canvas2DSceneObjectIdentity.ForProjected(id, "node"));
        return new ElementConnectionEndpoint(item.Origin.SemanticElementId!, item.Origin.VisualStateId,
            id, item.Id, scope, item.Bounds);
    }

    public enum Submission
    {
        Direct,
        SelectedOverload,
        SingleSelection,
        SelectedMember,
        SemanticSelection,
        SceneTarget,
        PersistentGesture,
        ElementConnection,
    }

    private sealed record ProbeCommand(DocumentId TargetDocumentId, DocumentRevision ExpectedRevision) : ICommand
    {
        internal static CommandTypeId Id { get; } = new("test:access:future-plugin-command");
        public CommandTypeId TypeId => Id;
        public CommandCategory Category => CommandCategory.Metadata;
        public AuthoritativeDocumentComponent AffectedComponents => AuthoritativeDocumentComponent.Metadata;
    }

    private sealed class CommandProbe : ICommandEnvelopeValidator, ICommandValidator, ICommandHandler,
        ICommandHistoryPolicy, IDocumentChangedSubscriber
    {
        internal int EnvelopeCalls { get; private set; }
        internal int ValidationCalls { get; private set; }
        internal int HandlerCalls { get; private set; }
        internal int HistoryCalls { get; private set; }
        internal ConcurrentQueue<DocumentChangedEvent> Events { get; } = new();

        public ImmutableArray<Diagnostic> Validate(ICommand command)
        {
            EnvelopeCalls++;
            return [];
        }

        public ImmutableArray<Diagnostic> Validate(ICommand command, DocumentSnapshot document)
        {
            ValidationCalls++;
            return [];
        }

        public ValueTask<CommandHandlerResult> HandleAsync(ICommand command, DocumentSnapshot document,
            CancellationToken cancellationToken)
        {
            HandlerCalls++;
            return ValueTask.FromResult(CommandHandlerResult.NoChange());
        }

        public CommandHistoryPreparationResult Prepare(ICommand command, DocumentSnapshot before, DocumentSnapshot committed)
        {
            HistoryCalls++;
            return CommandHistoryPreparationResult.NotUndoable();
        }

        public ValueTask OnDocumentChangedAsync(DocumentChangedEvent change)
        {
            Events.Enqueue(change);
            return ValueTask.CompletedTask;
        }
    }
}
