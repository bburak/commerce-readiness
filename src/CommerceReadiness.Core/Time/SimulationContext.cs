namespace CommerceReadiness.Core.Time;

/// <summary>
/// The seed and fixed clock of a generation run. Domain calculations take <see cref="Now"/> from here and
/// never read the system clock, so every time-sensitive result is reproducible.
/// </summary>
public sealed record SimulationContext(int Seed, DateTimeOffset Now)
{
    public const int DefaultSeed = 42;

    public static readonly TimeSpan TurkeyOffset = TimeSpan.FromHours(3);

    public static readonly DateTimeOffset DefaultClock = new(2026, 11, 17, 10, 0, 0, TimeSpan.FromHours(3));

    public static SimulationContext Create(int seed = DefaultSeed) => new(seed, DefaultClock);

    public DateOnly Today => TodayOf(Now);

    public static DateOnly TodayOf(DateTimeOffset now) => DateOnly.FromDateTime(now.ToOffset(TurkeyOffset).DateTime);
}
