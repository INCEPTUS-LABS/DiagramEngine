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
    [InlineData("simulation/", true)]
    [InlineData("simulation", true)]
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
        var simulation = path.StartsWith("simulation", StringComparison.Ordinal);
        Assert.Contains("Source Demo maintenance", markup, StringComparison.Ordinal);
        Assert.Empty(fixture.Activator.VerificationControls);
        Assert.DoesNotContain("data-verification-action", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("data-application-verification", markup, StringComparison.Ordinal);
        if (simulation)
        {
            Assert.Contains("Annotation example", markup, StringComparison.Ordinal);
            Assert.Contains("Application action example", markup, StringComparison.Ordinal);
            Assert.Equal(3, Regex.Count(AnnotationPanel(markup), "<button\\b"));
            Assert.DoesNotContain("Demonstrate failure", markup, StringComparison.Ordinal);
        }
        else
        {
            Assert.DoesNotContain("Annotation example", markup, StringComparison.Ordinal);
            Assert.DoesNotContain("Application action example", markup, StringComparison.Ordinal);
        }
        Assert.False(modeler.ApplicationContextualActionsEnabled);
        Assert.Contains("aria-label=\"Demo mode\"", markup, StringComparison.Ordinal);
        var modeGroup = Regex.Match(markup, "<nav class=\"demo-mode-navigation\"[^>]*>.*?</nav>", RegexOptions.Singleline).Value;
        Assert.Equal(3, Regex.Count(modeGroup, "<a\\b"));
        Assert.Equal(1, Regex.Count(modeGroup, "aria-current=\"page\""));
        Assert.Contains($">{(simulation ? "Simulation" : readOnly ? "ReadOnly" : "Editable")}</a>", modeGroup, StringComparison.Ordinal);
        var toolbar = ApplicationToolbar(markup, readOnly);
        Assert.Contains(">Settings</button>", toolbar, StringComparison.Ordinal);
        if (readOnly)
        {
            Assert.Single(fixture.Activator.ReadOnlyHosts);
            Assert.Empty(fixture.Activator.EditableHosts);
            Assert.Equal(3, Regex.Count(toolbar, "<button\\b"));
            Assert.Contains("Export SVG Image", toolbar, StringComparison.Ordinal);
            Assert.Contains("Export PNG Image", toolbar, StringComparison.Ordinal);
            Assert.DoesNotContain("Inceptus JSON", markup, StringComparison.Ordinal);
            Assert.DoesNotContain("type=\"file\"", markup, StringComparison.Ordinal);
            Assert.Contains("Native information", markup, StringComparison.Ordinal);
            Assert.Equal(simulation, Assert.Single(fixture.Activator.ReadOnlyHosts).SimulationExamples);
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
    [InlineData("", EditingSessionAccessMode.Editable)]
    [InlineData("read-only/", EditingSessionAccessMode.ReadOnly)]
    [InlineData("simulation/", EditingSessionAccessMode.ReadOnly)]
    public async Task DemoVerificationUnmountAndRemountUseNormalChildLifetimeWithinTheSamePage(
        string path, EditingSessionAccessMode mode)
    {
        await using var fixture = await DemoPageFixture.CreateAsync(path);
        await fixture.EnableMaintenanceAsync();
        var control = Assert.Single(fixture.Activator.VerificationControls);
        var layout = Assert.Single(fixture.Activator.Layouts);
        var original = Assert.Single(fixture.Activator.Modelers);
        var source = original.InitialDocument;
        Assert.True(control.AllowReplacement);
        Assert.True(control.UnmountRequested.HasDelegate);
        Assert.True(control.MountRequested.HasDelegate);
        var current = original;
        for (var cycle = 0; cycle < 2; cycle++)
        {
            var mountId = control.MountId;
            var generation = control.MountGeneration;
            var retiredReady = current.Ready;
            var retiredDocumentChanged = current.DocumentChanged;
            // Invoke the parent's actual compiled child callbacks. HtmlRenderer intentionally
            // does not initialize a browser surface; this proves Blazor lifetime, not renderer cleanup.
            await fixture.Renderer.Dispatcher.InvokeAsync(() => control.UnmountRequested.InvokeAsync());
            Assert.True(ModelerDisposed(current));
            Assert.False(control.Mounted);
            Assert.Null(control.Modeler);
            Assert.DoesNotContain("class=\"scope-breadcrumb\"", await fixture.MarkupAsync(), StringComparison.Ordinal);
            await fixture.Renderer.Dispatcher.InvokeAsync(() =>
                retiredReady.InvokeAsync(new BpmnModelerReadyEventArgs(A24Snapshot())));
            Assert.Equal(cycle * 2 + 1, control.RetiredCallbackCount);

            await fixture.Renderer.Dispatcher.InvokeAsync(() => control.MountRequested.InvokeAsync());
            var replacement = fixture.Activator.Modelers.Last();
            Assert.NotSame(current, replacement);
            Assert.NotSame(original, replacement);
            Assert.False(ModelerDisposed(replacement));
            Assert.Equal(mode, replacement.AccessMode);
            Assert.True(control.Mounted);
            Assert.True(control.MountGeneration > generation);
            Assert.NotEqual(mountId, control.MountId);
            Assert.Same(control, Assert.Single(fixture.Activator.VerificationControls));
            Assert.Same(layout, Assert.Single(fixture.Activator.Layouts));
            Assert.Equal(source, replacement.InitialDocument);
            await fixture.Renderer.Dispatcher.InvokeAsync(() => retiredDocumentChanged.InvokeAsync(
                new BpmnModelerDocumentChangedEventArgs(A24Snapshot(), BpmnModelerDocumentChangeKind.DocumentReplacement)));
            Assert.Equal(cycle * 2 + 2, control.RetiredCallbackCount);
            Assert.Equal(source, replacement.InitialDocument);
            Assert.Empty(fixture.Navigation.Navigations);
            current = replacement;
        }
        Assert.Equal(3, fixture.Activator.Modelers.Count);
        Assert.Single(mode == EditingSessionAccessMode.ReadOnly
            ? fixture.Activator.ReadOnlyHosts.Cast<object>() : fixture.Activator.EditableHosts.Cast<object>());
    }

    [Theory]
    [InlineData("", EditingSessionAccessMode.Editable)]
    [InlineData("simulation/", EditingSessionAccessMode.ReadOnly)]
    public async Task DemoApplicationPanelRemainsOutsideModelerAcrossDisplayModes(string path, EditingSessionAccessMode mode)
    {
        await using var fixture = await DemoPageFixture.CreateAsync(path);
        await fixture.EnableMaintenanceAsync();
        var modeler = Assert.Single(fixture.Activator.Modelers);
        var source = modeler.InitialDocument;
        var provider = modeler.ApplicationActionProvider;
        var invocation = modeler.ApplicationActionInvoked;
        foreach (var expectedMode in new[] { "normal", "maximized", "normal" })
        {
            var markup = await fixture.MarkupAsync();
            Assert.Contains($"class=\"demo-host\" data-display-mode=\"{expectedMode}\"", markup, StringComparison.Ordinal);
            var panelStart = markup.IndexOf("<details class=\"demo-application-actions\"", StringComparison.Ordinal);
            var modelerStart = markup.IndexOf("class=\"inceptus-bpmn-modeler", StringComparison.Ordinal);
            Assert.True(panelStart >= 0 && modelerStart >= 0);
            var panelEnd = markup.IndexOf("</details>", panelStart, StringComparison.Ordinal);
            Assert.True(panelEnd > panelStart);
            // Simulation examples stay in the top toolbar, Editable examples in maintenance.
            // Both remain outside the modeler and survive maximize/restore without remounting.
            Assert.True(panelEnd < modelerStart);
            if (mode == EditingSessionAccessMode.Editable)
                Assert.DoesNotContain("demo-application-actions", ApplicationToolbar(markup), StringComparison.Ordinal);
            else
            {
                var toolbar = ApplicationToolbar(markup);
                Assert.Contains("demo-application-actions", toolbar, StringComparison.Ordinal);
                Assert.Contains("demo-annotations", toolbar, StringComparison.Ordinal);
                Assert.DoesNotContain("hidden", HostToolbarTag(markup), StringComparison.Ordinal);
                var aside = markup[markup.IndexOf("<aside class=\"demo-information\"", StringComparison.Ordinal)..];
                Assert.DoesNotContain("demo-application-actions", aside, StringComparison.Ordinal);
                Assert.DoesNotContain("demo-annotations", aside, StringComparison.Ordinal);
            }
            Assert.DoesNotContain(" hidden", markup[panelStart..markup.IndexOf('>', panelStart)], StringComparison.Ordinal);
            Assert.Same(modeler, Assert.Single(fixture.Activator.Modelers));
            Assert.Equal(mode, modeler.AccessMode);
            Assert.Same(source, modeler.InitialDocument);
            Assert.Same(provider, modeler.ApplicationActionProvider);
            Assert.Equal(invocation, modeler.ApplicationActionInvoked);
            Assert.False(ModelerDisposed(modeler));
            if (mode == EditingSessionAccessMode.ReadOnly)
            {
                Assert.DoesNotContain("history-controls", markup, StringComparison.Ordinal);
                Assert.DoesNotContain("toolbox-panel", markup, StringComparison.Ordinal);
            }
            await fixture.Renderer.Dispatcher.InvokeAsync(modeler.ToggleDisplayModeAsync);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("read-only/")]
    [InlineData("simulation/")]
    public async Task DemoDisplayModeRoundTripPreservesThePageAndModeler(string path)
    {
        await using var fixture = await DemoPageFixture.CreateAsync(path);
        var modeler = Assert.Single(fixture.Activator.Modelers);
        var layout = Assert.Single(fixture.Activator.Layouts);
        var provider = modeler.ApplicationActionProvider;
        var invocation = modeler.ApplicationActionInvoked;
        for (var index = 0; index < 3; index++)
        {
            await fixture.Renderer.Dispatcher.InvokeAsync(modeler.ToggleDisplayModeAsync);
            var maximized = await fixture.MarkupAsync();
            if (path == "simulation/")
            {
                Assert.DoesNotContain("hidden", HostToolbarTag(maximized), StringComparison.Ordinal);
                Assert.Contains("hidden", ApplicationToolbar(maximized, true).Split('>')[0], StringComparison.Ordinal);
            }
            else Assert.Contains("hidden", HostToolbarTag(maximized), StringComparison.Ordinal);
            Assert.Contains("class=\"demo-host\" data-display-mode=\"maximized\"", maximized, StringComparison.Ordinal);
            Assert.Same(modeler, Assert.Single(fixture.Activator.Modelers));
            Assert.Same(layout, Assert.Single(fixture.Activator.Layouts));
            // Ordinary parent renders must not re-register the application callback and
            // thereby retire an otherwise current application-action request.
            Assert.Same(provider, modeler.ApplicationActionProvider);
            Assert.Equal(invocation, modeler.ApplicationActionInvoked);
            await fixture.Renderer.Dispatcher.InvokeAsync(modeler.ToggleDisplayModeAsync);
            Assert.DoesNotContain("hidden", HostToolbarTag(await fixture.MarkupAsync()), StringComparison.Ordinal);
        }
        Assert.False(ModelerDisposed(modeler));
    }

    private static bool ModelerDisposed(InceptusBpmnModeler modeler) =>
        Assert.IsType<bool>(typeof(InceptusBpmnModeler).GetField("_disposed", PropertiesUxFlags)!.GetValue(modeler));

    private static string ApplicationToolbar(string markup, bool documentActionsOnly = false)
    {
        var start = markup.IndexOf(documentActionsOnly ? "<div class=\"demo-document-actions\"" : "<div class=\"editor-toolbar host-toolbar\"", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var depth = 0;
        foreach (Match tag in Regex.Matches(markup[start..], "</?div\\b[^>]*>"))
        {
            depth += tag.Value.StartsWith("</", StringComparison.Ordinal) ? -1 : 1;
            if (depth == 0) return markup[start..(start + tag.Index + tag.Length)];
        }
        throw new InvalidOperationException("The application toolbar has no matching closing element.");
    }

    private static string AnnotationPanel(string markup)
    {
        var start = markup.IndexOf("<details class=\"demo-annotations\"", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var end = markup.IndexOf("</details>", start, StringComparison.Ordinal);
        Assert.True(end > start);
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
            services.AddSingleton(_ => new HttpClient { BaseAddress = new Uri("https://demo.invalid/") });
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

        internal Task EnableMaintenanceAsync() => Renderer.Dispatcher.InvokeAsync(() =>
            Assert.Single(Activator.Maintenance).EnabledChanged.InvokeAsync(true));

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
        internal List<DemoMaintenance> Maintenance { get; } = [];
        internal List<DemoVerificationControls> VerificationControls { get; } = [];

        public IComponent CreateInstance(Type componentType)
        {
            var component = (IComponent)System.Activator.CreateInstance(componentType)!;
            if (component is BpmnDemoHost editor) EditableHosts.Add(editor);
            if (component is ReadOnlyBpmnDemoHost viewer) ReadOnlyHosts.Add(viewer);
            if (component is InceptusBpmnModeler modeler) Modelers.Add(modeler);
            if (component is DemoApplicationLayout layout) Layouts.Add(layout);
            if (component is DemoMaintenance maintenance) Maintenance.Add(maintenance);
            if (component is DemoVerificationControls verification) VerificationControls.Add(verification);
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
