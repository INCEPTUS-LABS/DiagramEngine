using Inceptus.DiagramEngine.Blazor.Demo;
using Inceptus.DiagramEngine.Bpmn.Blazor;
using Inceptus.DiagramEngine.Canvas2D.EditingSession;
using Inceptus.DiagramEngine.Runtime.Documents;
using Microsoft.AspNetCore.Components;

namespace Inceptus.DiagramEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
    [Theory]
    [InlineData(EditingSessionAccessMode.Editable)]
    [InlineData(EditingSessionAccessMode.ReadOnly)]
    public async Task SmartDemoPolicyControlsUseCompiledBindingsAndPreserveNativeState(EditingSessionAccessMode mode)
    {
        await using var fixture = await ReadOnlyDemoFixture.CreateAsync(mode);
        var modeler = fixture.Renderer.Modeler;
        var before = Session(fixture.Host).CaptureState();
        var source = NativeDocumentSerializer.Export(modeler.GetDocument().Snapshot!);
        var example = new DemoApplicationActionExample(() => modeler);
        var provider = example.Provider;
        var typed = example.TypedHandler;
        var legacy = example.LegacyHandler;
        await fixture.Renderer.ExerciseSmartExampleBindingsAsync(example);
        Assert.True(example.ToolbarEnabled);
        Assert.False(example.InspectAvailable);
        Assert.True(example.AdditionalActions);
        Assert.True(example.UseTypedHandler);
        Assert.Equal(2, example.Revision);
        Assert.Same(provider, example.Provider);
        Assert.Same(typed, example.TypedHandler);
        Assert.Same(legacy, example.LegacyHandler);
        var after = Session(fixture.Host).CaptureState();
        Assert.Equal(before.HistoryStatus, after.HistoryStatus);
        Assert.Equal(before.DocumentRevision, after.DocumentRevision);
        Assert.Same(before.ProjectedGraph, after.ProjectedGraph);
        Assert.Same(before.LayoutResult, after.LayoutResult);
        Assert.Same(before.RoutingResult, after.RoutingResult);
        Assert.True(source.AsSpan().SequenceEqual(NativeDocumentSerializer.Export(modeler.GetDocument().Snapshot!).AsSpan()));
        Assert.Empty(fixture.DocumentChanges);
        Assert.Empty(fixture.Renderer.Exceptions);
    }

    [Theory]
    [InlineData(EditingSessionAccessMode.Editable)]
    [InlineData(EditingSessionAccessMode.ReadOnly)]
    public async Task SmartDemoTypedAndLegacyHandlersOpenExactCapturedTargetWithoutNativeWrites(EditingSessionAccessMode mode)
    {
        await using var fixture = await ReadOnlyDemoFixture.CreateAsync(mode);
        var modeler = fixture.Renderer.Modeler;
        var selected = await modeler.SelectVisualAsync(modeler.CapturePresentation().Stamp!, BpmnDemoPipeline.TaskVisualId);
        Assert.True(selected.Succeeded);
        var presentation = modeler.CapturePresentation();
        var target = Assert.Single(presentation.Selection);
        var context = new BpmnApplicationActionContext(presentation, target, null, 1, 0);
        var example = new DemoApplicationActionExample(() => modeler);
        var actions = (await example.Provider(context, CancellationToken.None)).ToArray();
        Assert.Equal(["Inspect details", "Open linked record"], actions.Select(action => action.Label));
        Assert.NotNull(actions[0].Icon);
        Assert.Null(actions[1].Icon);
        Assert.False(actions[1].IsEnabled);
        Assert.NotNull(actions[1].DisabledExplanation);
        var before = modeler.GetDocument().Snapshot;
        var args = new BpmnApplicationActionInvocationEventArgs(actions[0].Id, context, CancellationToken.None);
        Assert.True((await example.TypedHandler(args)).Succeeded);
        Assert.True(example.DialogOpen);
        Assert.Contains(target.SemanticId.Value, example.Details, StringComparison.Ordinal);
        Assert.Contains("actual part: unknown", example.Details, StringComparison.Ordinal);
        example.CloseDialog();
        await example.LegacyHandler(args);
        Assert.Equal(2, example.InvocationCount);
        Assert.True(example.DialogOpen);
        example.SetAvailability(false);
        Assert.Equal(BpmnModelerOperationStatus.Unavailable, (await example.TypedHandler(args)).Status);
        Assert.Equal(2, example.InvocationCount);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Equal(BpmnModelerOperationStatus.Cancelled, (await example.TypedHandler(
            new(args.ActionId, context, cancellation.Token))).Status);
        Assert.Same(before, modeler.GetDocument().Snapshot);
        Assert.Empty(fixture.DocumentChanges);
    }

#pragma warning disable BL0006 // Reuse the canonical renderer for real compiled Demo checkbox event binding.
    private sealed partial class ApplicationComponentRenderer
    {
        internal Task ExerciseSmartExampleBindingsAsync(DemoApplicationActionExample example) => Dispatcher.InvokeAsync(async () =>
        {
            var component = (DemoApplicationActions)InstantiateComponent(typeof(DemoApplicationActions));
            var id = AssignRootComponentId(component);
            var notifications = 0;
            await RenderRootComponentAsync(id, ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(DemoApplicationActions.Example)] = example,
                [nameof(DemoApplicationActions.Changed)] = EventCallback.Factory.Create(new object(), () => notifications++),
            }));
            foreach (var (control, value) in new[] { ("smart", true), ("availability", false), ("additional", true), ("typed", true) })
            {
                var attributes = AnnotationAttributes(id, "input", "data-application-example-control", control);
                await DispatchEventAsync(Assert.Single(attributes, item => item.AttributeName == "onchange").AttributeEventHandlerId,
                    null, new ChangeEventArgs { Value = value });
            }
            Assert.Equal(4, notifications);
        });
    }
#pragma warning restore BL0006
}
