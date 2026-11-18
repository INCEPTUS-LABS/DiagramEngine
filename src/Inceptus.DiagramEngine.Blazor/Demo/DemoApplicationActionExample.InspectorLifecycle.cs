using System.Security.Cryptography;
using Inceptus.DiagramEngine.Bpmn.Blazor;
using Inceptus.DiagramEngine.Bpmn.Blazor.Components;
using Inceptus.DiagramEngine.Runtime.Documents;
using Microsoft.AspNetCore.Components;

namespace Inceptus.DiagramEngine.Blazor.Demo;

public sealed partial class DemoApplicationActionExample
{
    internal bool InspectorLifecycleBusy { get; private set; }
    internal bool CanUseInspectorLifecycle => VerificationEnabled && DialogOpen && !InspectorLifecycleBusy &&
        _modeler()?.CaptureEditorContext()?.CanReplaceDocument == true;
    internal DemoInspectorLifecycleSnapshot? InspectorLifecycleBefore { get; private set; }
    internal DemoInspectorLifecycleSnapshot? InspectorLifecycleAfter { get; private set; }
    internal BpmnModelerDocumentResult? InspectorReplacementResult { get; private set; }
    internal bool? InspectorSameComponent { get; private set; }
    internal string InspectorLifecycleStatus { get; private set; } = "Idle";
    internal string InspectorLifecycleFeedback { get; private set; } = "No inspector lifecycle operation has run.";

    internal async Task ReplaceFromInspectorAsync()
    {
        if (!CanUseInspectorLifecycle || _modeler() is not { } modeler || modeler.GetDocument().Snapshot is not { } source) return;
        BeginInspectorLifecycle(modeler);
        try
        {
            InspectorReplacementResult = await modeler.SetDocumentAsync(source,
                new BpmnModelerDocumentLoadOptions { VerifySavedRouting = true });
            InspectorLifecycleAfter = CaptureInspectorLifecycle(_modeler());
            InspectorSameComponent = ReferenceEquals(modeler, _modeler());
            InspectorLifecycleStatus = InspectorReplacementResult.Status.ToString();
            InspectorLifecycleFeedback = "Submitted the accepted source through strict public replacement. The inspector retains its original captured target.";
        }
        finally { InspectorLifecycleBusy = false; }
    }

    internal async Task UnmountFromInspectorAsync(EventCallback unmountRequested)
    {
        if (!CanUseInspectorLifecycle || !unmountRequested.HasDelegate || _modeler() is not { } modeler) return;
        BeginInspectorLifecycle(modeler);
        try
        {
            await unmountRequested.InvokeAsync();
            InspectorLifecycleAfter = CaptureInspectorLifecycle(_modeler());
            InspectorSameComponent = ReferenceEquals(modeler, _modeler());
            InspectorLifecycleStatus = "UnmountRequested";
            InspectorLifecycleFeedback = "Requested normal parent unmount while keeping this inspector open. Asynchronous managed cleanup completion is not observed by this control.";
        }
        finally { InspectorLifecycleBusy = false; }
    }

    private void BeginInspectorLifecycle(InceptusBpmnModeler modeler)
    {
        InspectorLifecycleBusy = true;
        InspectorLifecycleBefore = CaptureInspectorLifecycle(modeler);
        InspectorLifecycleAfter = null;
        InspectorReplacementResult = null;
        InspectorSameComponent = null;
    }

    private static DemoInspectorLifecycleSnapshot CaptureInspectorLifecycle(InceptusBpmnModeler? modeler)
    {
        var source = modeler?.GetDocument().Snapshot;
        return new(modeler?.CaptureDecorationAttachment(), source?.DocumentId.Value, source?.Revision.Value,
            source is null ? null : Convert.ToHexString(SHA256.HashData(NativeDocumentSerializer.Export(source).AsSpan())));
    }
}

internal sealed record DemoInspectorLifecycleSnapshot(BpmnDecorationAttachment? Attachment, string? DocumentId,
    ulong? Revision, string? NativeSha256)
{
    public override string ToString() => Attachment is null ? "No current attachment." :
        $"Instance {Attachment.InstanceId}; attachment {Attachment.AttachmentVersion}; document {DocumentId}; revision {Revision}; native SHA-256 {NativeSha256}.";
}
