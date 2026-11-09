namespace Inceptus.DiagramEngine.Contracts.Commands;

public interface IDocumentChangedSubscriber
{
    ValueTask OnDocumentChangedAsync(DocumentChangedEvent change);
}
