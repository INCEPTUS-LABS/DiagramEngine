using System.Security.Cryptography;
using Inceptus.DiagramEngine.Bpmn.Blazor;
using Inceptus.DiagramEngine.Bpmn.Blazor.Components;
using Inceptus.DiagramEngine.Bpmn.Blazor.Presentation;
using Inceptus.DiagramEngine.Canvas2D.EditingSession;
using Inceptus.DiagramEngine.Contracts.Documents;
using Inceptus.DiagramEngine.Contracts.Routing;
using Inceptus.DiagramEngine.Contracts.Visuals;
using Inceptus.DiagramEngine.Runtime.Documents;
using Microsoft.AspNetCore.Components;

namespace Inceptus.DiagramEngine.Blazor.Demo;

/// <summary>Opt-in source Demo actions and copied internal lifecycle observations.</summary>
public partial class DemoVerificationControls
{
    [Parameter] public InceptusBpmnModeler? Modeler { get; set; }
    [Parameter] public bool Mounted { get; set; }
    [Parameter] public long MountGeneration { get; set; }
    [Parameter] public string MountId { get; set; } = string.Empty;
    [Parameter] public int RetiredCallbackCount { get; set; }
    [Parameter] public bool AllowDecorations { get; set; } = true;
    [Parameter] public bool AllowReplacement { get; set; }
    [Parameter] public EventCallback UnmountRequested { get; set; }
    [Parameter] public EventCallback MountRequested { get; set; }
    [Parameter] public EventCallback Changed { get; set; }

    private bool _enabled;
    private bool _busy;
    private bool _disposed;
    private BpmnDecorationAttachment? _retiredAttachment;
    private long _retiredRevision;
    private BpmnLifecycleObservation? _lifecycleObservation;
    internal BpmnLifecycleSnapshot? LifecycleSnapshot { get; private set; }

    private void ReadLifecycleObservations() => LifecycleSnapshot = _lifecycleObservation?.Read();

    private void ObserveLifecycle(InceptusBpmnModeler modeler)
    {
        _lifecycleObservation = modeler.BeginLifecycleObservation();
        LifecycleSnapshot = null;
    }
    internal DemoVerificationSnapshot? SnapshotBefore { get; private set; }
    internal DemoVerificationSnapshot? SnapshotAfter { get; private set; }
    internal BpmnModelerDocumentResult? DocumentResult { get; private set; }
    internal Canvas2DDecorationUpdateResult? DecorationResult { get; private set; }
    internal bool? SameComponent { get; private set; }
    internal bool? PendingAtUnmountRequest { get; private set; }
    internal bool? AcceptedAtUnmountRequest { get; private set; }
    internal Task<Canvas2DDecorationUpdateResult>? PendingDecorationOperation { get; private set; }
    internal string Status { get; private set; } = "Idle";
    internal string Feedback { get; private set; } = "No verification operation has run.";
    private bool CanOperate => _enabled && !_busy && !_disposed && Mounted &&
        Modeler?.CaptureEditorContext()?.CanReplaceDocument == true && Modeler.CaptureDecorationAttachment() is not null;
    private bool CanCheckRetiredAttachment => CanOperate && _retiredAttachment is not null &&
        Modeler!.CaptureDecorationAttachment() != _retiredAttachment;

    private void CaptureCurrent()
    {
        if (!CanOperate) return;
        SnapshotAfter = Capture(Modeler!);
        Feedback = "Captured public state at this UI action; this is not a live frame feed.";
        Status = "Captured";
    }

    private Task ReplaceIdenticalAsync() => ReplaceAsync(cancelled: false, incompatible: false);
    private Task ReplaceCancelledAsync() => ReplaceAsync(cancelled: true, incompatible: false);
    private Task ReplaceIncompatibleAsync() => ReplaceAsync(cancelled: false, incompatible: true);

    private async Task ReplaceAsync(bool cancelled, bool incompatible)
    {
        if (!AllowReplacement || !CanOperate || Modeler is not { } modeler || modeler.GetDocument().Snapshot is not { } source) return;
        Begin(modeler);
        _busy = true;
        try
        {
            var candidate = incompatible ? CreateIncompatibleCandidate(source) : source;
            if (candidate is null)
            {
                Status = "Unavailable";
                Feedback = "This source has no saved routing scopes to test. Choose the prepared Order Process sample before enabling verification.";
                return;
            }
            using var cancellation = new CancellationTokenSource();
            if (cancelled) cancellation.Cancel();
            DocumentResult = await modeler.SetDocumentAsync(candidate,
                new BpmnModelerDocumentLoadOptions { VerifySavedRouting = true }, cancellation.Token);
            if (_disposed) return;
            SnapshotAfter = Capture(modeler);
            SameComponent = ReferenceEquals(modeler, Modeler);
            Status = DocumentResult.Status.ToString();
            Feedback = incompatible
                ? "Strict saved-routing candidate result recorded. This tests compatibility rejection, not renderer fault injection."
                : cancelled ? "An already-cancelled token was supplied before replacement started."
                : "The current prepared source was submitted with identical native IDs and revision.";
            await Changed.InvokeAsync();
        }
        finally { _busy = false; }
    }

