namespace Inceptus.DiagramEngine.Contracts.EndpointReconnection;

/// <summary>Notation-owned eligibility for candidate highlighting before an endpoint is committed.</summary>
public interface IConnectorEndpointReconnectionCandidateEligibility
{
    bool IsEligible(ConnectorEndpointReconnectionRequest request);
}
