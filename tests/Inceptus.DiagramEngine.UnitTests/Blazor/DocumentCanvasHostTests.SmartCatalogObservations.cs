using Inceptus.DiagramEngine.Blazor.Demo;
using Inceptus.DiagramEngine.Bpmn.Blazor;
using Inceptus.DiagramEngine.Canvas2D.EditingSession;

namespace Inceptus.DiagramEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
    [Theory]
    [InlineData(EditingSessionAccessMode.Editable)]
    [InlineData(EditingSessionAccessMode.ReadOnly)]
    public async Task SmartCatalogDemoPreCancelledEntryDoesNotStartHttpOrClaimPending(EditingSessionAccessMode mode)
    {
        await using var fixture = await ReadOnlyDemoFixture.CreateAsync(mode);
        using var transport = new CatalogHttpHandler();
        using var client = new HttpClient(transport) { BaseAddress = new Uri("https://demo.invalid/") };
        using var example = new DemoApplicationActionExample(() => fixture.Renderer.Modeler, client);
        var context = await CatalogContextAsync(fixture, example);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.Equal(BpmnModelerOperationStatus.Cancelled,
            (await example.TypedHandler(new("example:inspect", context, cancelled.Token))).Status);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => example.Provider(context, cancelled.Token).AsTask());
        await WaitForCatalogCompletionAsync(example);
        Assert.Empty(transport.Requests);
        Assert.False(example.DialogOpen);
        Assert.Equal(0, example.InvocationCount);
        Assert.DoesNotContain(example.CatalogObservations!.Events, item => item.ConsumerIncomplete || item.IoIncomplete);
        Assert.All(example.CatalogObservations.Events.Where(item => item.Event == "RequestTokenObserved"),
            item => Assert.Equal("Unknown", item.ConsumerState));
    }

    [Theory]
    [InlineData(EditingSessionAccessMode.Editable, true, true)]
    [InlineData(EditingSessionAccessMode.ReadOnly, true, true)]
    [InlineData(EditingSessionAccessMode.Editable, false, true)]
    [InlineData(EditingSessionAccessMode.ReadOnly, false, true)]
    [InlineData(EditingSessionAccessMode.Editable, true, false)]
    [InlineData(EditingSessionAccessMode.ReadOnly, true, false)]
    [InlineData(EditingSessionAccessMode.Editable, false, false)]
    [InlineData(EditingSessionAccessMode.ReadOnly, false, false)]
    public async Task SmartCatalogDemoPreservesHttpAndSchemaFailuresWithLegacySemantics(
        EditingSessionAccessMode mode, bool typed, bool httpFailure)
    {
        await using var fixture = await ReadOnlyDemoFixture.CreateAsync(mode);
        using var transport = new CatalogHttpHandler();
        using var client = new HttpClient(transport) { BaseAddress = new Uri("https://demo.invalid/") };
        var modeler = fixture.Renderer.Modeler;
        using var example = new DemoApplicationActionExample(() => modeler, client) { ToolbarEnabled = true, UseTypedHandler = typed };
        await EnableCatalogDemoAsync(fixture, example);
        Assert.True((await modeler.SelectVisualAsync(modeler.CapturePresentation().Stamp!, BpmnDemoPipeline.TaskVisualId)).Succeeded);
        var surface = await CaptureSmartVerificationSurfaceAsync(fixture, popup: true);
        transport.PassNext();
        var plan = transport.BlockNext();
        if (httpFailure) plan.Release.TrySetException(new HttpRequestException("Application catalogue transport unavailable."));
        else plan.Release.TrySetResult("{}");
        await fixture.Renderer.DispatchApplicationSurfaceAsync(surface.ComponentId, surface.Handlers[Array.IndexOf(surface.Actions, "example:inspect")]);
        await WaitForApplicationConditionAsync(() => example.OperationFailureCount == 1);
        await WaitForCatalogCompletionAsync(example);
        Assert.False(example.DialogOpen);
        Assert.Equal(1, example.InvocationCount);
        Assert.Contains(typed ? "DEMO_APPLICATION_CATALOG_FAILED" : "BPMN_APPLICATION_INVOCATION_FAILED", example.OperationFeedback, StringComparison.Ordinal);
        Assert.Contains(example.CatalogObservations!.Events, item => item.Event == "IoFailed" &&
            item.Detail.Contains(httpFailure ? "HttpRequestException" : "JsonException", StringComparison.Ordinal));
        Assert.DoesNotContain(example.CatalogObservations.Events, item => item.Event == "IoCancelled");
        if (typed) Assert.Contains(example.CatalogObservations.Events, item => item.Event == "TypedOutcomeReturning" &&
            item.Detail.StartsWith("Failed;", StringComparison.Ordinal));
        Assert.Empty(fixture.Renderer.Exceptions);
    }

    [Theory]
    [InlineData(EditingSessionAccessMode.Editable)]
    [InlineData(EditingSessionAccessMode.ReadOnly)]
    public async Task SmartCatalogDemoPageCancellationIsDistinctAndIndependentInstancesContinue(EditingSessionAccessMode mode)
    {
        await using var first = await ReadOnlyDemoFixture.CreateAsync(mode);
        await using var second = await ReadOnlyDemoFixture.CreateAsync(mode);
        using var transport = new CatalogHttpHandler();
        using var client = new HttpClient(transport) { BaseAddress = new Uri("https://demo.invalid/") };
        using var example = new DemoApplicationActionExample(() => first.Renderer.Modeler, client);
        using var independent = new DemoApplicationActionExample(() => second.Renderer.Modeler, client);
        var context = await CatalogContextAsync(first, example);
        var other = await CatalogContextAsync(second, independent);
        Assert.NotEqual(context.Stamp.InstanceId, other.Stamp.InstanceId);
        var pending = transport.BlockNext();
        var task = example.TypedHandler(new("example:inspect", context, CancellationToken.None)).AsTask();
        await pending.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await WaitForCatalogPendingCaptureAsync(example);
        example.Dispose();
        example.Dispose();
        example.ReadCatalogObservations();
        Assert.Contains(example.CatalogObservations!.Events, item => item.Event == "PageTokenObserved" &&
            item.PageCancelled && !item.RequestCancelled && item.ConsumerIncomplete);
        pending.Release.TrySetResult(CatalogJson);
        Assert.Equal(BpmnModelerOperationStatus.Cancelled, (await task).Status);
        Assert.False(example.DialogOpen);
        Assert.True((await independent.TypedHandler(new("example:inspect", other, CancellationToken.None))).Succeeded);
        Assert.True(independent.DialogOpen);
        Assert.Contains(other.Stamp.InstanceId.ToString(), independent.Details, StringComparison.Ordinal);
        Assert.False(ModelerDisposed(first.Renderer.Modeler));
        Assert.False(ModelerDisposed(second.Renderer.Modeler));
        Assert.Empty(first.DocumentChanges);
        Assert.Empty(second.DocumentChanges);
    }

    [Fact]
    public async Task SmartCatalogDemoJournalDistinguishesUnknownCompletedAndOverflowWithoutChangingTasks()
    {
        await using var fixture = await ReadOnlyDemoFixture.CreateAsync();
        using var example = new DemoApplicationActionExample(() => fixture.Renderer.Modeler);
        var context = await CatalogContextAsync(fixture, example);
        var journal = new DemoApplicationActionJournal();
        using var preCancelled = new CancellationTokenSource();
        preCancelled.Cancel();
        var early = journal.Begin("Provider", context, null, 1, preCancelled.Token, CancellationToken.None);
        var completed = Task.FromResult(42);
        early.TrackIo(Task.CompletedTask);
        Assert.Same(completed, early.TrackConsumer(completed));
        Assert.Equal(CancellationToken.None, early.Token);
        Assert.Equal(CancellationToken.None, early.PageToken);
        Assert.Equal(default, early.RequestRegistration);
        var snapshot = journal.Read();
        Assert.Contains(snapshot.Events, item => item.Event == "RequestTokenObserved" && item.ConsumerState == "Unknown" &&
            !item.ConsumerIncomplete && item.IoState == "Unknown" && !item.IoIncomplete);
        Assert.Contains(snapshot.Events, item => item.Event == "ConsumerTaskCaptured" && item.ConsumerState == "RanToCompletion" && !item.ConsumerIncomplete);
        using var after = new CancellationTokenSource();
        var later = journal.Begin("TypedHandler", context, "example:inspect", 1, after.Token, CancellationToken.None);
        Assert.Same(completed, later.TrackConsumer(completed));
        after.Cancel();
        Assert.DoesNotContain(journal.Read().Events, item => item.AttemptId == later.Id && item.ConsumerIncomplete);
        for (var index = 0; index < DemoApplicationActionJournal.MaximumAttempts + 1; index++)
        {
            var attempt = journal.Begin("Provider", context, null, 1, CancellationToken.None, CancellationToken.None);
            Assert.Equal(42, await attempt.TrackConsumer(completed));
        }
        var bounded = journal.Read();
        Assert.True(bounded.Overflow);
        Assert.Equal(DemoApplicationActionJournal.MaximumAttempts, bounded.Attempts.Length);
        Assert.InRange(bounded.Events.Length, 1, DemoApplicationActionJournal.MaximumEvents);
        Assert.All(bounded.Attempts, attempt => Assert.Equal("RanToCompletion", attempt.ConsumerState));
        Assert.Contains("part=unknown:none", bounded.Attempts[0].Identity, StringComparison.Ordinal);
        Assert.Empty(fixture.DocumentChanges);
    }

    [Theory]
    [InlineData(EditingSessionAccessMode.Editable)]
    [InlineData(EditingSessionAccessMode.ReadOnly)]
    public async Task SmartCatalogDemoConsumesLoadedPolicyAndDoesNotLoadByDefault(EditingSessionAccessMode mode)
    {
        await using var fixture = await ReadOnlyDemoFixture.CreateAsync(mode);
        using var transport = new CatalogHttpHandler();
        using var client = new HttpClient(transport) { BaseAddress = new Uri("https://demo.invalid/") };
        using var example = new DemoApplicationActionExample(() => fixture.Renderer.Modeler, client);
        var context = await CatalogContextAsync(fixture, example);
        example.SetAsyncCatalogEnabled(false);
        var current = new BpmnApplicationActionContext(context.Presentation, context.Target, null, 2, example.Revision);
        Assert.Equal("Inspect details", (await example.Provider(current, CancellationToken.None)).First().Label);
        Assert.Empty(transport.Requests);
        example.SetAsyncCatalogEnabled(true);
        var revision = example.Revision;
        example.SetAsyncCatalogEnabled(true);
        Assert.Equal(revision, example.Revision);
        current = new(context.Presentation, context.Target, null, 3, revision);
        transport.BlockNext().Release.TrySetResult(CatalogJson.Replace("\"inspectEnabled\":true", "\"inspectEnabled\":false", StringComparison.Ordinal));
        var actions = (await example.Provider(current, CancellationToken.None)).ToArray();
        Assert.Equal("Catalogue inspection", actions[0].Label);
        Assert.False(actions[0].IsEnabled);
        Assert.False(actions[1].IsEnabled);
        Assert.NotNull(actions[1].DisabledExplanation);
        Assert.Single(transport.Requests);
        example.SetVerificationEnabled(false);
        Assert.False(example.AsyncCatalogEnabled);
        Assert.Equal(mode, fixture.Renderer.Modeler.AccessMode);
        Assert.Empty(fixture.DocumentChanges);
    }

    private static async Task<BpmnApplicationActionContext> CatalogContextAsync(ReadOnlyDemoFixture fixture, DemoApplicationActionExample example)
    {
        var modeler = fixture.Renderer.Modeler;
        Assert.True((await modeler.SelectVisualAsync(modeler.CapturePresentation().Stamp!, BpmnDemoPipeline.TaskVisualId)).Succeeded);
        example.SetVerificationEnabled(true);
        example.SetAsyncCatalogEnabled(true);
        var presentation = modeler.CapturePresentation();
        return new(presentation, Assert.Single(presentation.Selection), null, 1, example.Revision);
    }
}