    internal static DocumentSnapshot? CreateIncompatibleCandidate(DocumentSnapshot source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.VisualModel.RoutingScopes is not { Length: > 0 } scopes) return null;
        var candidates = scopes.Select(scope =>
        {
            var geometry = scope.Geometry;
            return new ScopeRoutingSnapshot(scope.ScopeId,
                new ScopeGeometrySnapshot(geometry.PolicyId, "demo-verification-incompatible", geometry.LayoutAlgorithmId,
                    geometry.Configuration, geometry.TextConfiguration, geometry.Contributors, geometry.Nodes,
                    geometry.Regions, geometry.Captions, geometry.TextMeasurements, geometry.SpatialWidths), scope.Connectors);
        });
        var visual = source.VisualModel;
        return new(source.SemanticModel, new VisualModelSnapshot(source.DocumentId, source.Revision, visual.VisualStates,
            visual.ProfileElementPresentations, candidates, visual.DocumentRoutingStates), source.Metadata, source.Publication);
    }

    private async Task CheckRetiredAttachmentAsync()
    {
        if (!CanCheckRetiredAttachment || Modeler is not { } modeler || _retiredAttachment is not { } retired) return;
        _busy = true;
        try
        {
            DecorationResult = await modeler.ClearDecorationsAsync(retired, checked(_retiredRevision + 1));
            if (_disposed) return;
            SnapshotAfter = Capture(modeler);
            Status = DecorationResult.Status.ToString();
            Feedback = "Submitted the retained old attachment to the current modeler; inspect acceptance and result.";
        }
        finally { _busy = false; }
    }

    private async Task UnmountAsync()
    {
        if (!CanOperate || Modeler is not { } modeler) return;
        Begin(modeler);
        ObserveLifecycle(modeler);
        await UnmountRequested.InvokeAsync();
        if (_disposed) return;
        Status = "UnmountRequested";
        Feedback = "Requested normal Blazor unmount. Read lifecycle observations to inspect actual managed cleanup.";
    }

    private async Task UpdateAndUnmountAsync()
    {
        if (!CanOperate || Modeler is not { } modeler || modeler.CaptureDecorationAttachment() is not { } attachment) return;
        Begin(modeler);
        _busy = true;
        try
        {
            var revision = checked((SnapshotBefore?.InputRevision ?? 0) + 1);
            ObserveLifecycle(modeler);
            _lifecycleObservation?.ExpectOperation(revision);
            var operation = modeler.ReplaceDecorationsAsync(attachment, revision,
                DemoAnnotationProvider.CreateInput("Unmount request")).AsTask();
            PendingDecorationOperation = operation;
            _lifecycleObservation?.TrackOperation(attachment, revision, operation);
            PendingAtUnmountRequest = !operation.IsCompleted;
            AcceptedAtUnmountRequest = modeler.CaptureEditorContext()?.Presentation.DecorationInputRevision == revision;
            await UnmountRequested.InvokeAsync();
            var result = await operation;
            if (_disposed) return;
            DecorationResult = result;
            Status = result.Status.ToString();
            Feedback = "Recorded the update outcome after requesting unmount. Pending-at-request and disposal completion are separate observations.";
        }
        finally { _busy = false; }
    }

    private async Task MountAsync()
    {
        if (!_enabled || _busy || Mounted || _disposed) return;
        await MountRequested.InvokeAsync();
        if (_disposed) return;
        Status = "MountRequested";
        Feedback = "Requested a fresh modeler in the same page. Capture its public identity after Ready.";
    }

    private void Begin(InceptusBpmnModeler modeler)
    {
        SnapshotBefore = Capture(modeler);
        SnapshotAfter = null;
        DocumentResult = null;
        DecorationResult = null;
        SameComponent = null;
        PendingAtUnmountRequest = null;
        AcceptedAtUnmountRequest = null;
        PendingDecorationOperation = null;
        _retiredAttachment = SnapshotBefore.Attachment;
        _retiredRevision = SnapshotBefore.InputRevision;
    }

    private DemoVerificationSnapshot Capture(InceptusBpmnModeler modeler)
    {
        var context = modeler.CaptureEditorContext();
        var source = modeler.GetDocument().Snapshot;
        return new(modeler.CaptureDecorationAttachment(), source?.DocumentId.Value, source?.Revision.Value,
            context?.Presentation.DecorationInputRevision ?? 0,
            context?.Presentation.DecorationPresentation?.FramePresented, MountGeneration, MountId,
            source is null ? null : Convert.ToHexString(SHA256.HashData(NativeDocumentSerializer.Export(source).AsSpan())));
    }

    private static string Describe(DemoVerificationSnapshot snapshot) =>
        $"mount {snapshot.MountGeneration} ({snapshot.MountId}); instance {snapshot.Attachment?.InstanceId}; " +
        $"attachment {snapshot.Attachment?.AttachmentVersion}; document {snapshot.DocumentId}; revision {snapshot.DocumentRevision}; " +
        $"input {snapshot.InputRevision}; last captured frame presented {snapshot.FramePresented}; native SHA-256 {snapshot.NativeSha256}";

    public void Dispose()
    {
        _disposed = true;
        _retiredAttachment = null;
        PendingDecorationOperation = null;
        GC.SuppressFinalize(this);
    }
}

internal sealed record DemoVerificationSnapshot(BpmnDecorationAttachment? Attachment, string? DocumentId,
    ulong? DocumentRevision, long InputRevision, bool? FramePresented, long MountGeneration, string MountId, string? NativeSha256);
