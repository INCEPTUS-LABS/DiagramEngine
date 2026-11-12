using Inceptus.DiagramEngine.Bpmn.Blazor;
using Inceptus.DiagramEngine.Bpmn.Blazor.Components;
using Inceptus.DiagramEngine.Canvas2D.EditingSession;
using Inceptus.DiagramEngine.Canvas2D.Rendering;
using Inceptus.DiagramEngine.Contracts.Documents;
using Inceptus.DiagramEngine.Contracts.Primitives;
using Inceptus.DiagramEngine.Contracts.Routing;
using Inceptus.DiagramEngine.Contracts.Visuals;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Inceptus.DiagramEngine.Blazor.Demo;

/// <summary>Read-only Demo page using the supported immutable presentation and document APIs.</summary>
public partial class ReadOnlyBpmnDemoHost
{
    [Parameter] public bool OrderProcess { get; set; }
    [Inject] private IJSRuntime JavaScript { get; set; } = null!;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly string _identity = "inspection-" + Guid.NewGuid().ToString("N");
    private DocumentSnapshot? _source;
    private BpmnModelerDisplayMode _displayMode;
    private ElementReference _settingsButton;
    private bool _detailsOpen;
    private bool _disposed;
    internal InceptusBpmnModeler? Modeler { get; set; }
    internal BpmnPresentationSnapshot? Presentation { get; private set; }
    internal string ApplicationDetails { get; private set; } = "Choose Inspect details from an element's application actions.";
    internal string Feedback { get; private set; } = "Loading the built-in sample…";
    private bool Busy { get; set; }
    private bool CanOperate => !Busy && !_disposed && Modeler is not null && Presentation?.IsAvailable == true;
    private string MeasurementId => _identity + "-measurement";

    protected override async Task OnInitializedAsync()
    {
        if (OrderProcess) return;
        var document = await BpmnPoolDemoStartupDocumentProvider.Instance.GetInitialDocumentAsync(_lifetime.Token);
        _source = document.CaptureSnapshot();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        // The Order Process fixture needs browser font measurements before strict read-only attachment.
        if (!firstRender || !OrderProcess || _disposed) return;
        await LoadSampleAsync(true);
        if (!_disposed) StateHasChanged();
    }

    private Task OpenSettingsAsync() => CanOperate
        ? Modeler!.OpenSettingsAsync(RestoreSettingsFocusAsync)
        : Task.CompletedTask;

    private async Task RestoreSettingsFocusAsync()
    {
        if (!_disposed) await _settingsButton.FocusAsync();
    }

    private void CloseDetails() => _detailsOpen = false;

    private void OnReady(BpmnModelerReadyEventArgs args)
    {
        if (_disposed) return;
        _source = args.Snapshot;
        Feedback = "Ready. The viewer preserves the supplied native snapshot.";
        RefreshPresentation();
    }

    private void OnDocumentChanged(BpmnModelerDocumentChangedEventArgs args)
    {
        if (_disposed) return;
        _source = args.Snapshot;
        ApplicationDetails = "Document replaced. Select an element to inspect it.";
        RefreshPresentation();
    }

    private void OnSelectionChanged(BpmnSelectionChangedEventArgs args)
    {
        if (!_disposed) Presentation = args.Presentation;
    }

    internal void RefreshPresentation()
    {
        if (_disposed) return;
        var current = Modeler?.CapturePresentation();
        // ViewChanged can run while an equivalent frame is still being presented. Keep
        // the last coherent display; selection notifications and fresh operation stamps remain authoritative.
        if (current is { IsAvailable: false } && Presentation?.Stamp is { } previous &&
            Modeler?.CaptureEditorContext() is { } live && live.AttachmentVersion == previous.AttachmentVersion &&
            live.Document.DocumentId == previous.DocumentId && live.Document.Revision == previous.Revision &&
            Equals(live.Presentation.PresentationContext, previous.PresentationContext) &&
            live.Presentation.Status is EditingSessionStatus.Ready or EditingSessionStatus.Rebuilding &&
            !live.Presentation.IsCurrentScenePresented) return;
        Presentation = current;
    }

    private void OnFailure(BpmnModelerOperationFailedEventArgs args)
    {
        if (_disposed) return;
        Feedback = string.Join("; ", args.Diagnostics.Select(item => item.Code + ": " + item.Message));
        _detailsOpen = true;
    }

