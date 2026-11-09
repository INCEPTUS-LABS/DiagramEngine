using Inceptus.DiagramEngine.Contracts.Commands;
using Inceptus.DiagramEngine.Contracts.Documents;

namespace Inceptus.DiagramEngine.Contracts.History;

/// <summary>
/// Prepares immutable restoration data for one successfully proposed Command commit.
/// </summary>
public interface ICommandHistoryPolicy
{
    CommandHistoryPreparationResult Prepare(
        ICommand command,
        DocumentSnapshot before,
        DocumentSnapshot committed);
}
