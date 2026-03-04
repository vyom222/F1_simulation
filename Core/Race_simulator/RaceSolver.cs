using F1_simulation.Core.Tyres;
using F1_simulation.Core.Strategy_solver;
using System;
using System.Collections.Generic;
using System.Linq;

namespace F1_simulation.Core.Race_simulator
{
    public enum StrategyAction { StayOut, Pit }

    public enum TrafficLevel
    {
        FreeAir = 0,     
        DirtyAir = 1,    
        CloseTraffic = 2 
    }

    // Information about the driver ahead - stored alongside memo but not used in DP comparison
    public record DriverAheadInfo(
        double Gap,
        List<double> LapTimes,
        List<(int Lap, TyreType PitTo)> Strategy
    );

    public sealed class RaceSolver
    {
        private readonly Dictionary<TyreType, Tyre> _tyres;
        private readonly OptimalStrategy _strategicSolver;
        private readonly double _pitLoss;
        private readonly int _horizon;

        private const double DirtyAirPenalty = -0.3;     
        private const double ClosePenalty = 0.2;  

        // Gap thresholds for traffic levels
        private const double FreeAirGap = 3.0;  
        private const double DirtyAirGap = 1.0; 

        // Only TacticalState is compared for memoisation, DriverAheadInfo is carried along
        private readonly Dictionary<TacticalState, (double Time, DriverAheadInfo AheadInfo)> _memo = new();

        private readonly record struct TacticalState(
            int LapOffset,
            TyreType Tyre,
            int TyreAge,
            double FuelRemaining,
            TrafficLevel Traffic  
        );

        private List<double>? _driverAheadLapTimes;
        private List<(int Lap, TyreType PitTo)>? _driverAheadPitStops;
        private TyreType _driverAheadStartTyre;

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

        // Pre-compute the optimal free-air strategy for the driver ahead.
        // Assumes the driver ahead follows the optimal strategy.
        private void ComputeDriverAheadStrategy(int raceLength, TyreType startTyre)
        {
            _driverAheadStartTyre = startTyre;
            _driverAheadLapTimes = new List<double>();
            _driverAheadPitStops = new List<(int, TyreType)>();

            var currentTyre = startTyre;
            int tyreAge = 0;
            var usedTyres = ToUsageFlag(startTyre);

            for (int lap = 1; lap <= raceLength; lap++)
            {
                int lapsRemaining = raceLength - lap + 1;
                var state = new RaceState(currentTyre, tyreAge, lapsRemaining, usedTyres);
                var decision = _strategicSolver.Solve(state);

                var tyre = _tyres[currentTyre];
                int safeTyreAge = Math.Min(tyreAge, tyre.LapTimes.Length - 1);
                double lapTime = tyre.LapTimes[safeTyreAge] + lapsRemaining * 0.05; // fuel penalty

                if (decision.Action == OptimalStrategy.StrategyAction.Pit && decision.PitTo.HasValue)
                {
                    lapTime += _pitLoss;
                    _driverAheadPitStops.Add((lap, decision.PitTo.Value));
                    usedTyres |= ToUsageFlag(decision.PitTo.Value);
                    currentTyre = decision.PitTo.Value;
                    tyreAge = 1;
                }
                else
                {
                    tyreAge++;
                }

                _driverAheadLapTimes.Add(lapTime);
            }
        }

        // Determine traffic level based on gap to driver ahead
        private static TrafficLevel DetermineTrafficLevel(double gap)
        {
            if (gap >= FreeAirGap) return TrafficLevel.FreeAir;
            if (gap >= DirtyAirGap) return TrafficLevel.DirtyAir;
            return TrafficLevel.CloseTraffic;
        }

        private static double GetTrafficPenalty(TrafficLevel level) => level switch
        {
            TrafficLevel.FreeAir => 0,
            TrafficLevel.DirtyAir => DirtyAirPenalty,
            TrafficLevel.CloseTraffic => ClosePenalty,
            _ => 0
        };

