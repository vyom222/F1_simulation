using System.Numerics;
using F1_simulation.Core.Tyres;
using F1_simulation.Core.Strategy_solver;

namespace F1_simulation.Core.Race_simulator
{
    public enum StrategyAction { StayOut, Pit }

    public readonly record struct StrategyResult(
        double TotalTime,
        StrategyAction Action,
        TyreType? PitTo
    );

    public class RaceSolver
    {
        private readonly Dictionary<TyreType, Tyre> _tyres;
        private readonly double _pitLoss;
        private readonly double _trafficPenalty;
        private readonly double _pitProbability; // Probability that other drivers pit and affect positions

        // Memo table: state -> best result
        private readonly Dictionary<RaceSolverState, StrategyResult> _memo = new();

        public RaceSolver(
            IEnumerable<Tyre> tyres,
            double pitLossSeconds = 25.0,
            double trafficPenalty = 0.5,
            double pitProbability = 0.3)
        {
            _tyres = tyres.ToDictionary(t => t.Name switch
            {
                "Soft" => TyreType.Soft,
                "Medium" => TyreType.Medium,
                "Hard" => TyreType.Hard,
                _ => throw new ArgumentException($"Unknown tyre name {t.Name}")
            });

            _pitLoss = pitLossSeconds;
            _trafficPenalty = trafficPenalty;
            _pitProbability = pitProbability;
        }

        // Race-aware state that includes position and traffic context
        public readonly record struct RaceSolverState(
            TyreType CurrentTyre,
            int TyreAge,
            int LapsRemaining,
            int CurrentPosition,        // 1-based position
            double GapToCarAhead,       // seconds to car ahead
            double GapToCarBehind,      // seconds to car behind
            TyreUsage UsedTyres
        );

        // Solve for optimal action given current race state
        public StrategyResult Solve(RaceSolverState state)
        {
            // Base case
            if (state.LapsRemaining == 0)
            {
                // F1 Regulation: Must use at least 2 different compounds
                if (CountBits(state.UsedTyres) < 2)
                {
                    return new StrategyResult(double.PositiveInfinity, StrategyAction.StayOut, null);
                }
                return new StrategyResult(0.0, StrategyAction.StayOut, null);
            }

            // Memo lookup
            if (_memo.TryGetValue(state, out var cached))
                return cached;

            StrategyResult best = new(double.PositiveInfinity, StrategyAction.StayOut, null);

            // Stay out option
            if (state.TyreAge < _tyres[state.CurrentTyre].LapTimes.Length)
            {
                double baseLapTime = _tyres[state.CurrentTyre].LapTimes[state.TyreAge];

                // Add expected traffic effect based on position and gaps
                double expectedTrafficEffect = CalculateExpectedTrafficEffect(state);

                // Add fuel penalty
                double fuelPenalty = state.LapsRemaining * 0.05;

                // Position change effects are now baked into traffic expectations
                double totalLapTime = baseLapTime + expectedTrafficEffect + fuelPenalty;

                // Create next state
                var nextState = state with
                {
                    TyreAge = state.TyreAge + 1,
                    LapsRemaining = state.LapsRemaining - 1,
                    // Position might change based on performance and other drivers' actions
                    CurrentPosition = CalculateNextPosition(state, totalLapTime),
                    GapToCarAhead = Math.Max(0, state.GapToCarAhead - (totalLapTime * 0.1)), // Gap might close
                    GapToCarBehind = state.GapToCarBehind + (totalLapTime * 0.1) // Gap might open
                };

                var next = Solve(nextState);
                double cost = totalLapTime + next.TotalTime;

                if (cost < best.TotalTime && !double.IsInfinity(cost))
                {
                    best = new StrategyResult(cost, StrategyAction.StayOut, null);
                }
            }

            // Pit option - try each available tyre
            foreach (var (tyreType, tyre) in _tyres)
            {
                double baseLapTime = tyre.LapTimes[0]; // New tyre

                // After pitting: expected traffic is lower due to fresh tires and potential clean air
                var pitState = state with { CurrentTyre = tyreType, TyreAge = 0 };
                double expectedTrafficEffect = CalculateExpectedTrafficEffect(pitState) * (1.0 - _pitProbability); // Reduced traffic after pit

                double fuelPenalty = state.LapsRemaining * 0.05;

                // Pitting adds time penalty but fresh tires may give position advantage
                double totalLapTime = baseLapTime + expectedTrafficEffect + fuelPenalty + _pitLoss;

                var nextState = state with
                {
                    CurrentTyre = tyreType,
                    TyreAge = 1,
                    LapsRemaining = state.LapsRemaining - 1,
                    UsedTyres = state.UsedTyres | ToUsageFlag(tyreType),
                    // Pitting drops positions but fresh tires may help recover
                    CurrentPosition = Math.Min(state.CurrentPosition + 2, 20), // Drop 2 positions (improved from pit loss)
                    GapToCarAhead = state.GapToCarAhead + 2.5, // Gap increases moderately
                    GapToCarBehind = Math.Max(0, state.GapToCarBehind - 2.5) // Gap decreases moderately
                };

                var next = Solve(nextState);
                double cost = totalLapTime + next.TotalTime;

                if (cost < best.TotalTime && !double.IsInfinity(cost))
                {
                    best = new StrategyResult(cost, StrategyAction.Pit, tyreType);
                }
            }

            _memo[state] = best;
            return best;
        }

