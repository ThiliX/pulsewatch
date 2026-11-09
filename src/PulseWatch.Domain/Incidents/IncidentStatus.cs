namespace PulseWatch.Domain.Incidents;

/// <summary>Stored as smallint; do not renumber. The partial unique index relies on Open = 0.</summary>
public enum IncidentStatus : short
{
    Open = 0,
    Resolved = 1,
}
