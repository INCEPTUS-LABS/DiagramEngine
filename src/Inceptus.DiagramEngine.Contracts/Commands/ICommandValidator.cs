using System.Collections.Immutable;
using Inceptus.DiagramEngine.Contracts.Diagnostics;
using Inceptus.DiagramEngine.Contracts.Documents;

namespace Inceptus.DiagramEngine.Contracts.Commands;

public interface ICommandValidator
{
    ImmutableArray<Diagnostic> Validate(ICommand command, DocumentSnapshot document);
}