        // Calculate expected traffic effect based on race position and gaps
        // ExpectedTrafficLoss = P(car ahead stays out) * trafficPenalty * f(gap) + P(car ahead pits) * 0
        private double CalculateExpectedTrafficEffect(RaceSolverState state)
        {
            // Base position effect (drivers further back have more traffic generally)
            double baseTraffic = (state.CurrentPosition - 1) * 0.02; // Reduced base effect

            // Expected traffic from car ahead
            double gapBasedTraffic = 0.0;

            if (state.GapToCarAhead <= 1.0)
            {
                // Close racing - high chance of traffic if car ahead doesn't pit
                gapBasedTraffic = _trafficPenalty * (1.0 - _pitProbability); // Full penalty if they stay out
            }
            else if (state.GapToCarAhead <= 3.0)
            {
                // Medium gap - moderate traffic
                gapBasedTraffic = _trafficPenalty * 0.4 * (1.0 - _pitProbability); // Reduced penalty
            }
            // Large gaps (>3s) have minimal traffic effect


            double expectedTraffic = baseTraffic + gapBasedTraffic;

            return Math.Max(0, expectedTraffic); // Traffic can only slow you down, not speed you up
        }


        // Estimate next position based on lap time and current situation
        private int CalculateNextPosition(RaceSolverState state, double lapTime)
        {
            // Simplified position calculation

            // Assume car ahead is 0.5s slower on average
            double assumedCarAheadLapTime = lapTime + 0.5;

            if (lapTime < assumedCarAheadLapTime)
            {
                // Fast lap - potential to gain position
                return Math.Max(1, state.CurrentPosition - 1);
            }
            else if (lapTime > assumedCarAheadLapTime + 1.0)
            {
                // Slow lap - potential to lose position
                return Math.Min(20, state.CurrentPosition + 1);
            }

            // Maintain position
            return state.CurrentPosition;
        }

        // Determine if driver should pit this lap using DP optimization
        public (bool shouldPit, TyreType? pitToTyre) ShouldPitThisLap(
            TyreType currentTyre,
            int tyreAge,
            int lapsRemaining,
            int currentPosition,
            double gapToCarAhead,
            double gapToCarBehind,
            TyreUsage usedTyres)
        {
            var currentState = new RaceSolverState(
                CurrentTyre: currentTyre,
                TyreAge: tyreAge,
                LapsRemaining: lapsRemaining,
                CurrentPosition: currentPosition,
                GapToCarAhead: gapToCarAhead,
                GapToCarBehind: gapToCarBehind,
                UsedTyres: usedTyres
            );

            var optimalAction = Solve(currentState);

            if (optimalAction.Action == StrategyAction.Pit && optimalAction.PitTo.HasValue)
            {
                return (true, optimalAction.PitTo.Value);
            }

            // If we're in final laps and haven't used 2 compounds, force a pit
            if (lapsRemaining <= 5 && CountBits(usedTyres) < 2)
            {
                var availableTyres = _tyres.Keys.Where(t => t != currentTyre).ToList();
                return (true, availableTyres.First());
            }

            return (false, null);
        }

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