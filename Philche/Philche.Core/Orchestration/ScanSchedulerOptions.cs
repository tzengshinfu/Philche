namespace Philche.Core.Orchestration;

public sealed record ScanSchedulerOptions
{
    public required TimeSpan PeriodicInterval { get; init; }
    public TimeSpan DebounceWindow { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan MinimumAcceleratedInterval { get; init; } = TimeSpan.FromMinutes(10);

    public void Validate()
    {
        if (PeriodicInterval < TimeSpan.FromHours(1))
        {
            throw new InvalidOperationException("Periodic interval must be at least 1 hour.");
        }

        if (DebounceWindow < TimeSpan.Zero)
        {
            throw new InvalidOperationException("Debounce window cannot be negative.");
        }

        if (MinimumAcceleratedInterval < TimeSpan.Zero)
        {
            throw new InvalidOperationException("Minimum accelerated interval cannot be negative.");
        }
    }
}
