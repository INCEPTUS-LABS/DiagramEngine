using Inceptus.DiagramEngine.Bpmn.Blazor;
using Inceptus.DiagramEngine.Bpmn.Blazor.Composition;
using Inceptus.DiagramEngine.Bpmn.Blazor.Presentation;
using Inceptus.DiagramEngine.Canvas2D.EditingSession;
using Inceptus.DiagramEngine.Canvas2D.Rendering;
using Inceptus.DiagramEngine.Contracts.EditorState;
using Inceptus.DiagramEngine.Runtime.Documents;

namespace Inceptus.DiagramEngine.IntegrationTests;

public sealed partial class PhaseN103NativeDocumentPresentationIntegrationTests
{
    [Fact]
    public async Task Stage3PublicReadOnlyNativeAttachmentSelectionNavigationAndReplacementAreExact()
    {
        var (source, unassigned) = await CreateSourceDocumentAsync();
        var expected = source.CaptureSnapshot();
        var bytes = NativeDocumentSerializer.Export(expected);
        var factory = new BpmnModelerCompositionFactory(initialDocument: expected, accessMode: EditingSessionAccessMode.ReadOnly);
        var notifications = new List<object>();
        using var facade = new BpmnModelerFacade(factory, value => { notifications.Add(value); return Task.CompletedTask; });
        await using var host = new DocumentCanvasHost(factory,
            CreateRenderer(new PhaseM31BpmnPropertiesIntegrationTests.RecordingRenderExecution()),
            new FixedSurfaceObserverFactory(new Canvas2DSurfaceSize(1600, 1000, 1)),
            replacementRendererFactory: () => CreateRenderer(new PhaseM31BpmnPropertiesIntegrationTests.RecordingRenderExecution()));
        facade.Attach(host);
        await host.InitializeAsync("stage3-active", "stage3-standby", "stage3-container");
#pragma warning disable CA1859 // Exercise the two supported public interfaces independently.
        IBpmnDiagramPresentation presentation = facade;
        IBpmnDocumentEditor lifecycle = facade;
#pragma warning restore CA1859
        var session = Session(host);
        var history = session.CaptureState().HistoryStatus;
        var initial = presentation.CapturePresentation();
        Assert.True(initial.IsAvailable);
        Assert.Equal(expected, lifecycle.GetDocument().Snapshot);
        var visual = expected.VisualModel.VisualStates.Single(item => item.SemanticElementId == unassigned);
        var selection = await presentation.SelectVisualAsync(initial.Stamp!, visual.Id);
        Assert.True(selection.Succeeded, Diagnostics(selection.Diagnostics));
        Assert.Equal(unassigned, Assert.Single(selection.Presentation.Selection).SemanticId);
        Assert.NotNull(selection.Presentation.PrimaryPart);
        Assert.Single(notifications.OfType<BpmnSelectionChangedEventArgs>());
        Assert.True((await session.UpdateEditorStateAsync(new(semanticSceneSelection: PoolAId,
            viewport: session.CaptureState().EditorState.Viewport))).Succeeded);
        await session.WaitForIdleAsync();
        var pool = Assert.Single(presentation.CapturePresentation().Selection);
        Assert.Equal(PoolAId, pool.SemanticId);
        Assert.Null(pool.VisualStateId);
        Assert.NotEmpty(pool.Parts);
        foreach (var scope in initial.AvailableScopes)
        {
            var navigation = await presentation.NavigateToScopeAsync(presentation.CapturePresentation().Stamp!, scope.Id);
            Assert.True(navigation.Succeeded, Diagnostics(navigation.Diagnostics));
            Assert.Equal(scope.Id, navigation.Presentation.ActiveScopeId);
            Assert.Equal(expected, lifecycle.GetDocument().Snapshot);
            Assert.Equal(history, session.CaptureState().HistoryStatus);
            Assert.True(bytes.AsSpan().SequenceEqual(NativeDocumentSerializer.Export(lifecycle.GetDocument().Snapshot!).AsSpan()));
        }
        var stale = presentation.CapturePresentation().Stamp!;
        var replacement = await lifecycle.SetDocumentAsync(expected, new BpmnModelerDocumentLoadOptions { VerifySavedRouting = true });
        Assert.True(replacement.Succeeded, Diagnostics(replacement.Diagnostics));
        Assert.NotSame(session, Session(host));
        Assert.True(session.CaptureState().IsClosed);
        Assert.Equal(EditingSessionAccessMode.ReadOnly, Session(host).CaptureState().AccessMode);
        Assert.Equal(BpmnModelerOperationStatus.Rejected,
            (await presentation.SelectVisualAsync(stale, visual.Id)).Status);
        Assert.True(bytes.AsSpan().SequenceEqual(NativeDocumentSerializer.Export(replacement.Snapshot!).AsSpan()));
        Assert.Equal(0, Session(host).CaptureState().HistoryStatus.EntryCount);
        Assert.Single(notifications.OfType<BpmnModelerDocumentChangedEventArgs>(),
            change => change.Kind == BpmnModelerDocumentChangeKind.DocumentReplacement);
    }
}
