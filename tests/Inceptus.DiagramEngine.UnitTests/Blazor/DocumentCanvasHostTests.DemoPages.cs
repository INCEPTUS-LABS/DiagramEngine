using System.Text.RegularExpressions;
using Inceptus.DiagramEngine.Blazor.Demo;
using Inceptus.DiagramEngine.Bpmn.Blazor;
using Inceptus.DiagramEngine.Bpmn.Blazor.Components;
using Inceptus.DiagramEngine.Canvas2D.EditingSession;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Web.HtmlRendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;

namespace Inceptus.DiagramEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
    [Theory]
    [InlineData("", false)]
    [InlineData("read-only/", true)]
    [InlineData("read-only", true)]
    public async Task DemoRoutesRenderTheMatchingModeInTheSharedApplicationLayout(string path, bool readOnly)
    {
        using var culture = new ModelerCultureScope("en");
        await using var fixture = await DemoPageFixture.CreateAsync(path);
        var markup = await fixture.MarkupAsync();
        Assert.Single(fixture.Activator.Layouts);
        var modeler = Assert.Single(fixture.Activator.Modelers);
        Assert.Equal(readOnly ? EditingSessionAccessMode.ReadOnly : EditingSessionAccessMode.Editable, modeler.AccessMode);
        Assert.Contains("class=\"demo-host\" data-display-mode=\"normal\"", markup, StringComparison.Ordinal);
        Assert.Contains("class=\"display-mode-toggle\"", markup, StringComparison.Ordinal);
        Assert.Contains("class=\"toolbar-group zoom-controls\"", markup, StringComparison.Ordinal);
        Assert.Contains("class=\"scope-breadcrumb\"", markup, StringComparison.Ordinal);
        var toolbar = ApplicationToolbar(markup);
        Assert.Contains(">Settings</button>", toolbar, StringComparison.Ordinal);
        if (readOnly)
        {
            Assert.Single(fixture.Activator.ReadOnlyHosts);
            Assert.Empty(fixture.Activator.EditableHosts);
            Assert.Single(Regex.Matches(toolbar, "<button\\b"));
            Assert.DoesNotContain("<input", toolbar, StringComparison.Ordinal);
            Assert.DoesNotContain("<select", toolbar, StringComparison.Ordinal);
            Assert.DoesNotContain("@onclick", markup, StringComparison.Ordinal);
            Assert.DoesNotContain("history-controls", markup, StringComparison.Ordinal);
            Assert.DoesNotContain("layout-controls", markup, StringComparison.Ordinal);
            Assert.DoesNotContain("toolbox-panel", markup, StringComparison.Ordinal);
            Assert.NotNull(modeler.ApplicationActionProvider);
            Assert.True(modeler.ApplicationActionInvoked.HasDelegate);
            Assert.True(modeler.SelectionChanged.HasDelegate);
        }
        else
        {
            Assert.Single(fixture.Activator.EditableHosts);
            Assert.Empty(fixture.Activator.ReadOnlyHosts);
            Assert.Contains("Import", toolbar, StringComparison.Ordinal);
            Assert.Contains("Export", toolbar, StringComparison.Ordinal);
            Assert.Contains("Validate", toolbar, StringComparison.Ordinal);
            Assert.Contains("publish-action", toolbar, StringComparison.Ordinal);
            Assert.Contains("history-controls", markup, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("?example=read-only", "read-only/", false)]
    [InlineData("?example=read-only&sample=order", "read-only/?sample=order", true)]
    public async Task DemoLegacyLinkRedirectsWithoutCreatingAnEditableModeler(string path, string expected, bool order)
    {
        await using var fixture = await DemoPageFixture.CreateAsync(path);
        Assert.Equal(new Uri(new Uri(fixture.Navigation.BaseUri), expected).AbsoluteUri, fixture.Navigation.Uri);
        Assert.True(Assert.Single(fixture.Navigation.Navigations).ReplaceHistoryEntry);
        Assert.Empty(fixture.Activator.EditableHosts);
        Assert.Equal(order, Assert.Single(fixture.Activator.ReadOnlyHosts).OrderProcess);
    }

    [Fact]
    public async Task DemoRouteChangesDisposeThePreviousModeAndCreateAnIndependentModeler()
    {
        await using var fixture = await DemoPageFixture.CreateAsync("");
        var editor = Assert.Single(fixture.Activator.Modelers);
        await fixture.NavigateAsync("read-only/");
        var viewer = fixture.Activator.Modelers.Last();
        Assert.NotSame(editor, viewer);
        Assert.True(ModelerDisposed(editor));
        Assert.False(ModelerDisposed(viewer));
        Assert.Equal(EditingSessionAccessMode.ReadOnly, viewer.AccessMode);
        var retiredHost = Assert.Single(fixture.Activator.ReadOnlyHosts);
        await fixture.NavigateAsync("");
        Assert.True(ModelerDisposed(viewer));
        Assert.True(Assert.IsType<bool>(typeof(ReadOnlyBpmnDemoHost).GetField("_disposed", PropertiesUxFlags)!.GetValue(retiredHost)));
        var replacement = fixture.Activator.Modelers.Last();
        Assert.NotSame(editor, replacement);
        Assert.NotSame(viewer, replacement);
        Assert.Equal(EditingSessionAccessMode.Editable, replacement.AccessMode);
    }

    [Fact]
    public async Task DemoClientNavigationToLegacyLinkRetiresTheEditorWithoutCreatingAnother()
    {
        await using var fixture = await DemoPageFixture.CreateAsync("");
        var editor = Assert.Single(fixture.Activator.Modelers);
        var editableHost = Assert.Single(fixture.Activator.EditableHosts);
        await fixture.NavigateAsync("?example=read-only", "read-only/");
        Assert.Equal("https://demo.invalid/read-only/", fixture.Navigation.Uri);
        Assert.True(fixture.Navigation.Navigations.Last().ReplaceHistoryEntry);
        Assert.Same(editableHost, Assert.Single(fixture.Activator.EditableHosts));
        Assert.True(ModelerDisposed(editor));
        Assert.Single(fixture.Activator.ReadOnlyHosts);
        Assert.Equal(2, fixture.Activator.Modelers.Count);
        var viewer = fixture.Activator.Modelers.Last();
        Assert.Equal(EditingSessionAccessMode.ReadOnly, viewer.AccessMode);
        Assert.False(ModelerDisposed(viewer));
    }

    [Fact]
    public async Task DemoSampleQueryChangeRetiresThePreviousReadOnlyAttachment()
    {
        await using var fixture = await DemoPageFixture.CreateAsync("read-only/");
        var viewer = Assert.Single(fixture.Activator.Modelers);
        var first = Assert.Single(fixture.Activator.ReadOnlyHosts);
        await fixture.NavigateAsync("read-only/?sample=order");
        var second = fixture.Activator.ReadOnlyHosts.Last();
        Assert.NotSame(first, second);
        Assert.True(second.OrderProcess);
        Assert.True(ModelerDisposed(viewer));
        Assert.True(Assert.IsType<bool>(typeof(ReadOnlyBpmnDemoHost).GetField("_disposed", PropertiesUxFlags)!.GetValue(first)));
        await fixture.NavigateAsync("read-only/");
        Assert.False(fixture.Activator.ReadOnlyHosts.Last().OrderProcess);
        Assert.NotSame(second, fixture.Activator.ReadOnlyHosts.Last());
    }

    [Theory]
    [InlineData("")]
    [InlineData("read-only/")]
    public async Task DemoDisplayModeRoundTripPreservesThePageAndModeler(string path)
    {
        await using var fixture = await DemoPageFixture.CreateAsync(path);
        var modeler = Assert.Single(fixture.Activator.Modelers);
        var layout = Assert.Single(fixture.Activator.Layouts);
        for (var index = 0; index < 3; index++)
        {
            await fixture.Renderer.Dispatcher.InvokeAsync(modeler.ToggleDisplayModeAsync);
            var maximized = await fixture.MarkupAsync();
            Assert.Contains("hidden", HostToolbarTag(maximized), StringComparison.Ordinal);
            Assert.Contains("class=\"demo-host\" data-display-mode=\"maximized\"", maximized, StringComparison.Ordinal);
            Assert.Same(modeler, Assert.Single(fixture.Activator.Modelers));
            Assert.Same(layout, Assert.Single(fixture.Activator.Layouts));
            await fixture.Renderer.Dispatcher.InvokeAsync(modeler.ToggleDisplayModeAsync);
            Assert.DoesNotContain("hidden", HostToolbarTag(await fixture.MarkupAsync()), StringComparison.Ordinal);
        }
        Assert.False(ModelerDisposed(modeler));
    }

    private static bool ModelerDisposed(InceptusBpmnModeler modeler) =>
        Assert.IsType<bool>(typeof(InceptusBpmnModeler).GetField("_disposed", PropertiesUxFlags)!.GetValue(modeler));

    private static string ApplicationToolbar(string markup)
    {
        var start = markup.IndexOf("<div class=\"editor-toolbar host-toolbar\"", StringComparison.Ordinal);
        var end = markup.IndexOf("<canvas", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        return markup[start..end];
    }

    private sealed class DemoPageFixture : IAsyncDisposable
    {
        private readonly ServiceProvider _services;
        private HtmlRootComponent _root;
        internal DemoPageActivator Activator { get; }
        internal DemoNavigation Navigation { get; }
        internal HtmlRenderer Renderer { get; }

        private DemoPageFixture(string path)
        {
            Activator = new();
            Navigation = new(path);
            var services = new ServiceCollection().AddLogging().AddInceptusBpmnModeler();
            services.AddSupplyValueFromQueryProvider();
            _services = services.AddSingleton<IJSRuntime>(new GridStorageRuntime())
                .AddSingleton<NavigationManager>(Navigation)
                .AddSingleton<INavigationInterception, DemoNavigationInterception>()
                .AddSingleton<IScrollToLocationHash, DemoScrollToLocationHash>()
                .AddSingleton<IComponentActivator>(Activator).BuildServiceProvider();
            Renderer = new(_services, NullLoggerFactory.Instance);
        }

        internal static async Task<DemoPageFixture> CreateAsync(string path)
        {
            var fixture = new DemoPageFixture(path);
            fixture._root = await fixture.Renderer.Dispatcher.InvokeAsync(() =>
                fixture.Renderer.RenderComponentAsync<Inceptus.DiagramEngine.Blazor.App>());
            return fixture;
        }

        internal Task<string> MarkupAsync() => Renderer.Dispatcher.InvokeAsync(_root.ToHtmlString);

        internal async Task NavigateAsync(string path, string? redirectedDestination = null)
        {
            if (redirectedDestination is null)
            {
                await Renderer.Dispatcher.InvokeAsync(() => Navigation.NavigateTo(path));
                return;
            }

            var expected = Navigation.ToAbsoluteUri(redirectedDestination).AbsoluteUri;
            var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnLocationChanged(object? sender, LocationChangedEventArgs args)
            {
                if (args.Location == expected) arrived.TrySetResult();
            }
            Navigation.LocationChanged += OnLocationChanged;
            try
            {
                await Renderer.Dispatcher.InvokeAsync(() => Navigation.NavigateTo(path));
                await arrived.Task.WaitAsync(TimeSpan.FromSeconds(5));
                // The redirect is asynchronous. Observe the completed destination notification
                // and then read component identities on the renderer's own dispatcher.
                await Renderer.Dispatcher.InvokeAsync(() => { });
            }
            finally { Navigation.LocationChanged -= OnLocationChanged; }
        }

        public async ValueTask DisposeAsync()
        {
            await Renderer.DisposeAsync();
            await _services.DisposeAsync();
        }
    }

    private sealed class DemoPageActivator : IComponentActivator
    {
        internal List<BpmnDemoHost> EditableHosts { get; } = [];
        internal List<ReadOnlyBpmnDemoHost> ReadOnlyHosts { get; } = [];
        internal List<InceptusBpmnModeler> Modelers { get; } = [];
        internal List<DemoApplicationLayout> Layouts { get; } = [];

        public IComponent CreateInstance(Type componentType)
        {
            var component = (IComponent)System.Activator.CreateInstance(componentType)!;
            if (component is BpmnDemoHost editor) EditableHosts.Add(editor);
            if (component is ReadOnlyBpmnDemoHost viewer) ReadOnlyHosts.Add(viewer);
            if (component is InceptusBpmnModeler modeler) Modelers.Add(modeler);
            if (component is DemoApplicationLayout layout) Layouts.Add(layout);
            return component;
        }
    }

    private sealed class DemoNavigation : NavigationManager
    {
        internal List<NavigationOptions> Navigations { get; } = [];

        internal DemoNavigation(string path) => Initialize("https://demo.invalid/", "https://demo.invalid/" + path);

        protected override void NavigateToCore(string uri, NavigationOptions options)
        {
            Uri = ToAbsoluteUri(uri).AbsoluteUri;
            Navigations.Add(options);
            NotifyLocationChanged(false);
        }

        protected override void NavigateToCore(string uri, bool forceLoad) =>
            NavigateToCore(uri, new NavigationOptions { ForceLoad = forceLoad });
    }

    private sealed class DemoNavigationInterception : INavigationInterception
    {
        public Task EnableNavigationInterceptionAsync() => Task.CompletedTask;
    }

    private sealed class DemoScrollToLocationHash : IScrollToLocationHash
    {
        public Task RefreshScrollPositionForHash(string locationAbsolute) => Task.CompletedTask;
    }
}
