using System.Text.Json;
using Inceptus.DiagramEngine.Bpmn.Blazor;
using Inceptus.DiagramEngine.Contracts.Diagnostics;

namespace Inceptus.DiagramEngine.Blazor.Demo;

public sealed partial class DemoApplicationActionExample
{
    private readonly HttpClient? _catalogClient;
    private readonly Func<long> _mountGeneration;
    private readonly CancellationTokenSource _catalogLifetime = new();
    private readonly CancellationToken _catalogToken;
    private readonly DemoApplicationActionJournal _catalogJournal = new();
    private bool _catalogDisposed;
    public bool AsyncCatalogEnabled { get; private set; }
    internal DemoActionJournalSnapshot? CatalogObservations { get; private set; }
    public string CatalogFeedback { get; private set; } = "No application catalogue load observed.";
    private bool UseAsyncCatalog => VerificationEnabled && AsyncCatalogEnabled;

    public void SetAsyncCatalogEnabled(bool enabled)
    {
        if (!VerificationEnabled || AsyncCatalogEnabled == enabled) return;
        AsyncCatalogEnabled = enabled;
        AdvanceRevision();
    }

    public void ReadCatalogObservations()
    {
        CatalogObservations = _catalogJournal.Read();
        Changed?.Invoke();
    }

    private Task<IEnumerable<BpmnApplicationAction>> DiscoverCatalogAsync(BpmnApplicationActionContext context, CancellationToken token)
    {
        var attempt = _catalogJournal.Begin("Provider", context, null, _mountGeneration(), token, _catalogToken);
        return attempt.TrackConsumer(DiscoverCatalogCoreAsync(context, attempt, token));
    }

    private async Task<IEnumerable<BpmnApplicationAction>> DiscoverCatalogCoreAsync(BpmnApplicationActionContext context,
        DemoApplicationActionJournal.Attempt attempt, CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _catalogToken);
        var catalog = await LoadCatalogAsync(attempt, linked.Token);
        linked.Token.ThrowIfCancellationRequested();
        if (_catalogDisposed || !UseAsyncCatalog || context.ApplicationActionsRevision != Revision)
        {
            attempt.Record("ProviderSuppressed", "Application policy no longer current; not an engine installation observation.");
            return [];
        }
        attempt.Record("ProviderReturning", "Catalogue policy consumed; descriptor return is not engine installation acknowledgement.");
        return Registration == DemoApplicationRegistration.EmptyActions ? [] : CreateActions(context, catalog);
    }

    private Task<BpmnApplicationActionResult> HandleCatalogAsync(BpmnApplicationActionInvocationEventArgs args, bool typed)
    {
        var attempt = _catalogJournal.Begin(typed ? "TypedHandler" : "LegacyHandler", args.Context, args.ActionId,
            _mountGeneration(), args.CancellationToken, _catalogToken);
        return attempt.TrackConsumer(HandleCatalogCoreAsync(args, typed, attempt));
    }

    private async Task<BpmnApplicationActionResult> HandleCatalogCoreAsync(BpmnApplicationActionInvocationEventArgs args,
        bool typed, DemoApplicationActionJournal.Attempt attempt)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(args.CancellationToken, _catalogToken);
        try
        {
            args.CancellationToken.ThrowIfCancellationRequested();
            linked.Token.ThrowIfCancellationRequested();
            if (!CatalogInvocationCurrent(args)) return CatalogOutcome(attempt, typed, BpmnModelerOperationStatus.Unavailable);
            InvocationCount++;
            var catalog = await LoadCatalogAsync(attempt, linked.Token);
            args.CancellationToken.ThrowIfCancellationRequested();
            linked.Token.ThrowIfCancellationRequested();
            if (!CatalogInvocationCurrent(args) || !catalog.InspectEnabled)
            {
                attempt.Record("InspectorSuppressed", "Captured target or application policy is no longer available.");
                return CatalogOutcome(attempt, typed, BpmnModelerOperationStatus.Unavailable);
            }
            OpenInspector(args, typed, catalog.Description);
            attempt.Record("InspectorPublished", "Captured target and catalogue description published; attempt success is not a later save.");
            return CatalogOutcome(attempt, typed, BpmnModelerOperationStatus.Succeeded);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested || args.CancellationToken.IsCancellationRequested)
        {
            attempt.Record("InspectorSuppressed", "Cancelled before publication; page and request tokens are observed separately.");
            return CatalogOutcome(attempt, typed, BpmnModelerOperationStatus.Cancelled);
        }
        catch (Exception exception) when (typed && exception is HttpRequestException or JsonException)
        {
            return CatalogOutcome(attempt, typed, BpmnModelerOperationStatus.Failed,
                new("DEMO_APPLICATION_CATALOG_FAILED", DiagnosticSeverity.Error, BoundedCatalogError(exception)));
        }
    }

    private static BpmnApplicationActionResult CatalogOutcome(DemoApplicationActionJournal.Attempt attempt,
        bool typed, BpmnModelerOperationStatus status, Diagnostic? diagnostic = null)
    {
        attempt.Record(typed ? "TypedOutcomeReturning" : "LegacyAttemptReturning",
            $"{status}; application handling attempt only, not engine acknowledgement or a configuration save." +
            (typed ? string.Empty : " The legacy callback has no typed result contract."));
        return new(status, diagnostic is null ? [] : [diagnostic]);
    }

    private bool CatalogInvocationCurrent(BpmnApplicationActionInvocationEventArgs args) =>
        !_catalogDisposed && UseAsyncCatalog && InspectAvailable && args.Context.ApplicationActionsRevision == Revision &&
        _modeler()?.CapturePresentation().Stamp == args.Context.Stamp &&
        args.ActionId is "example:inspect" or "example:owner" or "example:scope" or "example:source" or "example:visual";

    private async Task<DemoApplicationCatalog> LoadCatalogAsync(DemoApplicationActionJournal.Attempt attempt, CancellationToken token)
    {
        try
        {
            token.ThrowIfCancellationRequested();
            if (_catalogClient is null) throw new InvalidOperationException("The Demo catalogue HTTP client is not registered.");
            var io = DemoApplicationCatalog.LoadAsync(_catalogClient, token);
            attempt.TrackIo(io);
            CatalogFeedback = "Application catalogue load started. Read observations for actual Task states; a loading message does not prove a pending race.";
            Changed?.Invoke();
            var catalog = await io;
            attempt.Record("IoSucceeded", "Validated catalogue schema 1; browser caching was not disabled.");
            return catalog;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            attempt.Record("IoCancelled", "Application HTTP/deserialization await cancelled; no browser-native abort completion claimed.");
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or InvalidOperationException or OperationCanceledException)
        {
            attempt.Record("IoFailed", BoundedCatalogError(exception));
            throw;
        }
    }

    private static string BoundedCatalogError(Exception exception)
    {
        var message = exception.GetType().Name + ": " + exception.Message;
        return message.Length > 512 ? message[..512] : message;
    }

    public void Dispose()
    {
        if (_catalogDisposed) return;
        _catalogDisposed = true;
        _catalogLifetime.Cancel();
        _catalogLifetime.Dispose();
        Changed = null;
        GC.SuppressFinalize(this);
    }
}
