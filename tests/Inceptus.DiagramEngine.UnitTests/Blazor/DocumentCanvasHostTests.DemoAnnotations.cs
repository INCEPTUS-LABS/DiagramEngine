using Inceptus.DiagramEngine.Blazor.Demo;
using Inceptus.DiagramEngine.Canvas2D.EditingSession;
using Inceptus.DiagramEngine.Runtime.Documents;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.AspNetCore.Components.Web;

namespace Inceptus.DiagramEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
    [Fact]
    public async Task DemoAnnotationControlBindsInitialTextAndDispatchesUpdateClearAndFailureEvents()
    {
        await using var fixture = await ReadOnlyDemoFixture.CreateAsync();
        await fixture.Renderer.ExerciseDemoAnnotationControlsAsync();
        Assert.Empty(fixture.Renderer.Exceptions);
        Assert.Empty(fixture.DocumentChanges);
        Assert.Equal(0, Session(fixture.Host).CaptureState().HistoryStatus.EntryCount);
    }

    [Fact]
    public async Task DemoAnnotationProviderUpdatesClearsAndReportsFailureThroughPublicModeler()
    {
        await using var fixture = await ReadOnlyDemoFixture.CreateAsync();
        var modeler = fixture.Renderer.Modeler;
        var source = NativeDocumentSerializer.Export(modeler.GetDocument().Snapshot!);
        var attachment = modeler.CaptureDecorationAttachment()!;
        var first = await modeler.ReplaceDecorationsAsync(attachment, 1, DemoAnnotationProvider.CreateInput("Review note"));
        Assert.True(first.Accepted);
        Assert.Equal(Canvas2DDecorationUpdateStatus.Presented, first.Status);
        Assert.True(first.Presentation!.DecorationsIncluded);
        var second = await modeler.ReplaceDecorationsAsync(attachment, 2, DemoAnnotationProvider.CreateInput("Updated note"));
        Assert.Equal(Canvas2DDecorationUpdateStatus.Presented, second.Status);
        Assert.Equal(first.Presentation.BaseSceneGeneration, second.Presentation!.BaseSceneGeneration);
        var failed = await modeler.ReplaceDecorationsAsync(attachment, 3, DemoAnnotationProvider.CreateInput("Failure", true));
        Assert.True(failed.Accepted);
        Assert.Equal(Canvas2DDecorationUpdateStatus.Failed, failed.Status);
        Assert.True(failed.Presentation!.FramePresented);
        Assert.False(failed.Presentation.DecorationsIncluded);
        Assert.NotEmpty(failed.Diagnostics);
        var cleared = await modeler.ClearDecorationsAsync(attachment, 4);
        Assert.Equal(Canvas2DDecorationUpdateStatus.Presented, cleared.Status);
        Assert.True(cleared.Presentation!.FramePresented);
        Assert.False(cleared.Presentation.DecorationsIncluded);
        Assert.True(source.AsSpan().SequenceEqual(NativeDocumentSerializer.Export(modeler.GetDocument().Snapshot!).AsSpan()));
        Assert.Equal(0, Session(fixture.Host).CaptureState().HistoryStatus.EntryCount);
        Assert.Empty(fixture.DocumentChanges);
    }

