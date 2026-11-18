using Inceptus.DiagramEngine.Bpmn.Blazor;

namespace Inceptus.DiagramEngine.Blazor.Demo;

/// <summary>Opt-in application inputs using the public modeler; no engine fault injection or timing controls.</summary>
public sealed partial class DemoApplicationActionExample
{
    private long _revisionHighWater;
    public bool VerificationEnabled { get; private set; }
    public DemoApplicationRegistration Registration { get; private set; }
    public DemoApplicationOutcome Outcome { get; private set; }
    public BpmnPresentationContextStamp? CapturedRequestStamp { get; private set; }
    public long? CapturedRequestRevision { get; private set; }
    public BpmnPresentationOperationResult? ReplayResult { get; private set; }
    public string RequestFeedback { get; private set; } = "No request captured.";
    public string ConsumerOutcome { get; private set; } = "No application handling attempt recorded.";
    public string OperationFeedback { get; private set; } = "No modeler application-action failure notification recorded.";
    public int OperationFailureCount { get; private set; }
    public BpmnApplicationActionProvider? RegisteredProvider =>
        VerificationEnabled && Registration == DemoApplicationRegistration.NoExtension ? null : Provider;
    public BpmnApplicationActionHandler? RegisteredTypedHandler =>
        RegistrationHasAuthority && (UseTypedHandler || VerificationEnabled && Registration == DemoApplicationRegistration.ConflictingHandlers)
            ? TypedHandler : null;
    public bool RegisterLegacyHandler => RegistrationHasAuthority &&
        (!UseTypedHandler || VerificationEnabled && Registration == DemoApplicationRegistration.ConflictingHandlers);
    private bool RegistrationHasAuthority => !VerificationEnabled ||
        Registration is not (DemoApplicationRegistration.NoExtension or DemoApplicationRegistration.ProviderOnly);

    public void SetVerificationEnabled(bool enabled)
    {
        if (VerificationEnabled == enabled) return;
        VerificationEnabled = enabled;
        AsyncCatalogEnabled = false;
        Labels = DemoApplicationLabels.Default;
        Registration = DemoApplicationRegistration.Normal;
        Outcome = DemoApplicationOutcome.OpenInspector;
        CapturedRequestStamp = null;
        CapturedRequestRevision = null;
        ReplayResult = null;
        RequestFeedback = "No request captured.";
        AdvanceRevision();
    }

    public void SetRegistration(DemoApplicationRegistration registration)
    {
        if (!VerificationEnabled || !Enum.IsDefined(registration) || Registration == registration) return;
        Registration = registration;
        AdvanceRevision();
    }

    public void SetOutcome(DemoApplicationOutcome outcome)
    {
        if (!VerificationEnabled || !Enum.IsDefined(outcome) || Outcome == outcome) return;
        Outcome = outcome;
        AdvanceRevision();
    }

    public void SubmitNegativeRevision()
    {
        if (VerificationEnabled) Revision = -1;
    }

    public void SubmitDecreasingRevision()
    {
        if (VerificationEnabled) Revision = _revisionHighWater - 1;
    }

    public void RestoreValidRevision()
    {
        if (VerificationEnabled) AdvanceRevision();
    }

    private void AdvanceRevision()
    {
        Revision = checked(Math.Max(_revisionHighWater, Revision) + 1);
        _revisionHighWater = Revision;
    }

    public void CaptureRequest()
    {
        if (!VerificationEnabled) return;
        ReplayResult = null;
        if (Revision < 0 || _modeler()?.CapturePresentation().Stamp is not { } stamp)
        {
            RequestFeedback = "Capture requires a coherent presentation and nonnegative revision; the previous capture was retained.";
            return;
        }
        CapturedRequestStamp = stamp;
        CapturedRequestRevision = Revision;
        RequestFeedback = $"Captured instance {stamp.InstanceId}; attachment {stamp.AttachmentVersion}; " +
            $"document {stamp.DocumentId.Value}/{stamp.Revision.Value}; view {stamp.PresentationContext}; " +
            $"context {stamp.ContextVersion}; selection {stamp.SelectionVersion}; application revision {Revision}.";
    }

    public async Task ReplayRequestAsync(bool cancelled)
    {
        if (!VerificationEnabled || CapturedRequestStamp is not { } stamp || CapturedRequestRevision is not { } revision) return;
        ReplayResult = null;
        if (_modeler() is not { } modeler)
        {
            RequestFeedback = "No mounted modeler. The immutable request is retained for replay after remount.";
            return;
        }
        using var cancellation = new CancellationTokenSource();
        if (cancelled) cancellation.Cancel();
        // Deliberately replay the captured values, never recapture selection/policy or retain a live modeler.
        ReplayResult = await modeler.RequestApplicationContextualActionsAsync(stamp, revision, cancellation.Token);
        RequestFeedback = $"Captured request: {ReplayResult.Status}. " +
            string.Join("; ", ReplayResult.Diagnostics.Select(item => item.Code)) +
            (cancelled ? " Already-cancelled token supplied before entry." : string.Empty);
    }

    public void RecordOperationFailure(BpmnModelerOperationFailedEventArgs args)
    {
        if (args.Operation != BpmnModelerOperation.ApplicationAction) return;
        OperationFailureCount++;
        OperationFeedback = $"Modeler OperationFailed: {args.Status}. " +
            string.Join("; ", args.Diagnostics.Select(item => item.Code + ": " + item.Message));
        Changed?.Invoke();
    }

    private ValueTask<BpmnApplicationActionResult> CompleteVerificationAttempt()
    {
        ConsumerOutcome = Outcome switch
        {
            DemoApplicationOutcome.NullResult => "Application returned null; inspect the modeler's failure notification.",
            DemoApplicationOutcome.InvalidStatus => "Application attempted an unsupported result status; its constructor throws.",
            DemoApplicationOutcome.Throw => "Application throws InvalidOperationException.",
            DemoApplicationOutcome.UnrelatedCancellation => "Application throws with a separate cancelled token, not the invocation token.",
            _ => $"Application returned {Outcome}; this records the handling attempt, not a saved configuration.",
        };
        Changed?.Invoke();
        return Outcome switch
        {
            DemoApplicationOutcome.Unavailable => ValueTask.FromResult(new BpmnApplicationActionResult(BpmnModelerOperationStatus.Unavailable)),
            DemoApplicationOutcome.Cancelled => ValueTask.FromResult(new BpmnApplicationActionResult(BpmnModelerOperationStatus.Cancelled)),
            DemoApplicationOutcome.Failed => ValueTask.FromResult(new BpmnApplicationActionResult(BpmnModelerOperationStatus.Failed)),
            DemoApplicationOutcome.NullResult => ValueTask.FromResult<BpmnApplicationActionResult>(null!),
            DemoApplicationOutcome.InvalidStatus => ValueTask.FromResult(new BpmnApplicationActionResult(BpmnModelerOperationStatus.Rejected)),
            DemoApplicationOutcome.UnrelatedCancellation => throw new OperationCanceledException(new CancellationToken(canceled: true)),
            _ => throw new InvalidOperationException("The Demo application deliberately failed this handling attempt."),
        };
    }
}

public enum DemoApplicationRegistration { Normal, EmptyActions, NoExtension, ProviderOnly, ConflictingHandlers }
public enum DemoApplicationOutcome { OpenInspector, Unavailable, Cancelled, Failed, NullResult, InvalidStatus, Throw, UnrelatedCancellation }
