using System.Numerics;
using F1_simulation.Core.Tyres;

namespace F1_simulation.Core.Strategy_solver
{
    public class OptimalStrategy
    {
        // Memo table: state -> best result from this state
        private readonly Dictionary<RaceState, StrategyResult> _memo = new();

        private readonly Dictionary<TyreType, Tyre> _tyres;
        private readonly double _pitLoss;
        private readonly int _raceLength;

        public OptimalStrategy(
            IEnumerable<Tyre> tyres,
            int raceLength,
            double pitLossSeconds = 20
        )
        {
            _tyres = tyres.ToDictionary(t => t.Name switch
            {
                "Soft" => TyreType.Soft,
                "Medium" => TyreType.Medium,
                "Hard" => TyreType.Hard,
                _ => throw new ArgumentException($"Unknown tyre name {t.Name}")
            });

            _raceLength = raceLength;
            _pitLoss = pitLossSeconds;
        }

        // Dynamic Programming Solver
        public StrategyResult Solve(RaceState state)
        {
            // ----- Base case -----
            if (state.LapsRemaining == 0)
            {
                // Must use at least 2 different compounds
                if (CountBits(state.Usage) < 2)
                {
                    return new StrategyResult(
                        double.PositiveInfinity,
                        StrategyAction.StayOut,
                        null
                    );
                }

                return new StrategyResult(0.0, StrategyAction.StayOut, null);
            }

            // ----- Memo lookup -----
            if (_memo.TryGetValue(state, out var cached))
                return cached;

            StrategyResult best = new(
                double.PositiveInfinity,
                StrategyAction.StayOut,
                null
            );

            // Stay out
            {
                var tyre = _tyres[state.Tyre];

                if (state.TyreAge < tyre.LapTimes.Length)
                {
                    double lapTime = tyre.LapTimes[state.TyreAge];

                    var nextState = state with
                    {
                        TyreAge = state.TyreAge + 1,
                        LapsRemaining = state.LapsRemaining - 1
                    };

                    var next = Solve(nextState);
                    double cost = lapTime + next.TotalTime;

                    if (cost < best.TotalTime)
                    {
                        best = new StrategyResult(
                            cost,
                            StrategyAction.StayOut,
                            null
                        );
                    }
                }
            }


            // Pit

            foreach (var (tyreType, tyre) in _tyres)
            {
                double lapTime = tyre.LapTimes[0];
                var flag = ToUsageFlag(tyreType);

                var nextState = state with
                {
                    Tyre = tyreType,
                    TyreAge = 1,
                    LapsRemaining = state.LapsRemaining - 1,
                    Usage = state.Usage | flag
                };

                var next = Solve(nextState);
                double cost = _pitLoss + lapTime + next.TotalTime;

                if (cost < best.TotalTime)
                {
                    best = new StrategyResult(
                        cost,
                        StrategyAction.Pit,
                        tyreType
                    );
                }
            }

            _memo[state] = best;
            return best;
        }


        // Strategy reconstruction 
        public List<StrategyResult> GetFullStrategy(RaceState start)
        {
            var strategy = new List<StrategyResult>();
            var state = start;

            while (state.LapsRemaining > 0)
            {
                var result = Solve(state);
                strategy.Add(result);

                if (result.Action == StrategyAction.StayOut)
                {
                    state = state with
                    {
                        TyreAge = state.TyreAge + 1,
                        LapsRemaining = state.LapsRemaining - 1
                    };
                }
                else
                {
                    state = state with
                    {
                        Tyre = result.PitTo!.Value,
                        TyreAge = 1,
                        LapsRemaining = state.LapsRemaining - 1,
                        Usage = state.Usage | ToUsageFlag(result.PitTo.Value)
                    };
                }
            }

            return strategy;
        }

        // Helper types
        public enum StrategyAction
        {
            StayOut,
            Pit
        }

        public readonly record struct StrategyResult(
            double TotalTime,
            StrategyAction Action,
            TyreType? PitTo
        );

        private static TyreUsage ToUsageFlag(TyreType tyre) => tyre switch
        {
            TyreType.Soft => TyreUsage.Soft,
            TyreType.Medium => TyreUsage.Medium,
            TyreType.Hard => TyreUsage.Hard,
            _ => throw new ArgumentOutOfRangeException()
        };

        private static int CountBits(TyreUsage usage) =>
            BitOperations.PopCount((uint)usage);
    }
}
