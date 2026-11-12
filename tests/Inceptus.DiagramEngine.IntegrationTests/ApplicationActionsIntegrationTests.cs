using System.Collections.Concurrent;
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
    [Theory]
    [InlineData(EditingSessionAccessMode.ReadOnly)]
    [InlineData(EditingSessionAccessMode.Editable)]
    public async Task Stage4ProductionCompositionApplicationActionsPreserveNativeDocumentAndHistory(EditingSessionAccessMode mode)
    {
        var (source, unassigned) = await CreateSourceDocumentAsync();
        var expected = source.CaptureSnapshot();
        var bytes = NativeDocumentSerializer.Export(expected);
        var factory = new BpmnModelerCompositionFactory(initialDocument: expected, accessMode: mode);
        var invocations = new ConcurrentQueue<BpmnApplicationActionInvocationEventArgs>();
        var notifications = new ConcurrentQueue<object>();
        using var facade = new BpmnModelerFacade(factory, value =>
        {
            notifications.Enqueue(value);
            if (value is BpmnApplicationActionDispatch dispatch && dispatch.TryBeginInvocation(out var invocation))
            {
                invocations.Enqueue(invocation!);
                dispatch.Complete();
            }
            return Task.CompletedTask;
        });
        await using var host = new DocumentCanvasHost(factory,
            CreateRenderer(new PhaseM31BpmnPropertiesIntegrationTests.RecordingRenderExecution()),
            new FixedSurfaceObserverFactory(new Canvas2DSurfaceSize(1600, 1000, 1)),
            replacementRendererFactory: () => CreateRenderer(new PhaseM31BpmnPropertiesIntegrationTests.RecordingRenderExecution()));
        facade.Attach(host);
        host.SetApplicationActions((context, token) => ValueTask.FromResult<IEnumerable<BpmnApplicationAction>>(
            [new("application:inspect", "Inspect application state"), new("application:disabled", "Unavailable", false, "No application run")]), true);
        await host.InitializeAsync("stage4-production-active", "stage4-production-standby", "stage4-production-container");
#pragma warning disable CA1859 // Exercise the existing public presentation/lifecycle interfaces.
        IBpmnDiagramPresentation presentation = facade;
        IBpmnDocumentEditor lifecycle = facade;
#pragma warning restore CA1859
        var session = Session(host);
        var history = session.CaptureState().HistoryStatus;
        var visual = expected.VisualModel.VisualStates.Single(item => item.SemanticElementId == unassigned);
        Assert.True((await presentation.SelectVisualAsync(presentation.CapturePresentation().Stamp!, visual.Id)).Succeeded);
        Assert.True((await host.OpenApplicationContextMenuFromSelectionAsync()).Succeeded);
        var elementMenu = await Stage4IntegrationMenuAsync(host);
        Assert.Equal(unassigned, elementMenu.ApplicationContext!.Target.SemanticId);
        Assert.Equal(BpmnSemanticTargetKind.Element, elementMenu.ApplicationContext.Target.Kind);
        Assert.Equal(visual.Id, elementMenu.ApplicationContext.Target.VisualStateId);
        Assert.Equal(mode == EditingSessionAccessMode.Editable, elementMenu.PropertiesAvailable);
        Assert.Equal(mode == EditingSessionAccessMode.Editable, elementMenu.DeletionAction is not null);
        Assert.Equal(BpmnModelerOperationStatus.Rejected,
            (await host.ExecuteApplicationActionAsync(elementMenu.ApplicationContext.RequestVersion, "application:disabled")).Status);
        Assert.Empty(invocations);
        Assert.True((await host.ExecuteApplicationActionAsync(elementMenu.ApplicationContext.RequestVersion, "application:inspect")).Succeeded);
        Assert.Equal(unassigned, Assert.Single(invocations).Context.Target.SemanticId);

        Assert.True((await session.UpdateEditorStateAsync(new(semanticSceneSelection: PoolAId,
            viewport: session.CaptureState().EditorState.Viewport))).Succeeded);
        await session.WaitForIdleAsync();
        Assert.True((await host.OpenApplicationContextMenuFromSelectionAsync()).Succeeded);
        var poolMenu = await Stage4IntegrationMenuAsync(host);
        var pool = poolMenu.ApplicationContext!.Target;
        Assert.Equal(PoolAId, pool.SemanticId);
        Assert.Equal(BpmnSemanticTargetKind.Element, pool.Kind);
        Assert.Null(pool.VisualStateId);
        Assert.All(pool.Parts, part => Assert.Equal(BpmnPresentationPartKind.SemanticRegion, part.Kind));
        Assert.True((await host.ExecuteApplicationActionAsync(poolMenu.ApplicationContext.RequestVersion, "application:inspect")).Succeeded);
        Assert.Equal([unassigned, PoolAId], invocations.Select(invocation => invocation.Context.Target.SemanticId));
        Assert.Equal(expected, lifecycle.GetDocument().Snapshot);
        Assert.Equal(history, session.CaptureState().HistoryStatus);
        Assert.True(bytes.AsSpan().SequenceEqual(NativeDocumentSerializer.Export(lifecycle.GetDocument().Snapshot!).AsSpan()));
        Assert.Empty(notifications.OfType<BpmnModelerDocumentChangedEventArgs>());

        await host.OpenApplicationContextMenuFromSelectionAsync();
        var retired = (await Stage4IntegrationMenuAsync(host)).ApplicationContext!;
        var replace = await lifecycle.SetDocumentAsync(expected, new BpmnModelerDocumentLoadOptions { VerifySavedRouting = true });
        Assert.True(replace.Succeeded, Diagnostics(replace.Diagnostics));
        Assert.NotEqual(retired.Stamp.AttachmentVersion, presentation.CapturePresentation().Stamp!.AttachmentVersion);
        Assert.False((await host.ExecuteApplicationActionAsync(retired.RequestVersion, "application:inspect")).Succeeded);
        Assert.Equal(2, invocations.Count);
        Assert.True(session.CaptureState().IsClosed);
        Assert.Equal(mode, Session(host).CaptureState().AccessMode);
        Assert.Equal(0, Session(host).CaptureState().HistoryStatus.EntryCount);
        Assert.True(bytes.AsSpan().SequenceEqual(NativeDocumentSerializer.Export(lifecycle.GetDocument().Snapshot!).AsSpan()));
        Assert.Single(notifications.OfType<BpmnModelerDocumentChangedEventArgs>(),
            change => change.Kind == BpmnModelerDocumentChangeKind.DocumentReplacement);
    }

    private static async Task<DocumentCanvasContextMenuState> Stage4IntegrationMenuAsync(DocumentCanvasHost host)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (host.CaptureState().ContextMenu is not { IsApplicationDiscoveryPending: false, ApplicationActions.IsEmpty: false })
            await Task.Delay(10, timeout.Token);
        return host.CaptureState().ContextMenu!;
    }
}
