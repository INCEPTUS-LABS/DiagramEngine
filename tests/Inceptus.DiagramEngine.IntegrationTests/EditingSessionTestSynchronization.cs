using Inceptus.DiagramEngine.Canvas2D.EditingSession;
using Inceptus.DiagramEngine.Runtime.Commands;
using Inceptus.DiagramEngine.Runtime.Documents;

namespace Inceptus.DiagramEngine.IntegrationTests;

internal static class EditingSessionTestSynchronization
{
    internal static async Task WaitForCommittedEventAndSessionIdleAsync(
        Document document,
        EditingSession session)
    {
        // Commands enqueue events without awaiting subscribers. Drain delivery before
        // settling all session pipeline work triggered by those events.
        await CommandProcessor.WaitForEventDispatchIdleAsync(document).WaitAsync(TimeSpan.FromSeconds(5));
        await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    }
}
