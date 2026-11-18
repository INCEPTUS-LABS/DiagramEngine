using Inceptus.DiagramEngine.Bpmn.Blazor;
using Inceptus.DiagramEngine.Bpmn.Blazor.Components;
using Inceptus.DiagramEngine.Canvas2D.EditingSession;
using Inceptus.DiagramEngine.Canvas2D.Rendering;
using Inceptus.DiagramEngine.Canvas2D.Rendering.Interop;
using Inceptus.DiagramEngine.Contracts.Diagnostics;
using Microsoft.Extensions.Localization;

namespace Inceptus.DiagramEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
    [Theory]
    [InlineData(EditingSessionAccessMode.Editable, true)]
    [InlineData(EditingSessionAccessMode.Editable, false)]
    [InlineData(EditingSessionAccessMode.ReadOnly, true)]
    [InlineData(EditingSessionAccessMode.ReadOnly, false)]
    public async Task ResizeStatusPreservesErrorDiagnosticsWhilePending(
        EditingSessionAccessMode mode, bool hostError)
    {
        var factory = await BpmnModelerTestComposition.CreateFreshDemoFactoryAsync();
        await using var test = await PanComponentFixture.CreateAsync(compositionFactory: factory.WithAccessMode(mode));
        if (!hostError)
        {
            test.Execution.RenderResult = new Canvas2DInteropOperationResult
            {
                Succeeded = false,
                Code = "TEST_RESIZE_PRESENTATION_FAILURE",
            };
            await test.Renderer.Dispatcher.InvokeAsync(() => Session(test.Host).RenderCurrentAsync().AsTask());
            await test.DrainAsync();
        }

        var state = test.Host.CaptureState() with
        {
            SurfaceRenderPending = true,
            LatestPresentationSucceeded = false,
            HostDiagnostics = hostError ? [new Diagnostic("TEST_HOST_FAILURE", DiagnosticSeverity.Error, "Retained host failure.")] : [],
        };
        var diagnostic = Assert.Single(hostError ? state.HostDiagnostics : state.Session!.PresentationDiagnostics);
        await test.Renderer.Dispatcher.InvokeAsync(async () =>
        {
            typeof(DocumentCanvas).GetField("_state", PropertiesUxFlags)!.SetValue(test.Activator.Canvas, state);
            await test.Activator.Canvas.RefreshAsync();
        });
        var markup = System.Net.WebUtility.HtmlDecode(await test.MarkupAsync());
        Assert.Contains("data-presentation-status=\"renderfaulted\"", markup);
        Assert.Contains(ResizeStatusMessage(test, "Status_RenderFailed"), markup);
        Assert.Contains("role=\"alert\"", markup);
        Assert.Contains(diagnostic.Message, markup);
        Assert.DoesNotContain(ResizeStatusMessage(test, "Status_Preparing"), markup);
    }

    [Theory]
    [InlineData(EditingSessionAccessMode.Editable, false, true)]
    [InlineData(EditingSessionAccessMode.Editable, false, false)]
    [InlineData(EditingSessionAccessMode.Editable, true, true)]
    [InlineData(EditingSessionAccessMode.Editable, true, false)]
    [InlineData(EditingSessionAccessMode.ReadOnly, false, true)]
    [InlineData(EditingSessionAccessMode.ReadOnly, false, false)]
    [InlineData(EditingSessionAccessMode.ReadOnly, true, true)]
    [InlineData(EditingSessionAccessMode.ReadOnly, true, false)]
    public async Task ResizeStatusDistinguishesPendingPresentationFromFailure(
        EditingSessionAccessMode mode, bool dprOnly, bool succeeds)
    {
        var factory = await BpmnModelerTestComposition.CreateFreshDemoFactoryAsync();
        await using var test = await PanComponentFixture.CreateAsync(compositionFactory: factory.WithAccessMode(mode));
        var session = Session(test.Host);
        var before = session.CaptureState();
        var document = AttachedDocument(session).CaptureSnapshot();
        var surface = Assert.IsType<Canvas2DSurfaceSize>(test.Host.CaptureState().SurfaceSize);
        test.Execution.BlockRender = true;
        test.Execution.RenderResult = new Canvas2DInteropOperationResult
        {
            Succeeded = succeeds,
            Code = succeeds ? null : "TEST_RESIZE_PRESENTATION_FAILURE",
        };
        var operation = test.Renderer.Dispatcher.InvokeAsync(() => test.Surface.RaiseAsync(
            new Canvas2DSurfaceSize(dprOnly ? surface.CssWidth : 1000, surface.CssHeight, 2)));
        try
        {
            await test.Execution.RenderStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await test.DrainAsync();
            var pending = session.CaptureState();
            Assert.Equal(mode, pending.AccessMode);
            Assert.Equal(EditingSessionStatus.Ready, pending.Status);
            Assert.False(pending.IsCurrentScenePresented);
            Assert.False(test.Host.CaptureState().LatestPresentationSucceeded);
            Assert.Empty(pending.PresentationDiagnostics);
            Assert.Empty(test.Host.CaptureState().HostDiagnostics);
            var markup = System.Net.WebUtility.HtmlDecode(await test.MarkupAsync());
            Assert.Contains("data-scene-status=\"ready\"", markup);
            Assert.Contains("data-presentation-status=\"rendering\"", markup);
            Assert.Contains(ResizeStatusMessage(test, "Status_Preparing"), markup);
            Assert.DoesNotContain(ResizeStatusMessage(test, "Status_RenderFailed"), markup);
            Assert.DoesNotContain("role=\"alert\"", markup);
        }
        finally
        {
            test.Execution.ReleaseRender();
            await operation.WaitAsync(TimeSpan.FromSeconds(10));
        }

        await test.DrainAsync();
        var final = session.CaptureState();
        Assert.Equal(EditingSessionStatus.Ready, final.Status);
        Assert.Equal(succeeds, final.IsCurrentScenePresented);
        Assert.Equal(succeeds, test.Host.CaptureState().LatestPresentationSucceeded);
        Assert.Same(document, AttachedDocument(session).CaptureSnapshot());
        Assert.Equal(before.HistoryStatus, final.HistoryStatus);
        Assert.Equal(before.EditorState.Selection, final.EditorState.Selection);
        Assert.Equal(before.EditorState.Viewport.Pan, final.EditorState.Viewport.Pan);
        Assert.Equal(before.EditorState.Viewport.Zoom, final.EditorState.Viewport.Zoom);
        var completedMarkup = System.Net.WebUtility.HtmlDecode(await test.MarkupAsync());
        Assert.Contains(succeeds ? "data-presentation-status=\"ready\"" : "data-presentation-status=\"renderfaulted\"", completedMarkup);
        if (!succeeds)
        {
            Assert.Contains(final.PresentationDiagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            Assert.Contains(ResizeStatusMessage(test, "Status_RenderFailed"), completedMarkup);
            Assert.Contains("role=\"alert\"", completedMarkup);
            test.Execution.RenderResult = new Canvas2DInteropOperationResult { Succeeded = true };
            await test.Renderer.Dispatcher.InvokeAsync(() => session.RenderCurrentAsync().AsTask());
            await test.DrainAsync();
            Assert.True(session.CaptureState().IsCurrentScenePresented);
            Assert.Empty(session.CaptureState().PresentationDiagnostics);
            var recoveredMarkup = System.Net.WebUtility.HtmlDecode(await test.MarkupAsync());
            Assert.Contains("data-presentation-status=\"ready\"", recoveredMarkup);
            Assert.DoesNotContain(ResizeStatusMessage(test, "Status_RenderFailed"), recoveredMarkup);
            Assert.DoesNotContain("role=\"alert\"", recoveredMarkup);
        }
    }

    private static string ResizeStatusMessage(PanComponentFixture test, string key) =>
        Assert.IsAssignableFrom<IStringLocalizer<ModelerStrings>>(typeof(DocumentCanvas)
            .GetProperty("Text", PropertiesUxFlags)!.GetValue(test.Activator.Canvas))[key].Value;
}
