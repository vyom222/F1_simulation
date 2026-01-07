namespace F1_simulation.Core.Strategy_solver;

using F1_simulation.Core.Tyres;

// Must be immutable so required the creation of TyreType
// Choice of record struct important
public readonly record struct RaceState(
    TyreType Tyre,
    int TyreAge,
    int LapsRemaining,
    TyreUsage Usage
);

public enum Action
{
    StayOut,
    PitSoft,
    PitMedium,
    PitHard
};

    [Flags]
    public enum TyreUsage
    {
        None = 0,
        Soft = 1 << 0,
        Medium = 1 << 1,
        Hard = 1 << 2
    };


