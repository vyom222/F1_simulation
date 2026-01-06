namespace F1_simulation.Core.Strategy_solver;

public class Optimal_strategy
{
    [Flags]
    public enum TyreUsage
    {
        None = 0,
        Soft = 1 << 0,
        Medium = 1 << 1,
        Hard = 1 << 2
    }
}
