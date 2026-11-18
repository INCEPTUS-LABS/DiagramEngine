using System.Globalization;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using Inceptus.DiagramEngine.Bpmn.Blazor;
using Inceptus.DiagramEngine.Bpmn.Blazor.Components;
using Inceptus.DiagramEngine.Bpmn.Blazor.Presentation;
using Inceptus.DiagramEngine.Canvas2D.EditingSession;
using Inceptus.DiagramEngine.Contracts.Geometry;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace Inceptus.DiagramEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
    [Fact]
    public async Task ApplicationPopupDefersInitialFocusUntilPendingApplicationOnlyShellExists()
    {
        var browser = new ApplicationFocusJsRuntime();
        using var services = new ServiceCollection().AddSingleton<IJSRuntime>(browser)
            .AddLogging().AddInceptusBpmnModeler().BuildServiceProvider();
        await using var renderer = new ApplicationComponentRenderer(services);
        await using var fixture = new ApplicationActionFixture(new(initialDocument: A24Snapshot(), accessMode: EditingSessionAccessMode.ReadOnly));
        await fixture.InitializeAsync();
        var definitions = new TaskCompletionSource<IEnumerable<BpmnApplicationAction>>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Host.SetApplicationActions((_, _) => new(definitions.Task), true);
        await SelectApplicationTargetAsync(fixture.Facade, new("v-source"));
        await fixture.Host.OpenApplicationContextMenuFromSelectionAsync();
        var surface = await renderer.CaptureApplicationSurfaceAsync(fixture.Host);
        Assert.DoesNotContain("menu", surface.Roles);
        await renderer.UpdateApplicationFocusAsync(surface);
        Assert.Equal([true], browser.FocusRequests);
        definitions.SetResult([new("app:inspect", "Inspect details")]);
        await ApplicationMenuAsync(fixture.Host);
        await renderer.RefreshApplicationSurfaceAsync(surface, fixture.Host);
        browser.MenuRendered = true;
        await renderer.UpdateApplicationFocusAsync(surface);
        await renderer.UpdateApplicationFocusAsync(surface);
        Assert.Equal([true, true, false], browser.FocusRequests);
        fixture.Host.CloseContextMenu();
        await renderer.RefreshApplicationSurfaceAsync(surface, fixture.Host);
        await renderer.UpdateApplicationFocusAsync(surface);
        Assert.DoesNotContain("restoreCanvasFocus", browser.Calls);
        Assert.Empty(renderer.Exceptions);
    }

    [Theory]
    [InlineData(EditingSessionAccessMode.ReadOnly)]
    [InlineData(EditingSessionAccessMode.Editable)]
    public async Task ApplicationPopupCompiledSurfaceRemovesHeadingAndAddsOnlyJustifiedSeparator(EditingSessionAccessMode mode)
    {
        using var services = new ServiceCollection().AddSingleton<IJSRuntime>(new SiteToolbarJsRuntime())
            .AddLogging().AddInceptusBpmnModeler().BuildServiceProvider();
        await using var renderer = new ApplicationComponentRenderer(services);
        await using var fixture = new ApplicationActionFixture(new(initialDocument: A24Snapshot(), accessMode: mode));
        await fixture.InitializeAsync();
        fixture.Host.SetApplicationActions((_, _) => ValueTask.FromResult<IEnumerable<BpmnApplicationAction>>(
            [new("app:inspect", "Inspect details"), new("app:linked", "Open linked record", false, "No linked record is available.")]), true);
        await SelectApplicationTargetAsync(fixture.Facade, new("v-source"));
        await fixture.Host.OpenApplicationContextMenuFromSelectionAsync();
        await ApplicationMenuAsync(fixture.Host);
        var surface = await renderer.CaptureApplicationSurfaceAsync(fixture.Host);
        Assert.DoesNotContain(surface.Classes, static value => value.Contains("application-action-heading", StringComparison.Ordinal));
        Assert.Equal(["app:inspect", "app:linked"], surface.Actions);
        Assert.Contains("No linked record is available.", surface.Text);
        Assert.Equal(mode == EditingSessionAccessMode.Editable ? 1 : 0, surface.Roles.Count(static role => role == "separator"));
        Assert.Contains(surface.Classes, static value => value.Contains("object-context-menu", StringComparison.Ordinal));
        fixture.Host.SetApplicationActions((_, _) => ValueTask.FromResult<IEnumerable<BpmnApplicationAction>>([]), true);
        await fixture.Host.OpenApplicationContextMenuFromSelectionAsync();
        await WaitForApplicationConditionAsync(() => fixture.Host.CaptureState().ContextMenu?.IsApplicationDiscoveryPending != true);
        surface = await renderer.CaptureApplicationSurfaceAsync(fixture.Host);
        Assert.DoesNotContain("application-action-group", surface.Classes);
        Assert.DoesNotContain("separator", surface.Roles);
        Assert.Empty(surface.Actions);
        Assert.Empty(renderer.Exceptions);
    }

    [Theory]
    [InlineData(EditingSessionAccessMode.ReadOnly)]
    [InlineData(EditingSessionAccessMode.Editable)]
    public async Task ApplicationToolbarCompiledClickUsesOneDispatcherAndDecorativeInvariantIcon(EditingSessionAccessMode mode)
    {
        using var services = new ServiceCollection().AddSingleton<IJSRuntime>(new SiteToolbarJsRuntime())
            .AddLogging().AddInceptusBpmnModeler().BuildServiceProvider();
        await using var renderer = new ApplicationComponentRenderer(services);
        await using var fixture = new ApplicationActionFixture(new(initialDocument: A24Snapshot(), accessMode: mode));
        await fixture.InitializeAsync();
        var icon = new BpmnApplicationActionIcon("M0 0 L10 0 L10 10 Z", new RectD(0.5, 0.25, 10, 10));
        fixture.Host.SetApplicationActions((_, _) => ValueTask.FromResult<IEnumerable<BpmnApplicationAction>>(
            [new("app:inspect", "Inspect details", true, null, icon),
                new("app:linked", "Open linked record", false, "No linked record is available.")]), true, false, true, 0);
        await SelectApplicationTargetAsync(fixture.Facade, new("v-source"));
        await WaitForApplicationConditionAsync(() => fixture.Host.ApplicationToolbar is { IsDiscoveryPending: false, Actions.IsEmpty: false });
        var toolbar = fixture.Host.ApplicationToolbar!;
        var culture = CultureInfo.CurrentCulture;
        ApplicationSurfaceCapture surface;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pl-PL");
            surface = await renderer.CaptureApplicationSurfaceAsync(fixture.Host);
        }
        finally { CultureInfo.CurrentCulture = culture; }
        Assert.Contains("toolbar", surface.Roles);
        Assert.Contains("0.5 0.25 10 10", surface.ViewBoxes);
        Assert.Contains("false", surface.Focusable);
        Assert.Equal(2, surface.Actions.Length);
        Assert.Equal("app:inspect", surface.Actions[0]);
        Assert.DoesNotContain("Inspect details", surface.Text);
        Assert.DoesNotContain("Open linked record", surface.Text);
        Assert.Contains("0 0 24 24", surface.ViewBoxes); // Decorative fallback for the icon-free descriptor.
        Assert.Contains("Inspect details", surface.Labels);
        Assert.Contains("Open linked record", surface.Labels);
        Assert.Contains("Open linked record: No linked record is available.", surface.Titles);
        await renderer.DispatchApplicationSurfaceAsync(surface.ComponentId, surface.Handlers[0]);
        await WaitForApplicationConditionAsync(() => fixture.Invocations.Count == 1);
        Assert.Equal("app:inspect", Assert.Single(fixture.Invocations).ActionId);
        Assert.False((await fixture.Host.ExecuteApplicationActionAsync(toolbar.Context.RequestVersion, "app:inspect")).Succeeded);
        Assert.Empty(renderer.Exceptions);
    }

    private sealed record ApplicationSurfaceCapture(int ComponentId, DocumentCanvas Canvas, string[] Classes, string[] Text,
        string[] Actions, string[] Roles, string[] ViewBoxes, string[] Focusable, string[] Labels, string[] Titles, ulong[] Handlers);

    private sealed class ApplicationFocusJsRuntime : IJSRuntime, IJSObjectReference
    {
        internal bool MenuRendered { get; set; }
        internal List<bool> FocusRequests { get; } = [];
        internal List<string> Calls { get; } = [];
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => InvokeAsync<TValue>(identifier, default, args);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            Calls.Add(identifier);
            if (identifier == "import") return ValueTask.FromResult((TValue)(object)this);
            if (identifier == "focusContextMenu")
            {
                FocusRequests.Add((bool)args![1]!);
                return ValueTask.FromResult((TValue)(object)MenuRendered);
            }
            return ValueTask.FromResult(default(TValue)!);
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

#pragma warning disable BL0006 // Verify actual compiled Razor attributes and event bindings in the canonical renderer.
    private sealed partial class ApplicationComponentRenderer
    {
        internal Task<ApplicationSurfaceCapture> CaptureApplicationSurfaceAsync(DocumentCanvasHost host) => Dispatcher.InvokeAsync(async () =>
        {
            var canvas = (ApplicationComponentCanvas)InstantiateComponent(typeof(ApplicationComponentCanvas));
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(DocumentCanvas).GetField("_host", flags)!.SetValue(canvas, host);
            typeof(DocumentCanvas).GetField("_state", flags)!.SetValue(canvas, host.CaptureState());
            typeof(DocumentCanvas).GetMethod("CaptureApplicationToolbar", flags)!.Invoke(canvas, [host]);
            var id = AssignRootComponentId(canvas);
            await RenderRootComponentAsync(id, ParameterView.Empty);
            var frames = GetCurrentRenderTreeFrames(id);
            var classes = new List<string>();
            var text = new List<string>();
            var actions = new List<string>();
            var roles = new List<string>();
            var viewBoxes = new List<string>();
            var focusable = new List<string>();
            var labels = new List<string>();
            var titles = new List<string>();
            var handlers = new List<ulong>();
            for (var index = 0; index < frames.Count; index++)
            {
                var frame = frames.Array[index];
                if (frame.FrameType == RenderTreeFrameType.Text) text.Add(frame.TextContent);
                if (frame.FrameType == RenderTreeFrameType.Markup)
                {
                    // Razor emits constant fragments (including the group separator) as
                    // markup rather than individual element/attribute frames.
                    var markup = frame.MarkupContent;
                    CaptureMarkupAttributes(markup, "class", classes);
                    CaptureMarkupAttributes(markup, "role", roles);
                    CaptureMarkupAttributes(markup, "viewBox", viewBoxes);
                    CaptureMarkupAttributes(markup, "focusable", focusable);
                    CaptureMarkupAttributes(markup, "aria-label", labels);
                    CaptureMarkupAttributes(markup, "title", titles);
                    text.Add(WebUtility.HtmlDecode(Regex.Replace(markup, "<[^>]*>", string.Empty)));
                }
                if (frame.FrameType != RenderTreeFrameType.Attribute) continue;
                var value = frame.AttributeValue?.ToString() ?? string.Empty;
                switch (frame.AttributeName)
                {
                    case "class": classes.Add(value); break;
                    case "role": roles.Add(value); break;
                    case "viewBox": viewBoxes.Add(value); break;
                    case "focusable": focusable.Add(value); break;
                    case "aria-label": labels.Add(value); break;
                    case "title": titles.Add(value); break;
                    case "data-application-action-id":
                        actions.Add(value);
                        for (var following = index + 1; following < frames.Count && frames.Array[following].FrameType == RenderTreeFrameType.Attribute; following++)
                            if (frames.Array[following].AttributeName == "onclick") handlers.Add(frames.Array[following].AttributeEventHandlerId);
                        break;
                }
            }
            return new ApplicationSurfaceCapture(id, canvas, classes.ToArray(), text.ToArray(), actions.ToArray(), roles.ToArray(), viewBoxes.ToArray(), focusable.ToArray(), labels.ToArray(), titles.ToArray(), handlers.ToArray());
        });

        private static void CaptureMarkupAttributes(string markup, string name, List<string> values)
        {
            foreach (Match match in Regex.Matches(markup, $"(?<![\\w:-]){Regex.Escape(name)}=\"([^\"]*)\"", RegexOptions.CultureInvariant))
                values.Add(WebUtility.HtmlDecode(match.Groups[1].Value));
        }

        internal Task RefreshApplicationSurfaceAsync(ApplicationSurfaceCapture surface, DocumentCanvasHost host) => Dispatcher.InvokeAsync(async () =>
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(DocumentCanvas).GetField("_state", flags)!.SetValue(surface.Canvas, host.CaptureState());
            typeof(DocumentCanvas).GetMethod("CaptureApplicationToolbar", flags)!.Invoke(surface.Canvas, [host]);
            await RenderRootComponentAsync(surface.ComponentId, ParameterView.Empty);
        });

        internal Task UpdateApplicationFocusAsync(ApplicationSurfaceCapture surface) => Dispatcher.InvokeAsync(async () =>
            await (Task)typeof(DocumentCanvas).GetMethod("UpdateApplicationMenuFocusAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(surface.Canvas, null)!);

        internal Task DispatchApplicationSurfaceAsync(int id, ulong handler) => Dispatcher.InvokeAsync(async () =>
        {
            Assert.NotEqual(0UL, handler);
            _ = id;
            await DispatchEventAsync(handler, null, new MouseEventArgs());
        });
    }
#pragma warning restore BL0006
}
