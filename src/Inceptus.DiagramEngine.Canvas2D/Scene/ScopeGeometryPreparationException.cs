using System.Collections.Immutable;
using Inceptus.DiagramEngine.Contracts.Diagnostics;

namespace Inceptus.DiagramEngine.Canvas2D.Scene;

/// <summary>A preparation service failed; this does not classify saved content as incompatible.</summary>
internal sealed class ScopeGeometryPreparationException : Exception
{
    internal ScopeGeometryPreparationException(IEnumerable<Diagnostic> diagnostics)
        : base("The current text measurement service could not prepare scope geometry.")
    {
        Diagnostics = [new Diagnostic("INCEPTUS.ROUTING.PREPARATION.UNAVAILABLE", DiagnosticSeverity.Error,
            Message), .. diagnostics];
    }

    internal ImmutableArray<Diagnostic> Diagnostics { get; }
}
