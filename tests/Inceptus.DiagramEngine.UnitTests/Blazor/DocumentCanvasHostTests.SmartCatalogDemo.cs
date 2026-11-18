using System.Net;
using System.Text;
using Inceptus.DiagramEngine.Blazor.Demo;
using Inceptus.DiagramEngine.Bpmn.Blazor;
using Inceptus.DiagramEngine.Canvas2D.EditingSession;
using Inceptus.DiagramEngine.Runtime.Documents;

namespace Inceptus.DiagramEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
    private const string CatalogJson = """
        {"schemaVersion":1,"inspectEnabled":true,"inspectLabel":"Catalogue inspection","description":"Application data actually loaded."}
        """;

    [Theory]
    [InlineData(EditingSessionAccessMode.Editable, false, false)]
    [InlineData(EditingSessionAccessMode.Editable, false, true)]
    [InlineData(EditingSessionAccessMode.Editable, true, false)]
    [InlineData(EditingSessionAccessMode.Editable, true, true)]
    [InlineData(EditingSessionAccessMode.ReadOnly, false, false)]
    [InlineData(EditingSessionAccessMode.ReadOnly, false, true)]
    [InlineData(EditingSessionAccessMode.ReadOnly, true, false)]
    [InlineData(EditingSessionAccessMode.ReadOnly, true, true)]
    public async Task SmartCatalogDemoUsesRealBindingsAndDispatcherWithIndependentPendingTasks(
        EditingSessionAccessMode mode, bool popup, bool typed)
    {
        await using var fixture = await ReadOnlyDemoFixture.CreateAsync(mode);
        using var http = new CatalogHttpHandler();
        using var client = new HttpClient(http) { BaseAddress = new Uri("https://demo.invalid/") };
        var modeler = fixture.Renderer.Modeler;
        using var example = new DemoApplicationActionExample(() => modeler, client, () => 7)
        { ToolbarEnabled = true, UseTypedHandler = typed };
        var component = await EnableCatalogDemoAsync(fixture, example);
        var source = modeler.GetDocument().Snapshot!;
        var bytes = NativeDocumentSerializer.Export(source);
        var before = Session(fixture.Host).CaptureState();
        var provider = http.BlockNext();
        Assert.True((await modeler.SelectVisualAsync(modeler.CapturePresentation().Stamp!, BpmnDemoPipeline.TaskVisualId)).Succeeded);
        await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await WaitForCatalogPendingCaptureAsync(example);
        await fixture.Renderer.ClickSmartVerificationAsync(component, "read-catalog");
        Assert.Contains(example.CatalogObservations!.Events, item => item.Event == "ConsumerTaskCaptured" && item.ConsumerIncomplete && item.IoIncomplete);
        Assert.False(example.DialogOpen);
        provider.Release.TrySetResult(CatalogJson);
        await SmartToolbarAsync(fixture.Host);
        var surface = await CaptureSmartVerificationSurfaceAsync(fixture, popup);
        var version = popup ? fixture.Host.CaptureState().ContextMenu!.ApplicationContext!.RequestVersion :
            fixture.Host.ApplicationToolbar!.Context.RequestVersion;
        http.PassNext(); // The existing dispatcher rechecks availability before handler entry.
        var handling = http.BlockNext();
        var action = Array.IndexOf(surface.Actions, "example:inspect");
        await fixture.Renderer.DispatchApplicationSurfaceAsync(surface.ComponentId, surface.Handlers[action]);
        await handling.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(1, example.InvocationCount);
        Assert.False(example.DialogOpen);
        Assert.False((await fixture.Host.ExecuteApplicationActionAsync(version, "example:inspect")).Succeeded);
        Assert.Equal(1, example.InvocationCount);
        handling.Release.TrySetResult(CatalogJson);
        await WaitForApplicationConditionAsync(() => example.DialogOpen);
        await WaitForCatalogCompletionAsync(example);
        Assert.Contains("Application data actually loaded.", example.Details, StringComparison.Ordinal);
        Assert.Contains(BpmnDemoPipeline.TaskId.Value, example.Details, StringComparison.Ordinal);
        Assert.Contains(typed ? "Succeeded" : "Legacy callback completed", example.ConsumerOutcome, StringComparison.Ordinal);
        Assert.Contains(example.CatalogObservations!.Attempts, attempt => attempt.Identity.Contains("mount=7", StringComparison.Ordinal));
        Assert.Contains(example.CatalogObservations.Events, item => item.Event == "InspectorPublished");
        Assert.Contains(example.CatalogObservations.Events, item => item.Event == (typed ? "TypedOutcomeReturning" : "LegacyAttemptReturning") &&
            item.Detail.StartsWith("Succeeded;", StringComparison.Ordinal));
        Assert.All(http.Requests, uri => Assert.Equal("https://demo.invalid/demo/application-action-catalog.json", uri));
        Assert.True(bytes.AsSpan().SequenceEqual(NativeDocumentSerializer.Export(modeler.GetDocument().Snapshot!).AsSpan()));
        var after = Session(fixture.Host).CaptureState();
        Assert.Equal(before.HistoryStatus, after.HistoryStatus);
        Assert.Same(before.ProjectedGraph, after.ProjectedGraph);
        Assert.Same(before.LayoutResult, after.LayoutResult);
        Assert.Same(before.RoutingResult, after.RoutingResult);
        Assert.Equal(mode, modeler.AccessMode);
        Assert.Empty(fixture.DocumentChanges);
        Assert.Empty(fixture.Renderer.Exceptions);
    }

    [Theory]
    [InlineData(EditingSessionAccessMode.Editable, "selection", false)]
    [InlineData(EditingSessionAccessMode.ReadOnly, "selection", false)]
    [InlineData(EditingSessionAccessMode.Editable, "scope", false)]
    [InlineData(EditingSessionAccessMode.ReadOnly, "scope", false)]
    [InlineData(EditingSessionAccessMode.Editable, "policy", false)]
    [InlineData(EditingSessionAccessMode.ReadOnly, "policy", false)]
    [InlineData(EditingSessionAccessMode.Editable, "replacement", false)]
    [InlineData(EditingSessionAccessMode.ReadOnly, "replacement", false)]
    [InlineData(EditingSessionAccessMode.Editable, "dispose", false)]
    [InlineData(EditingSessionAccessMode.ReadOnly, "dispose", false)]
    [InlineData(EditingSessionAccessMode.Editable, "selection", true)]
    [InlineData(EditingSessionAccessMode.ReadOnly, "selection", true)]
    [InlineData(EditingSessionAccessMode.Editable, "scope", true)]
    [InlineData(EditingSessionAccessMode.ReadOnly, "scope", true)]
    [InlineData(EditingSessionAccessMode.Editable, "policy", true)]
    [InlineData(EditingSessionAccessMode.ReadOnly, "policy", true)]
    [InlineData(EditingSessionAccessMode.Editable, "replacement", true)]
    [InlineData(EditingSessionAccessMode.ReadOnly, "replacement", true)]
    [InlineData(EditingSessionAccessMode.Editable, "dispose", true)]
    [InlineData(EditingSessionAccessMode.ReadOnly, "dispose", true)]
    public async Task SmartCatalogDemoRetiresPendingConsumerWithoutStaleInspector(
        EditingSessionAccessMode mode, string change, bool handler)
    {
        await using var fixture = await ReadOnlyDemoFixture.CreateAsync(mode);
        using var http = new CatalogHttpHandler();
        using var client = new HttpClient(http) { BaseAddress = new Uri("https://demo.invalid/") };
        var modeler = fixture.Renderer.Modeler;
        using var example = new DemoApplicationActionExample(() => modeler, client) { ToolbarEnabled = true, UseTypedHandler = true };
        var component = await EnableCatalogDemoAsync(fixture, example);
        CatalogHttpPlan pending;
        if (handler)
        {
            Assert.True((await modeler.SelectVisualAsync(modeler.CapturePresentation().Stamp!, BpmnDemoPipeline.TaskVisualId)).Succeeded);
            var surface = await CaptureSmartVerificationSurfaceAsync(fixture, popup: false);
            http.PassNext();
            pending = http.BlockNext();
            await fixture.Renderer.DispatchApplicationSurfaceAsync(surface.ComponentId, surface.Handlers[Array.IndexOf(surface.Actions, "example:inspect")]);
        }
        else
        {
            pending = http.BlockNext();
            Assert.True((await modeler.SelectVisualAsync(modeler.CapturePresentation().Stamp!, BpmnDemoPipeline.TaskVisualId)).Succeeded);
        }
        await pending.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await WaitForCatalogPendingCaptureAsync(example);
        var observed = Assert.Single(example.CatalogObservations!.Attempts, attempt => attempt.IoState == "WaitingForActivation");
        var bytes = NativeDocumentSerializer.Export(modeler.GetDocument().Snapshot!);
        switch (change)
        {
            case "selection":
                Assert.True((await modeler.SelectVisualAsync(modeler.CapturePresentation().Stamp!, BpmnDemoPipeline.ApprovedTaskVisualId)).Succeeded);
                break;
            case "scope":
                Assert.True((await modeler.NavigateToScopeAsync(modeler.CapturePresentation().Stamp!, BpmnDemoPipeline.ProcessOrderScopeId)).Succeeded);
                break;
            case "policy":
                await fixture.Renderer.ChangeSmartVerificationAsync(component, "outcome", nameof(DemoApplicationOutcome.Cancelled));
                break;
            case "replacement":
                Assert.True((await modeler.SetDocumentAsync(modeler.GetDocument().Snapshot!, new BpmnModelerDocumentLoadOptions { VerifySavedRouting = true })).Succeeded);
                // This canonical root omits its native render tree. Render the actual action surface
                // after replacement, as the browser child does on the host's state notification.
                await fixture.Renderer.CaptureApplicationSurfaceAsync(fixture.Host);
                break;
            case "dispose":
                await fixture.Renderer.RemoveVerificationModelerAsync();
                break;
        }
        await WaitForApplicationConditionAsync(() =>
        {
            example.ReadCatalogObservations();
            return example.CatalogObservations!.Events.Any(item => item.AttemptId == observed.Id && item.Event == "RequestTokenObserved");
        });
        Assert.Contains(example.CatalogObservations!.Events, item => item.AttemptId == observed.Id &&
            item.Event == "RequestTokenObserved" && item.ConsumerIncomplete && item.IoIncomplete && !item.PageCancelled);
        pending.Release.TrySetResult(CatalogJson);
        await WaitForCatalogCompletionAsync(example);
        Assert.False(example.DialogOpen);
        Assert.DoesNotContain(example.CatalogObservations!.Events, item => item.AttemptId == observed.Id && item.Event == "InspectorPublished");
        if (handler) Assert.Contains(example.CatalogObservations.Events, item => item.AttemptId == observed.Id &&
            item.Event == "TypedOutcomeReturning" && item.Detail.StartsWith("Cancelled;", StringComparison.Ordinal));
        Assert.Equal(handler ? 1 : 0, example.InvocationCount);
        if (change != "dispose") Assert.True(bytes.AsSpan().SequenceEqual(NativeDocumentSerializer.Export(modeler.GetDocument().Snapshot!).AsSpan()));
        Assert.Empty(fixture.Renderer.Exceptions);
    }

    [Theory]
    [InlineData(EditingSessionAccessMode.Editable, false)]
    [InlineData(EditingSessionAccessMode.Editable, true)]
    [InlineData(EditingSessionAccessMode.ReadOnly, false)]
    [InlineData(EditingSessionAccessMode.ReadOnly, true)]
    public async Task SmartCatalogDemoUncommittedReplacementPreservesPendingAttempt(EditingSessionAccessMode mode, bool cancelled)
    {
        await using var fixture = await ReadOnlyDemoFixture.CreateAsync(mode);
        using var http = new CatalogHttpHandler();
        using var client = new HttpClient(http) { BaseAddress = new Uri("https://demo.invalid/") };
        var modeler = fixture.Renderer.Modeler;
        using var example = new DemoApplicationActionExample(() => modeler, client) { ToolbarEnabled = true, UseTypedHandler = true };
        await EnableCatalogDemoAsync(fixture, example);
        Assert.True((await modeler.SelectVisualAsync(modeler.CapturePresentation().Stamp!, BpmnDemoPipeline.TaskVisualId)).Succeeded);
        var surface = await CaptureSmartVerificationSurfaceAsync(fixture, popup: false);
        http.PassNext();
        var pending = http.BlockNext();
        await fixture.Renderer.DispatchApplicationSurfaceAsync(surface.ComponentId, surface.Handlers[Array.IndexOf(surface.Actions, "example:inspect")]);
        await pending.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var stamp = modeler.CapturePresentation().Stamp;
        var source = modeler.GetDocument().Snapshot!;
        var (controls, id) = await fixture.Renderer.RenderVerificationControlsAsync(allowReplacement: true);
        await fixture.Renderer.EnableVerificationControlsAsync(id);
        await fixture.Renderer.ClickVerificationAsync(id, cancelled ? "replace-cancelled" : "replace-incompatible");
        Assert.Equal(cancelled ? BpmnModelerOperationStatus.Cancelled : BpmnModelerOperationStatus.Rejected, controls.DocumentResult!.Status);
        Assert.Equal(stamp, modeler.CapturePresentation().Stamp);
        Assert.Same(source, modeler.GetDocument().Snapshot);
        Assert.False((await pending.Started.Task).IsCancellationRequested);
        pending.Release.TrySetResult(CatalogJson);
        await WaitForApplicationConditionAsync(() => example.DialogOpen);
        await WaitForCatalogCompletionAsync(example);
        Assert.Equal(1, example.InvocationCount);
        Assert.Equal(mode, modeler.AccessMode);
        Assert.Empty(fixture.Renderer.Exceptions);
    }

    private static async Task<int> EnableCatalogDemoAsync(ReadOnlyDemoFixture fixture, DemoApplicationActionExample example)
    {
        var component = await fixture.Renderer.RenderSmartVerificationAsync(example);
        await fixture.Renderer.ChangeSmartVerificationAsync(component, "enabled", true);
        await fixture.Renderer.ChangeSmartVerificationAsync(component, "catalog", true);
        Assert.True(example.AsyncCatalogEnabled);
        Assert.Equal(2, example.Revision);
        return component;
    }

    private static Task WaitForCatalogCompletionAsync(DemoApplicationActionExample example) => WaitForApplicationConditionAsync(() =>
    {
        example.ReadCatalogObservations();
        return example.CatalogObservations!.Attempts.All(attempt => attempt.ConsumerState is "RanToCompletion" or "Canceled" or "Faulted");
    });

    private static Task WaitForCatalogPendingCaptureAsync(DemoApplicationActionExample example) => WaitForApplicationConditionAsync(() =>
    {
        example.ReadCatalogObservations();
        return example.CatalogObservations!.Attempts.Any(attempt => attempt.ConsumerState == "WaitingForActivation" && attempt.IoState == "WaitingForActivation");
    });

    private sealed class CatalogHttpPlan
    {
        internal TaskCompletionSource<CancellationToken> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource<string> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <summary>Controlled HTTP completion exists only in canonical managed tests, never in the browser Demo.</summary>
    private sealed class CatalogHttpHandler : HttpMessageHandler
    {
        private readonly object _sync = new();
        private readonly Queue<CatalogHttpPlan> _plans = [];
        private readonly List<CatalogHttpPlan> _allPlans = [];
        internal List<string> Requests { get; } = [];
        internal CatalogHttpPlan BlockNext()
        {
            var plan = new CatalogHttpPlan();
            lock (_sync) { _plans.Enqueue(plan); _allPlans.Add(plan); }
            return plan;
        }
        internal void PassNext() => BlockNext().Release.TrySetResult(CatalogJson);
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                lock (_sync)
                    foreach (var plan in _allPlans) plan.Release.TrySetResult(CatalogJson);
            }
            base.Dispose(disposing);
        }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CatalogHttpPlan? plan;
            lock (_sync)
            {
                Requests.Add(request.RequestUri!.AbsoluteUri);
                plan = _plans.Count == 0 ? null : _plans.Dequeue();
            }
            var json = CatalogJson;
            if (plan is not null)
            {
                plan.Started.TrySetResult(cancellationToken);
                // Deliberately uncooperative test transport proves cancellation observation before late completion.
                json = await plan.Release.Task;
            }
            return new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }
}
