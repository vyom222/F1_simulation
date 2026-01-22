using F1_simulation.Core.Tyres;
using F1_simulation.Core.Strategy_solver;
using System;
using System.Collections.Generic;
using System.Linq;

namespace F1_simulation.Core.Race_simulator
{
    public enum StrategyAction { StayOut, Pit }

    public sealed class RaceSolver
    {
        private readonly Dictionary<TyreType, Tyre> _tyres;
        private readonly OptimalStrategy _strategicSolver;
        private readonly double _pitLoss;
        private readonly int _horizon;

        private readonly Dictionary<TacticalState, double> _memo = new();

        private readonly record struct TacticalState(
            int LapOffset,
            TyreType Tyre,
            int TyreAge
        );

        public RaceSolver(
            IEnumerable<Tyre> tyres,
            OptimalStrategy strategicSolver,
            double pitLoss,
            int horizon = 8)
        {
            _tyres = tyres.ToDictionary(t => t.Name switch
            {
                "Soft" => TyreType.Soft,
                "Medium" => TyreType.Medium,
                "Hard" => TyreType.Hard,
                _ => throw new ArgumentException()
            });

            _strategicSolver = strategicSolver;
            _pitLoss = pitLoss;
            _horizon = horizon;
        }

        public (StrategyAction action, TyreType? pitTo) Decide(
            int absoluteLap,
            int raceLength,
            TyreType tyre,
            int tyreAge,
            TyreUsage usedTyres,
            double trafficPenaltyThisLap)
        {
            _memo.Clear();

            var start = new TacticalState(0, tyre, tyreAge);

            double stay = Evaluate(start, absoluteLap, raceLength, usedTyres, trafficPenaltyThisLap);

            double bestPit = double.PositiveInfinity;
            TyreType? bestTyre = null;

            if (absoluteLap >= raceLength)
            {
                return (StrategyAction.StayOut, null);
            }


            if (tyreAge > 0) // Make sure they don't pit every other lap
            {
                foreach (var t in _tyres.Keys)
                {
                    var pitState = new TacticalState(1, t, 1);

                    double cost =
                        _pitLoss +
                        _tyres[t].LapTimes[0] +
                        Evaluate(pitState, absoluteLap, raceLength, usedTyres | ToUsageFlag(t), 0.0);

                    if (cost < bestPit)
                    {
                        bestPit = cost;
                        bestTyre = t;
                    }
                }
            }

            return bestPit < stay
                ? (StrategyAction.Pit, bestTyre)
                : (StrategyAction.StayOut, null);
        }

        private double Evaluate(
            TacticalState state,
            int absoluteLap,
            int raceLength,
            TyreUsage usedTyres,
            double trafficPenalty)
        {
            if (state.LapOffset >= _horizon || absoluteLap + state.LapOffset >= raceLength)
            {
                int lapsRemaining = raceLength - (absoluteLap + state.LapOffset);
                return _strategicSolver.Solve(new RaceState(
                    state.Tyre,
                    state.TyreAge,
                    lapsRemaining,
                    usedTyres
                )).TotalTime;
            }

            if (_memo.TryGetValue(state, out var cached))
                return cached;

            double best = double.PositiveInfinity;

            // ---- Stay out ----
            var tyre = _tyres[state.Tyre];
            if (state.TyreAge < tyre.LapTimes.Length)
            {
                double lap =
                    tyre.LapTimes[state.TyreAge] +
                    (state.LapOffset == 0 ? trafficPenalty : 0.0);

                best = lap + Evaluate(
                    state with
                    {
                        LapOffset = state.LapOffset + 1,
                        TyreAge = state.TyreAge + 1
                    },
                    absoluteLap,
                    raceLength,
                    usedTyres,
                    0.0
                );
            }

            _memo[state] = best;
            return best;
        }

        private static TyreUsage ToUsageFlag(TyreType tyre) => tyre switch
        {
            TyreType.Soft => TyreUsage.Soft,
            TyreType.Medium => TyreUsage.Medium,
            TyreType.Hard => TyreUsage.Hard,
            _ => TyreUsage.None
        };


    }
}
