using System.Reflection;
using Inceptus.DiagramEngine.Bpmn.Blazor;
using Inceptus.DiagramEngine.Bpmn.Blazor.Components;
using Inceptus.DiagramEngine.Bpmn.Blazor.Presentation;
using Inceptus.DiagramEngine.Contracts.Geometry;
using Inceptus.DiagramEngine.Contracts.Presentation;
using Inceptus.DiagramEngine.Contracts.Primitives;
using Inceptus.DiagramEngine.Contracts.Toolbox;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace Inceptus.DiagramEngine.UnitTests.Blazor;

public sealed partial class InceptusBpmnModelerFacadeComponentTests
{
    [Fact]
    public async Task Stage4CanvasPreservesKeyboardBubblingAndTheExistingRootEscapeHandler()
    {
        using var services = CreateServices();
        await using var component = new DocumentCanvas();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(DocumentCanvas).GetProperty("Text", flags)!.SetValue(component,
            services.GetRequiredService<IStringLocalizer<ModelerStrings>>());
        using var builder = new RenderTreeBuilder();
        typeof(DocumentCanvas).GetMethod("BuildRenderTree", flags)!.Invoke(component, [builder]);

#pragma warning disable BL0006 // Inspect the compiled event modifiers that control Blazor ancestor key propagation.
        var frames = builder.GetFrames();
        var canvasCount = 0;
        for (var index = 0; index < frames.Count; index++)
        {
            var frame = frames.Array[index];
            if (frame.FrameType != RenderTreeFrameType.Element || frame.ElementName != "canvas") continue;
            canvasCount++;
            var attributeNames = new List<string>();
            for (var attributeIndex = index + 1; attributeIndex < frames.Count &&
                 frames.Array[attributeIndex].FrameType == RenderTreeFrameType.Attribute; attributeIndex++)
                attributeNames.Add(frames.Array[attributeIndex].AttributeName);
            Assert.Contains("onkeydown", attributeNames);
            Assert.DoesNotContain("__internal_stopPropagation_onkeydown", attributeNames);
            Assert.DoesNotContain("__internal_preventDefault_onkeydown", attributeNames);
        }
        Assert.Equal(2, canvasCount);
#pragma warning restore BL0006

        var selection = Assert.IsType<ToolboxSelectionState>(typeof(DocumentCanvas)
            .GetField("_toolboxSelection", flags)!.GetValue(component));
        var selected = new ToolboxItemId("test:unchanged-default-keyboard");
        Assert.True(selection.Select(selected));
        var escape = new KeyboardEventArgs { Key = "Escape" };
        await Assert.IsAssignableFrom<Task>(typeof(DocumentCanvas)
            .GetMethod("HandleCanvasContextKeyAsync", flags)!.Invoke(component, [escape]));
        Assert.Equal(selected, selection.SelectedItemId);
        await Assert.IsAssignableFrom<Task>(typeof(DocumentCanvas)
            .GetMethod("HandleEditorKeyDownAsync", flags)!.Invoke(component, [escape]));
        Assert.Null(selection.SelectedItemId);
    }

    [Fact]
    public async Task Stage4RootActionProvidersUseTheirOwnDispatcherAndPermitReentrantRenders()
    {
        using var services = CreateServices();
        await using var first = new ExceptionRecordingRenderer(services);
        await using var second = new ExceptionRecordingRenderer(services);
        var started = NewCompletion();
        var release = NewCompletion();
        var firstCalls = 0;
        var secondCalls = 0;
        var context = CreateApplicationActionContext();
        BpmnApplicationActionProvider firstProvider = async (received, token) =>
        {
            Assert.True(first.Dispatcher.CheckAccess());
            Assert.Same(context, received);
            firstCalls++;
            await first.SetParametersAsync(Parameters((nameof(InceptusBpmnModeler.Class), "from-provider")));
            started.SetResult();
            await release.Task.WaitAsync(token);
            return [new("first:inspect", "Inspect first")];
        };
        BpmnApplicationActionProvider secondProvider = (received, _) =>
        {
            Assert.True(second.Dispatcher.CheckAccess());
            Assert.Same(context, received);
            secondCalls++;
            return ValueTask.FromResult<IEnumerable<BpmnApplicationAction>>([new("second:inspect", "Inspect second")]);
        };
        await first.InitializeAsync(Parameters((nameof(InceptusBpmnModeler.ApplicationActionProvider), firstProvider)));
        await second.InitializeAsync(Parameters((nameof(InceptusBpmnModeler.ApplicationActionProvider), secondProvider)));
        var firstConfigured = ConfiguredApplicationActionProvider(first.Component);
        var secondConfigured = ConfiguredApplicationActionProvider(second.Component);

        var pending = Task.Run(async () => await firstConfigured(context, CancellationToken.None));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(pending.IsCompleted);
        await first.SetParametersAsync(Parameters((nameof(InceptusBpmnModeler.Style), "height: 20rem")));
        Assert.Same(firstConfigured, ConfiguredApplicationActionProvider(first.Component));
        Assert.Equal("second:inspect", Assert.Single(await secondConfigured(context, CancellationToken.None)).Id);
        release.SetResult();
        Assert.Equal("first:inspect", Assert.Single(await pending.WaitAsync(TimeSpan.FromSeconds(5))).Id);

        await first.Dispatcher.InvokeAsync(first.Component.Dispose);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            firstConfigured(context, CancellationToken.None).AsTask());
        Assert.Equal("second:inspect", Assert.Single(await secondConfigured(context, CancellationToken.None)).Id);
        Assert.Equal(1, firstCalls);
        Assert.Equal(2, secondCalls);
        Assert.Empty(first.Exceptions);
        Assert.Empty(second.Exceptions);
    }

    private static BpmnApplicationActionProvider ConfiguredApplicationActionProvider(InceptusBpmnModeler component) =>
        Assert.IsType<BpmnApplicationActionProvider>(typeof(BpmnModelerFacade)
            .GetField("_applicationActionProvider", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(GetFacade(component)));

    private static BpmnApplicationActionContext CreateApplicationActionContext()
    {
        var part = new BpmnPresentationPart(new SceneObjectId("test:action:body"),
            BpmnPresentationPartKind.NodeBody, new RectD(20, 30, 100, 60), null);
        var target = new BpmnSemanticSelectionTarget(new SemanticElementId("test:action:task"),
            BpmnSemanticTargetKind.Element, new VisualStateId("test:action:visual"), [part]);
        var stamp = new BpmnPresentationContextStamp(Guid.NewGuid(), 1,
            new DocumentId("test:action:document"), new DocumentRevision(0),
            new ProcessScopePresentationContext(new DocumentScopeId("test:action:process")), 1, 1);
        return new(new BpmnPresentationSnapshot(stamp, [target], part, [], [], []), target, part, 1);
    }
}
