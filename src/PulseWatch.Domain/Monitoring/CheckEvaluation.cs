using PulseWatch.Domain.Incidents;

namespace PulseWatch.Domain.Monitoring;

public enum IncidentTransition
{
    None = 0,
    Opened = 1,
    Resolved = 2,
}

/// <summary>What applying one check result changed.</summary>
/// <param name="Transition">Whether an incident was opened or resolved by this check.</param>
/// <param name="Incident">The incident that is (or was just) affected; null when there is none.</param>
public sealed record CheckEvaluation(IncidentTransition Transition, Incident? Incident)
{
    internal static CheckEvaluation NoChange(Incident? openIncident) => new(IncidentTransition.None, openIncident);

    internal static CheckEvaluation Opened(Incident incident) => new(IncidentTransition.Opened, incident);

    internal static CheckEvaluation Resolved(Incident incident) => new(IncidentTransition.Resolved, incident);
}