        public (StrategyAction action, TyreType? pitTo, DriverAheadInfo? aheadInfo) Decide(
            int absoluteLap,
            int raceLength,
            TyreType tyre,
            int tyreAge,
            TyreUsage usedTyres,
            double initialGapToAhead,
            double fuelRemaining,
            TyreType driverAheadStartTyre = TyreType.Medium)
        {
            _memo.Clear();

            // Compute driver ahead's optimal free-air strategy
            ComputeDriverAheadStrategy(raceLength, driverAheadStartTyre);

            if (absoluteLap >= raceLength)
            {
                return (StrategyAction.StayOut, null, null);
            }

            // Determine initial traffic level from initial gap
            var initialTraffic = DetermineTrafficLevel(initialGapToAhead);

            var start = new TacticalState(0, tyre, tyreAge, fuelRemaining, initialTraffic);
            var stayResult = Evaluate(start, absoluteLap, raceLength, usedTyres, initialGapToAhead);
            double stay = stayResult.Time;

            double bestPit = double.PositiveInfinity;
            TyreType? bestTyre = null;
            DriverAheadInfo? bestPitAheadInfo = null;

            if (tyreAge > 0) // Prevent pitting every lap
            {
                foreach (var t in _tyres.Keys)
                {
                    // After pitting, we rejoin with a larger gap (pit loss added)
                    // This typically means we're in free air after a pit
                    double gapAfterPit = initialGapToAhead + _pitLoss;
                    var trafficAfterPit = DetermineTrafficLevel(gapAfterPit);

                    var pitState = new TacticalState(1, t, 1, fuelRemaining - 1, trafficAfterPit);

                    var tyre_obj = _tyres[t];
                    double outLapTime = tyre_obj.LapTimes[0] + (fuelRemaining - 1) * 0.05;

                    var result = Evaluate(pitState, absoluteLap, raceLength, usedTyres | ToUsageFlag(t), gapAfterPit);
                    double cost = _pitLoss + outLapTime + result.Time;

                    if (cost < bestPit)
                    {
                        bestPit = cost;
                        bestTyre = t;
                        bestPitAheadInfo = result.AheadInfo;
                    }
                }
            }

            if (bestPit < stay)
            {
                return (StrategyAction.Pit, bestTyre, bestPitAheadInfo);
            }
            return (StrategyAction.StayOut, null, stayResult.AheadInfo);
        }

        // Evaluate the total time from a given state to the end of the horizon.
        // Simulates both our car and the driver ahead to track gap and apply traffic.
        private (double Time, DriverAheadInfo AheadInfo) Evaluate(
            TacticalState state,
            int absoluteLap,
            int raceLength,
            TyreUsage usedTyres,
            double currentGap)
        {
            // Base case: reached horizon or end of race
            if (state.LapOffset >= _horizon || absoluteLap + state.LapOffset >= raceLength)
            {
                int lapsRemaining = raceLength - (absoluteLap + state.LapOffset);
                double strategicTime = _strategicSolver.Solve(new RaceState(
                    state.Tyre,
                    state.TyreAge,
                    lapsRemaining,
                    usedTyres
                )).TotalTime;

                // Return remaining laps info for driver ahead
                int aheadLapIndex = absoluteLap + state.LapOffset - 1;
                var remainingAheadLaps = aheadLapIndex < _driverAheadLapTimes!.Count
                    ? _driverAheadLapTimes.Skip(aheadLapIndex).ToList()
                    : new List<double>();
                var remainingAheadPits = _driverAheadPitStops!
                    .Where(p => p.Lap > absoluteLap + state.LapOffset)
                    .ToList();

                return (strategicTime, new DriverAheadInfo(currentGap, remainingAheadLaps, remainingAheadPits));
            }

            if (_memo.TryGetValue(state, out var cached))
                return cached;

            double best = double.PositiveInfinity;
            DriverAheadInfo bestAheadInfo = new(currentGap, new List<double>(), new List<(int, TyreType)>());

            // ---- Stay out ----
            var tyre = _tyres[state.Tyre];
            if (state.TyreAge < tyre.LapTimes.Length)
            {
                // Our lap time with traffic penalty
                double trafficPenalty = GetTrafficPenalty(state.Traffic);
                double ourLapTime = tyre.LapTimes[state.TyreAge] + trafficPenalty + (state.FuelRemaining * 0.05);

                // Driver ahead's lap time for this lap
                int aheadLapIndex = absoluteLap + state.LapOffset - 1;
                double aheadLapTime = (aheadLapIndex >= 0 && aheadLapIndex < _driverAheadLapTimes!.Count)
                    ? _driverAheadLapTimes[aheadLapIndex]
                    : ourLapTime; // Fallback if out of bounds

                // Update gap: positive gap means we're behind
                double newGap = currentGap + ourLapTime - aheadLapTime;
                
                // Gap can't go negative (would mean we overtake - assume we do and are then in free air)
                if (newGap <= 0)
                {
                    newGap = FreeAirGap + 67; // Reset to free air and make sure we don't end up back in traffic
                }

                // Determine new traffic level based on new gap
                TrafficLevel newTraffic = DetermineTrafficLevel(newGap);

                var nextState = state with
                {
                    LapOffset = state.LapOffset + 1,
                    TyreAge = state.TyreAge + 1,
                    FuelRemaining = Math.Max(0, state.FuelRemaining - 1),
                    Traffic = newTraffic
                };

                var result = Evaluate(nextState, absoluteLap, raceLength, usedTyres, newGap);

                best = ourLapTime + result.Time;
                bestAheadInfo = new DriverAheadInfo(
                    newGap,
                    result.AheadInfo.LapTimes,
                    result.AheadInfo.Strategy
                );
            }

            _memo[state] = (best, bestAheadInfo);
            return (best, bestAheadInfo);
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