    internal static async ValueTask<IEnumerable<BpmnApplicationAction>> DiscoverActionsAsync(
        BpmnApplicationActionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        // Stand-in for a cancellation-aware application lookup; no native editing command is returned.
        await Task.Delay(150, cancellationToken);
        return [new("example:inspect", "Inspect details"),
            new("example:linked-record", "Open linked record", false, "No external application is connected in this example.")];
    }

    internal Task InspectAsync(BpmnApplicationActionInvocationEventArgs args)
    {
        if (_disposed || args.CancellationToken.IsCancellationRequested || args.ActionId != "example:inspect" ||
            Modeler?.CapturePresentation().Stamp != args.Context.Stamp) return Task.CompletedTask;
        ApplicationDetails = $"Application inspected {args.Context.Target.Kind}: {args.Context.Target.SemanticId.Value}" +
            (args.Context.Part is { } part ? $" ({part.Kind})." : ".");
        _detailsOpen = true;
        return Task.CompletedTask;
    }

    internal async Task NavigateAsync(DocumentScopeId scopeId)
    {
        if (!CanOperate || Modeler is not { } modeler) return;
        var current = modeler.CapturePresentation();
        if (current.Stamp is not { } stamp) return;
        var result = await modeler.NavigateToScopeAsync(stamp, scopeId, _lifetime.Token);
        if (_disposed) return;
        Presentation = result.Presentation;
        Feedback = result.Succeeded ? "Scope opened without changing the document." : $"Navigation: {result.Status}.";
    }

    internal async Task ReplaceAsync(DocumentSnapshot snapshot)
    {
        if (_disposed) return;
        if (Modeler is not { } modeler)
        {
            _source = snapshot;
            return;
        }
        var result = await modeler.SetDocumentAsync(snapshot,
            new BpmnModelerDocumentLoadOptions { VerifySavedRouting = true }, _lifetime.Token);
        if (_disposed) return;
        if (result.Succeeded) _source = result.Snapshot;
        RefreshPresentation();
        Feedback = result.Succeeded ? "Snapshot replaced. Previous selections and actions are retired." :
            "The previous attachment was retained. " + string.Join("; ", result.Diagnostics.Select(item => item.Code));
    }

    private async Task LoadSampleAsync(bool orderProcess)
    {
        if (_disposed || Busy) return;
        Busy = true;
        try
        {
            var document = orderProcess
                ? await BpmnDemoStartupDocumentProvider.Instance.GetInitialDocumentAsync(_lifetime.Token)
                : await BpmnPoolDemoStartupDocumentProvider.Instance.GetInitialDocumentAsync(_lifetime.Token);
            var snapshot = document.CaptureSnapshot();
            if (orderProcess)
            {
                // Author the built-in sample before attachment. Imported native files never use this preparation path.
                Feedback = "Preparing the built-in Order Process sample…";
                await using var renderer = new Canvas2DRenderer(JavaScript, DemoBpmnImportPreparation.RendererConfiguration);
                var initialized = await renderer.InitializeAsync(MeasurementId, new(1, 1, 1), _lifetime.Token);
                if (!initialized.Succeeded) { Feedback = "Sample measurement is unavailable."; return; }
                var prepared = await DemoBpmnImportPreparation.Create(renderer).PrepareAsync(
                    new(snapshot, snapshot, [], [], null, false, ConnectorRoutingPreparationPurpose.RecoverSavedState), _lifetime.Token);
                if (!prepared.Succeeded) { Feedback = "The built-in sample could not be prepared."; return; }
                snapshot = new(snapshot.SemanticModel,
                    new VisualModelSnapshot(snapshot.DocumentId, snapshot.Revision, snapshot.VisualModel.VisualStates,
                        snapshot.VisualModel.ProfileElementPresentations, prepared.RoutingScopes, prepared.DocumentRoutingStates),
                    snapshot.Metadata, snapshot.Publication);
            }
            await ReplaceAsync(snapshot);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (JSException) { if (!_disposed) Feedback = "Sample preparation is unavailable. The previous attachment was retained."; }
        finally { Busy = false; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        _lifetime.Dispose();
        GC.SuppressFinalize(this);
    }
}
