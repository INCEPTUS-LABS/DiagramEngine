using System.Collections.Immutable;
using Inceptus.DiagramEngine.Contracts.Diagnostics;

namespace Inceptus.DiagramEngine.Bpmn.Blazor;

/// <summary>A bounded immutable outcome for one application handling attempt.</summary>
public sealed class BpmnApplicationActionResult
{
    public BpmnApplicationActionResult(BpmnModelerOperationStatus status,
        ImmutableArray<Diagnostic> diagnostics = default)
    {
        if (status is not (BpmnModelerOperationStatus.Succeeded or BpmnModelerOperationStatus.Unavailable or
            BpmnModelerOperationStatus.Cancelled or BpmnModelerOperationStatus.Failed))
            throw new ArgumentOutOfRangeException(nameof(status), status, "The application action status is unsupported.");
        diagnostics = diagnostics.IsDefault ? [] : diagnostics;
        if (diagnostics.Length > 16)
            throw new ArgumentException("An application result can contain at most sixteen diagnostics.", nameof(diagnostics));
        foreach (var diagnostic in diagnostics)
        {
            if (diagnostic is null || diagnostic.Code.Length > 128 || diagnostic.Code.Any(char.IsControl) ||
                diagnostic.Message.Length > 512 || diagnostic.SourceIdentity?.Length > 256 ||
                diagnostic.Context.Count > 8 || diagnostic.Context.Any(static pair => pair.Key.Length > 64 || pair.Value.Length > 512))
                throw new ArgumentException("An application result diagnostic exceeds the supported bounds.", nameof(diagnostics));
            if (status is BpmnModelerOperationStatus.Succeeded or BpmnModelerOperationStatus.Cancelled &&
                diagnostic.Severity == DiagnosticSeverity.Error)
                throw new ArgumentException("Successful or cancelled application attempts cannot report error diagnostics.", nameof(diagnostics));
        }
        Status = status;
        Diagnostics = diagnostics;
    }

    public BpmnModelerOperationStatus Status { get; }
    public bool Succeeded => Status == BpmnModelerOperationStatus.Succeeded;
    public ImmutableArray<Diagnostic> Diagnostics { get; }
}
