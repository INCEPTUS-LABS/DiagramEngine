using System.Text.Json;
using System.Text.Json.Nodes;
using Inceptus.Bpmn.Integration;
using Inceptus.DiagramEngine.Blazor.Demo;
using Inceptus.DiagramEngine.Bpmn.Blazor;
using Inceptus.DiagramEngine.Bpmn.Blazor.Composition;
using Inceptus.DiagramEngine.Bpmn.Blazor.Presentation;
using Inceptus.DiagramEngine.Canvas2D.EditingSession;
using Inceptus.DiagramEngine.Canvas2D.Rendering;
using Inceptus.DiagramEngine.Runtime.Documents;

namespace Inceptus.DiagramEngine.IntegrationTests;

public sealed partial class PhaseN103NativeDocumentPresentationIntegrationTests
{
    [Fact]
    public async Task Stage5LowerRevisionNativeReplacementRetiresSelectionNavigationAndActions()
    {
        var (source, semanticId) = await CreateSourceDocumentAsync();
        var snapshot = source.CaptureSnapshot();
        Assert.True(snapshot.Revision.Value > 0);
        var originalBytes = NativeDocumentSerializer.Export(snapshot);
        var json = JsonNode.Parse(originalBytes.AsSpan())!;
        json["document"]!["revision"] = 0;
        var lower = BpmnDocumentSerializer.Deserialize(JsonSerializer.SerializeToUtf8Bytes(json));
        Assert.True(lower.Succeeded, Diagnostics(lower.Diagnostics));
        var expectedBytes = NativeDocumentSerializer.Export(lower.Document!);
        var visualId = snapshot.VisualModel.VisualStates.Single(item => item.SemanticElementId == semanticId).Id;
        var factory = new BpmnModelerCompositionFactory(initialDocument: snapshot, accessMode: EditingSessionAccessMode.ReadOnly);
        using var facade = new BpmnModelerFacade(factory, _ => Task.CompletedTask);
        await using var host = new DocumentCanvasHost(factory,
            CreateRenderer(new PhaseM31BpmnPropertiesIntegrationTests.RecordingRenderExecution()),
            new FixedSurfaceObserverFactory(new Canvas2DSurfaceSize(1600, 1000, 1)),
            replacementRendererFactory: () => CreateRenderer(new PhaseM31BpmnPropertiesIntegrationTests.RecordingRenderExecution()));
        facade.Attach(host);
        host.SetApplicationActions((_, _) => ValueTask.FromResult<IEnumerable<BpmnApplicationAction>>([new("app:inspect", "Inspect")]), true);
        await host.InitializeAsync("stage5-revision-active", "stage5-revision-standby", "stage5-revision-container");
        Assert.True((await facade.SelectVisualAsync(facade.CapturePresentation().Stamp!, visualId)).Succeeded);
        await host.OpenApplicationContextMenuFromSelectionAsync();
        var action = (await Stage4IntegrationMenuAsync(host)).ApplicationContext!;
        var retired = facade.CapturePresentation().Stamp!;
        var replacement = await new DemoDocumentOperations(facade).ImportNativeDocumentAsync(expectedBytes.AsMemory());
        Assert.True(replacement.Succeeded, Diagnostics(replacement.Diagnostics));
        Assert.Equal(snapshot.DocumentId, replacement.Snapshot!.DocumentId);
        Assert.True(replacement.Snapshot.Revision.Value < snapshot.Revision.Value);
        Assert.NotEqual(retired.AttachmentVersion, facade.CapturePresentation().Stamp!.AttachmentVersion);
        Assert.Equal(BpmnModelerOperationStatus.Rejected, (await facade.SelectVisualAsync(retired, visualId)).Status);
        Assert.Equal(BpmnModelerOperationStatus.Rejected,
            (await facade.NavigateToScopeAsync(retired, snapshot.SemanticModel.RootScopeId)).Status);
        Assert.False((await host.ExecuteApplicationActionAsync(action.RequestVersion, "app:inspect")).Succeeded);
        Assert.Empty(facade.CapturePresentation().Selection);
        Assert.Equal(EditingSessionAccessMode.ReadOnly, Session(host).CaptureState().AccessMode);
        Assert.Equal(0, Session(host).CaptureState().HistoryStatus.EntryCount);
        Assert.True(expectedBytes.AsSpan().SequenceEqual(NativeDocumentSerializer.Export(facade.GetDocument().Snapshot!).AsSpan()));
        Assert.True(originalBytes.AsSpan().SequenceEqual(NativeDocumentSerializer.Export(source).AsSpan()));
    }

