namespace PulseWatch.Application.Scheduling;

public interface ICheckRetentionStore
{
    /// <summary>
    /// Deletes up to <paramref name="batchSize"/> checks that started before <paramref name="cutoffUtc"/>,
    /// oldest first. Never touches incidents or notifications. Returns how many rows were deleted.
    /// </summary>
    Task<int> DeleteChecksBeforeAsync(DateTimeOffset cutoffUtc, int batchSize, CancellationToken cancellationToken);
}
