using System.Globalization;
using System.Net;
using Inceptus.DiagramEngine.Blazor.Demo;
using Inceptus.DiagramEngine.Bpmn.Blazor;
using Inceptus.DiagramEngine.Canvas2D.EditingSession;
using Inceptus.DiagramEngine.Runtime.Documents;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.Extensions.DependencyInjection;

namespace Inceptus.DiagramEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
    [Theory]
    [InlineData(EditingSessionAccessMode.Editable, false)]
    [InlineData(EditingSessionAccessMode.ReadOnly, false)]
    [InlineData(EditingSessionAccessMode.Editable, true)]
    [InlineData(EditingSessionAccessMode.ReadOnly, true)]
    public async Task DemoLabelBindingsRefreshBothSurfacesWithoutChangingNativeState(EditingSessionAccessMode mode, bool catalog)
    {
        using var culture = new ModelerCultureScope("en");
        await using var fixture = await ReadOnlyDemoFixture.CreateAsync(mode);
        using var http = new CatalogHttpHandler();
        using var client = new HttpClient(http) { BaseAddress = new Uri("https://demo.invalid/") };
        var modeler = fixture.Renderer.Modeler;
        using var example = new DemoApplicationActionExample(() => modeler, client) { ToolbarEnabled = true, UseTypedHandler = true };
        example.SetLabels(DemoApplicationLabels.Polish);
        Assert.Equal(DemoApplicationLabels.Default, example.Labels);
        var component = await fixture.Renderer.RenderSmartVerificationAsync(example);
        await fixture.Renderer.ChangeSmartVerificationAsync(component, "enabled", true);
        await fixture.Renderer.ChangeSmartVerificationAsync(component, "catalog", catalog);
        example.SetAdditionalActions(true);
        await fixture.Renderer.ChangeSmartVerificationAsync(component, "labels", "Default");
        Assert.True((await modeler.SelectVisualAsync(modeler.CapturePresentation().Stamp!, BpmnDemoPipeline.TaskVisualId)).Succeeded);
        var before = Session(fixture.Host).CaptureState();
        var source = modeler.GetDocument().Snapshot!;
        var bytes = NativeDocumentSerializer.Export(source);
        var stamp = modeler.CapturePresentation().Stamp;
        var provider = modeler.ApplicationActionProvider;
        var handler = modeler.ApplicationActionHandler;
        var original = await SmartToolbarAsync(fixture.Host);
        var ids = original.Actions.Select(action => action.Id).ToArray();
        var icon = original.Actions[0].Icon;
        var labels = new[] { (DemoApplicationLabels.Expanded, "Inspect the complete application details for the currently selected diagram representation"),
            (DemoApplicationLabels.German, "Details anzeigen"), (DemoApplicationLabels.Spanish, "Inspeccionar detalles"),
            (DemoApplicationLabels.French, "Examiner les détails"), (DemoApplicationLabels.Polish, "Sprawdź szczegóły"),
            (DemoApplicationLabels.Default, catalog ? "Catalogue inspection" : "Inspect details") };
        foreach (var (preset, expected) in labels)
        {
            var old = await SmartToolbarAsync(fixture.Host);
            var revision = example.Revision;
            await fixture.Renderer.ChangeSmartVerificationAsync(component, "labels", preset.ToString());
            Assert.Equal(revision + 1, example.Revision);
            Assert.Equal(preset, example.Labels);
            Assert.False((await fixture.Host.ExecuteApplicationActionAsync(old.Context.RequestVersion, "example:inspect")).Succeeded);
            Assert.Equal(0, example.InvocationCount);
            var toolbar = await SmartToolbarAsync(fixture.Host);
            Assert.Equal(ids, toolbar.Actions.Select(action => action.Id));
            Assert.Equal(expected, toolbar.Actions[0].Label);
            Assert.Equal(icon, toolbar.Actions[0].Icon);
            Assert.False(toolbar.Actions[1].IsEnabled);
            Assert.False(string.IsNullOrWhiteSpace(toolbar.Actions[1].DisabledExplanation));
            var surface = await fixture.Renderer.CaptureApplicationSurfaceAsync(fixture.Host);
            Assert.Contains(expected, surface.Labels);
            Assert.Contains(expected, surface.Titles);
            Assert.DoesNotContain(expected, surface.Text);
            await fixture.Host.OpenApplicationContextMenuFromSelectionAsync();
            var menu = await ApplicationMenuAsync(fixture.Host);
            Assert.Equal(toolbar.Actions.Select(action => (action.Id, action.Label, action.IsEnabled, action.DisabledExplanation, action.Icon)),
                menu.ApplicationActions.Select(action => (action.Id, action.Label, action.IsEnabled, action.DisabledExplanation, action.Icon)));
            var popup = await fixture.Renderer.CaptureApplicationSurfaceAsync(fixture.Host);
            Assert.Contains(expected, popup.Text);
            Assert.Contains(toolbar.Actions[1].DisabledExplanation!, popup.Text);
            fixture.Host.CloseContextMenu();
        }
        var unchanged = example.Revision;
        foreach (var value in new[] { "Default", "not-a-preset", "999" })
            await fixture.Renderer.ChangeSmartVerificationAsync(component, "labels", value);
        Assert.Equal(unchanged, example.Revision);
        await fixture.Renderer.ChangeSmartVerificationAsync(component, "labels", "Polish");
        example.SetAvailability(false);
        await fixture.Renderer.ChangeSmartVerificationAsync(component, "labels", "Polish");
        var unavailable = await SmartToolbarAsync(fixture.Host);
        Assert.False(unavailable.Actions[0].IsEnabled);
        Assert.Equal("Sprawdzanie jest wyłączone przez bieżącą politykę aplikacji.", unavailable.Actions[0].DisabledExplanation);
        await fixture.Renderer.ChangeSmartVerificationAsync(component, "enabled", false);
        Assert.Equal(DemoApplicationLabels.Default, example.Labels);
        Assert.Same(provider, modeler.ApplicationActionProvider);
        Assert.Same(handler, modeler.ApplicationActionHandler);
        Assert.Same(modeler, fixture.Renderer.Modeler);
        Assert.Equal(mode, modeler.AccessMode);
        Assert.Equal(stamp, modeler.CapturePresentation().Stamp);
        Assert.Same(source, modeler.GetDocument().Snapshot);
        Assert.True(bytes.AsSpan().SequenceEqual(NativeDocumentSerializer.Export(source).AsSpan()));
        var after = Session(fixture.Host).CaptureState();
        Assert.Equal(before.HistoryStatus, after.HistoryStatus);
        Assert.Equal(before.DocumentRevision, after.DocumentRevision);
        Assert.Same(before.ProjectedGraph, after.ProjectedGraph);
        Assert.Same(before.LayoutResult, after.LayoutResult);
        Assert.Same(before.RoutingResult, after.RoutingResult);
        Assert.Empty(fixture.DocumentChanges);
        Assert.Empty(fixture.Renderer.Exceptions);
        Assert.Equal(catalog, http.Requests.Count > 0);
    }

    [Theory]
    [InlineData("", "en")]
    [InlineData("?uiCulture=de", "de")]
    [InlineData("?uiCulture=es", "es")]
    [InlineData("?uiCulture=fr", "fr")]
    [InlineData("?sample=message&uiCulture=pl#target", "pl")]
    [InlineData("?uiCulture=", "en")]
    [InlineData("?uiCulture=it", "en")]
    [InlineData("?uiCulture=fr&uiCulture=de", "en")]
    public void DemoCultureStartupAcceptsOnlyOneSupportedInput(string query, string expected)
    {
        Assert.Equal(expected, DemoUiCulture.Resolve("https://demo.invalid/read-only/" + query).Name);
    }

    [Theory]
    [InlineData("", "de", "Vergrößern")]
    [InlineData("read-only/", "de", "Vergrößern")]
    [InlineData("", "es", "Acercar")]
    [InlineData("read-only/", "es", "Acercar")]
    [InlineData("", "fr", "Zoom avant")]
    [InlineData("read-only/", "fr", "Zoom avant")]
    [InlineData("", "pl", "Powiększ")]
    [InlineData("read-only/", "pl", "Powiększ")]
    [InlineData("simulation/", "pl", "Powiększ")]
    public async Task DemoCultureHostsExposeActiveCultureAndExistingNativeTranslations(string path, string cultureName, string zoom)
    {
        using var culture = new ModelerCultureScope(DemoUiCulture.Resolve("https://demo.invalid/?uiCulture=" + cultureName).Name);
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        await using var fixture = await DemoPageFixture.CreateAsync(path + "?uiCulture=" + cultureName);
        var modeler = Assert.Single(fixture.Activator.Modelers);
        Assert.DoesNotContain("data-demo-active-ui-culture", await fixture.MarkupAsync(), StringComparison.Ordinal);
        await fixture.EnableMaintenanceAsync();
        await fixture.Renderer.Dispatcher.InvokeAsync(modeler.ToggleDisplayModeAsync);
        var markup = WebUtility.HtmlDecode(await fixture.MarkupAsync());
        Assert.Contains($"data-demo-active-ui-culture=\"{cultureName}\"", markup, StringComparison.Ordinal);
        Assert.Contains("data-demo-format-culture=\"\"", markup, StringComparison.Ordinal);
        Assert.Contains(zoom, markup, StringComparison.Ordinal);
        Assert.Equal(path.Length == 0 ? EditingSessionAccessMode.Editable : EditingSessionAccessMode.ReadOnly, modeler.AccessMode);
        Assert.Same(modeler, Assert.Single(fixture.Activator.Modelers));
    }

    [Theory]
    [InlineData("?keep=yes#target")]
    [InlineData("read-only/?sample=message&uiCulture=de#target")]
    public async Task DemoCultureBindingReloadsSameRouteAndPreservesOtherInputs(string path)
    {
        using var culture = new ModelerCultureScope("en");
        var navigation = new DemoNavigation(path);
        using var services = new ServiceCollection().AddLogging().AddSingleton<NavigationManager>(navigation).BuildServiceProvider();
        await using var renderer = new ApplicationComponentRenderer(services);
        await renderer.ChangeDemoCultureAsync("unsupported");
        await renderer.ChangeDemoCultureAsync("en");
        Assert.Empty(navigation.Navigations);
        await renderer.ChangeDemoCultureAsync("fr");
        Assert.True(Assert.Single(navigation.Navigations).ForceLoad);
        Assert.Contains("uiCulture=fr", navigation.Uri, StringComparison.Ordinal);
        Assert.EndsWith("#target", navigation.Uri, StringComparison.Ordinal);
        Assert.Contains(path.StartsWith("read-only", StringComparison.Ordinal) ? "read-only/?sample=message" : "?keep=yes", navigation.Uri, StringComparison.Ordinal);
        Assert.Empty(renderer.Exceptions);
    }

#pragma warning disable BL0006 // Exercise the actual compiled selector binding in the canonical renderer.
    private sealed partial class ApplicationComponentRenderer
    {
        internal Task ChangeDemoCultureAsync(string value) => Dispatcher.InvokeAsync(async () =>
        {
            var component = InstantiateComponent(typeof(DemoCultureControls));
            var id = AssignRootComponentId(component);
            await RenderRootComponentAsync(id, ParameterView.Empty);
            var attributes = AnnotationAttributes(id, "select", "aria-label", "Modeler UI culture");
            var handler = Assert.Single(attributes, attribute => attribute.AttributeName == "onchange").AttributeEventHandlerId;
            Assert.NotEqual(0UL, handler);
            await DispatchEventAsync(handler, null, new ChangeEventArgs { Value = value });
        });
    }
#pragma warning restore BL0006
}