    [Theory]
    [InlineData("malformed")]
    [InlineData("missing-routing")]
    [InlineData("recovery")]
    [InlineData("cancelled")]
    public async Task Stage5DemoNativeImportPreservesTheReadOnlyAttachmentOnRejection(string failure)
    {
        var (source, _) = await CreateSourceDocumentAsync();
        var snapshot = source.CaptureSnapshot();
        var bytes = NativeDocumentSerializer.Export(snapshot);
        var factory = new BpmnModelerCompositionFactory(initialDocument: snapshot, accessMode: EditingSessionAccessMode.ReadOnly);
        using var facade = new BpmnModelerFacade(factory, _ => Task.CompletedTask);
        await using var host = new DocumentCanvasHost(factory,
            CreateRenderer(new PhaseM31BpmnPropertiesIntegrationTests.RecordingRenderExecution()),
            new FixedSurfaceObserverFactory(new Canvas2DSurfaceSize(1600, 1000, 1)),
            replacementRendererFactory: () => CreateRenderer(new PhaseM31BpmnPropertiesIntegrationTests.RecordingRenderExecution()));
        facade.Attach(host);
        await host.InitializeAsync("stage5-native-active", "stage5-native-standby", "stage5-native-container");
        var stamp = facade.CapturePresentation().Stamp;
        var history = Session(host).CaptureState().HistoryStatus;
        var payload = failure == "malformed" ? "{broken"u8.ToArray() : BpmnDocumentSerializer.Serialize(snapshot).ToArray();
        if (failure == "missing-routing")
        {
            var json = JsonNode.Parse(payload)!;
            Assert.True(json["document"]!["visualModel"]!.AsObject().Remove("routingScopes"));
            payload = JsonSerializer.SerializeToUtf8Bytes(json);
        }
        using var cancellation = new CancellationTokenSource();
        if (failure == "cancelled") cancellation.Cancel();
        var operations = new DemoDocumentOperations(facade);
        var rejected = await operations.ImportNativeDocumentAsync(payload,
            new BpmnModelerDocumentLoadOptions { VerifySavedRouting = true, ReprepareIncompatibleRouting = failure == "recovery" },
            cancellation.Token);
        Assert.False(rejected.Succeeded);
        Assert.Equal(failure == "cancelled" ? BpmnModelerOperationStatus.Cancelled : BpmnModelerOperationStatus.Rejected, rejected.Status);
        Assert.True(bytes.AsSpan().SequenceEqual(NativeDocumentSerializer.Export(facade.GetDocument().Snapshot!).AsSpan()));
        Assert.Equal(stamp, facade.CapturePresentation().Stamp);
        Assert.Equal(history, Session(host).CaptureState().HistoryStatus);
        Assert.Equal(EditingSessionAccessMode.ReadOnly, Session(host).CaptureState().AccessMode);
        var accepted = await operations.ImportNativeDocumentAsync(BpmnDocumentSerializer.Serialize(snapshot).AsMemory());
        Assert.True(accepted.Succeeded, Diagnostics(accepted.Diagnostics));
        Assert.NotEqual(stamp!.AttachmentVersion, facade.CapturePresentation().Stamp!.AttachmentVersion);
        Assert.True(bytes.AsSpan().SequenceEqual(NativeDocumentSerializer.Export(accepted.Snapshot!).AsSpan()));
        Assert.Equal(EditingSessionAccessMode.ReadOnly, Session(host).CaptureState().AccessMode);
        Assert.Equal(0, Session(host).CaptureState().HistoryStatus.EntryCount);
    }

    [Fact]
    public async Task Stage5TargetDeletionRetiresInFlightApplicationInvocationAndUndoCannotReviveIt()
    {
        var (source, semanticId) = await CreateSourceDocumentAsync();
        var snapshot = source.CaptureSnapshot();
        var visualId = snapshot.VisualModel.VisualStates.Single(item => item.SemanticElementId == semanticId).Id;
        var factory = new BpmnModelerCompositionFactory(initialDocument: snapshot);
        var invocations = 0;
        using var facade = new BpmnModelerFacade(factory, value =>
        {
            if (value is BpmnApplicationActionDispatch dispatch && dispatch.TryBeginInvocation(out _))
            { invocations++; dispatch.Complete(); }
            return Task.CompletedTask;
        });
        await using var host = new DocumentCanvasHost(factory,
            CreateRenderer(new PhaseM31BpmnPropertiesIntegrationTests.RecordingRenderExecution()),
            new FixedSurfaceObserverFactory(new Canvas2DSurfaceSize(1600, 1000, 1)));
        facade.Attach(host);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var delayed = new TaskCompletionSource<IEnumerable<BpmnApplicationAction>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        host.SetApplicationActions(async (_, _) =>
        {
            if (++calls == 1) return [new("application:inspect", "Inspect")];
            entered.TrySetResult();
            return await delayed.Task; // Deliberately late application service; engine must reject its result.
        }, true);
        await host.InitializeAsync("stage5-remove-active", "stage5-remove-container");
        Assert.True((await facade.SelectVisualAsync(facade.CapturePresentation().Stamp!, visualId)).Succeeded);
        await host.OpenApplicationContextMenuFromSelectionAsync();
        var menu = await Stage4IntegrationMenuAsync(host);
        var pending = host.ExecuteApplicationActionAsync(menu.ApplicationContext!.RequestVersion, "application:inspect").AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var session = Session(host);
        var deleted = await session.ExecuteAsync(new Inceptus.DiagramEngine.Bpmn.Commands.DeleteBpmnFlowNodeCommand(
            snapshot.DocumentId, snapshot.Revision, semanticId, visualId));
        Assert.True(deleted.IsCommitted, Diagnostics(deleted.Diagnostics));
        await session.WaitForIdleAsync();
        Assert.False(facade.GetDocument().Snapshot!.SemanticModel.TryGetElement(semanticId, out _));
        var afterDelete = NativeDocumentSerializer.Export(facade.GetDocument().Snapshot!);
        delayed.TrySetResult([new("application:inspect", "Inspect")]);
        Assert.False((await pending.WaitAsync(TimeSpan.FromSeconds(5))).Succeeded);
        Assert.Equal(0, invocations);
        Assert.True(afterDelete.AsSpan().SequenceEqual(NativeDocumentSerializer.Export(facade.GetDocument().Snapshot!).AsSpan()));
        Assert.Equal(1, session.CaptureState().HistoryStatus.EntryCount);
        Assert.True((await session.UndoAsync()).IsCommitted);
        await session.WaitForIdleAsync();
        Assert.True(facade.GetDocument().Snapshot!.SemanticModel.TryGetElement(semanticId, out _));
        Assert.False((await host.ExecuteApplicationActionAsync(menu.ApplicationContext.RequestVersion, "application:inspect")).Succeeded);
        Assert.Equal(0, invocations);
        Assert.Equal(1, session.CaptureState().HistoryStatus.EntryCount);
    }
}
