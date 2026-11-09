using Inceptus.DiagramEngine.Contracts.Documents;

namespace Inceptus.DiagramEngine.Contracts.Commands;

public interface ICommandHandler
{
    ValueTask<CommandHandlerResult> HandleAsync(
        ICommand command,
        DocumentSnapshot document,
        CancellationToken cancellationToken);
}