#pragma warning disable BL0006 // Extend the existing renderer to dispatch actual compiled Demo control events.
    private sealed partial class ApplicationComponentRenderer
    {
        internal Task ExerciseDemoAnnotationControlsAsync() => Dispatcher.InvokeAsync(async () =>
        {
            var component = (DemoAnnotations)InstantiateComponent(typeof(DemoAnnotations));
            var componentId = AssignRootComponentId(component);
            await RenderRootComponentAsync(componentId, ParameterView.FromDictionary(
                new Dictionary<string, object?> { [nameof(DemoAnnotations.Maintenance)] = true, [nameof(DemoAnnotations.Modeler)] = Modeler }));
            var frames = GetCurrentRenderTreeFrames(componentId);
            Assert.DoesNotContain(frames.Array.Take(frames.Count), frame =>
                frame.FrameType == RenderTreeFrameType.Attribute && frame.AttributeName.StartsWith('@'));
            var input = AnnotationAttributes(componentId, "input");
            Assert.Equal("Review note", Assert.Single(input, attribute => attribute.AttributeName == "value").AttributeValue);
            var change = Assert.Single(input, attribute => attribute.AttributeName == "onchange").AttributeEventHandlerId;
            Assert.NotEqual(0UL, change);
            await DispatchEventAsync(change, null, new ChangeEventArgs { Value = "Changed through input" });
            Assert.Equal("Changed through input", Assert.Single(AnnotationAttributes(componentId, "input"),
                attribute => attribute.AttributeName == "value").AttributeValue);

            await ClickAnnotationAsync(componentId, "update");
            var state = Modeler.CaptureEditorContext()!.Presentation;
            Assert.Equal(1, state.DecorationInputRevision);
            Assert.True(state.DecorationPresentation!.FramePresented);
            Assert.True(state.DecorationPresentation.DecorationsIncluded);
            AssertAnnotationStatus(componentId, "Presented", 1L);

            await ClickAnnotationAsync(componentId, "clear");
            state = Modeler.CaptureEditorContext()!.Presentation;
            Assert.Equal(2, state.DecorationInputRevision);
            Assert.True(state.DecorationPresentation!.FramePresented);
            Assert.False(state.DecorationPresentation.DecorationsIncluded);
            AssertAnnotationStatus(componentId, "Presented", 2L);

            await ClickAnnotationAsync(componentId, "fail");
            state = Modeler.CaptureEditorContext()!.Presentation;
            Assert.Equal(3, state.DecorationInputRevision);
            Assert.True(state.DecorationPresentation!.FramePresented);
            Assert.False(state.DecorationPresentation.DecorationsIncluded);
            Assert.NotEmpty(state.DecorationDiagnostics);
            AssertAnnotationStatus(componentId, "Failed", 3L);
        });

        private Task ClickAnnotationAsync(int componentId, string action)
        {
            var attributes = AnnotationAttributes(componentId, "button", "data-decoration-action", action);
            var handler = Assert.Single(attributes, attribute => attribute.AttributeName == "onclick").AttributeEventHandlerId;
            Assert.NotEqual(0UL, handler);
            return DispatchEventAsync(handler, null, new MouseEventArgs());
        }

        private void AssertAnnotationStatus(int componentId, string status, long revision)
        {
            var attributes = AnnotationAttributes(componentId, "p", "role", "status");
            Assert.Equal(status, Assert.Single(attributes, attribute => attribute.AttributeName == "data-decoration-status").AttributeValue);
            Assert.Equal(revision.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Convert.ToString(Assert.Single(attributes, attribute => attribute.AttributeName == "data-decoration-revision").AttributeValue,
                    System.Globalization.CultureInfo.InvariantCulture));
        }

        private RenderTreeFrame[] AnnotationAttributes(int componentId, string elementName,
            string? attributeName = null, string? attributeValue = null)
        {
            var frames = GetCurrentRenderTreeFrames(componentId);
            var matches = new List<RenderTreeFrame[]>();
            for (var index = 0; index < frames.Count; index++)
            {
                var frame = frames.Array[index];
                if (frame.FrameType != RenderTreeFrameType.Element || frame.ElementName != elementName) continue;
                var attributes = new List<RenderTreeFrame>();
                for (var next = index + 1; next < frames.Count && frames.Array[next].FrameType == RenderTreeFrameType.Attribute; next++)
                    attributes.Add(frames.Array[next]);
                if (attributeName is null || attributes.Any(attribute => attribute.AttributeName == attributeName &&
                        Equals(attribute.AttributeValue, attributeValue))) matches.Add([.. attributes]);
            }
            return Assert.Single(matches);
        }
    }
#pragma warning restore BL0006
}
