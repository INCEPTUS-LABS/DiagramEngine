using System.Collections.Immutable;
using Inceptus.DiagramEngine.Contracts.Commands;
using Inceptus.DiagramEngine.Contracts.Diagnostics;

namespace Inceptus.DiagramEngine.Runtime.Commands;

internal sealed class UpdateDocumentPublicationCommandEnvelopeValidator :
    ICommandEnvelopeValidator
{
    public ImmutableArray<Diagnostic> Validate(ICommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return command is UpdateDocumentPublicationCommand
            ? []
            :
            [
                new Diagnostic(
                    CommandExecutionDiagnosticCodes.InvalidCommandStructure,
                    DiagnosticSeverity.Error,
                    $"Command type '{command.TypeId}' does not use its registered immutable request shape.",
                    command.TypeId.Value),
            ];
    }
}
