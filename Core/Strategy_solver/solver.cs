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
        private readonly double _fuelPenaltyPerLap; // Seconds lost per lap of fuel remaining
        private readonly double _windowSizeSeconds; // Time window for grouping similar strategies
        private readonly int _numStrategies; // Number of different strategies to find

        public OptimalStrategy(
            IEnumerable<Tyre> tyres,
            int raceLength,
            double pitLossSeconds = 20,
            double fuelPenaltyPerLap = 0.05,
            double windowSizeSeconds = 2.5,  // 2.5 second window for grouping strategies
            int numStrategies = 3  // Find top 3 different compound sequences
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
            _fuelPenaltyPerLap = fuelPenaltyPerLap;
            _windowSizeSeconds = windowSizeSeconds;
            _numStrategies = numStrategies;
        }

        // Dynamic Programming Solver
        public StrategyResult Solve(RaceState state)
        {
            // ----- Base case -----
            if (state.LapsRemaining <= 0)
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
                    double lapTime = GetFuelAdjustedLapTime(tyre, state.TyreAge, state);

                    var nextState = state with
                    {
                        TyreAge = state.TyreAge + 1,
                        LapsRemaining = state.LapsRemaining - 1
                    };

                    var next = Solve(nextState);
                    double cost = lapTime + next.TotalTime;

                    if (cost < best.TotalTime && !double.IsInfinity(cost))
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
                double lapTime = GetFuelAdjustedLapTime(tyre, 0, state);
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

                if (cost < best.TotalTime && !double.IsInfinity(cost))
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


        // Define a simpler strategy structure without windows
        public readonly record struct BasicStrategy(
            string CompoundSequence,
            List<(int lap, TyreType pitTo)> PitStops,
            double TotalTime
        );

        // Find multiple strategies including timing variations for the same compounds
        public List<BasicStrategy> FindBasicStrategies()
        {
            var strategies = new List<BasicStrategy>();

            // Try each starting tyre and find multiple timing variations
            foreach (var startTyre in _tyres.Keys)
            {
                // Find the optimal strategy
                var startState = new RaceState(
                    Tyre: startTyre,
                    TyreAge: 0,
                    LapsRemaining: _raceLength,
                    Usage: ToUsageFlag(startTyre)
                );

                var optimalResult = Solve(startState);
                if (double.IsInfinity(optimalResult.TotalTime))
                    continue;

                var optimalStrategy = GetFullStrategy(startState);

                // Extract compound sequence from optimal strategy
                var compoundList = new List<TyreType> { startTyre };
                foreach (var step in optimalStrategy)
                {
                    if (step.Action == StrategyAction.Pit)
                    {
                        compoundList.Add(step.PitTo!.Value);
                    }
                }
                var compoundSequence = string.Join("->", compoundList);

                // Add the optimal strategy
                var pitStops = optimalStrategy
                    .Where(step => step.Action == StrategyAction.Pit)
                    .Select(step => (optimalStrategy.IndexOf(step) + 1, step.PitTo!.Value))
                    .ToList();

                strategies.Add(new BasicStrategy(compoundSequence, pitStops, optimalResult.TotalTime));
                
                // Also explore slight variations in pit timing (±3 laps) with ACTUAL evaluation
                // This helps create more realistic pit windows
                if (pitStops.Count > 0) 
                {
                    for (int offset = -3; offset <= 3; offset++)
                    {
                        if (offset == 0) continue;
                        
                        // Create a variation by trying to pit earlier/later
                        var variedStrategy = ExploreTimingVariation(startTyre, compoundList.ToArray(), pitStops, offset);
                        if (variedStrategy.HasValue && !double.IsInfinity(variedStrategy.Value.TotalTime))
                        {
                            strategies.Add(variedStrategy.Value);
                        }
                    }
                }
            }

            return strategies.OrderBy(s => s.TotalTime).ToList();
        }
        
        // Evaluate a specific strategy with given compound sequence and pit timings
        private BasicStrategy? ExploreTimingVariation(TyreType startTyre, TyreType[] compounds, List<(int lap, TyreType pitTo)> originalPits, int lapOffset)
        {
            if (compounds.Length < 2) return null;
            
            // Shift pit laps by offset
            var adjustedPits = new List<(int lap, TyreType pitTo)>();
            foreach (var (lap, pitTo) in originalPits)
            {
                int newLap = lap + lapOffset;
                if (newLap < 1 || newLap >= _raceLength) return null; // Invalid pit lap
                adjustedPits.Add((newLap, pitTo));
            }
            
            // Manually evaluate this specific strategy
            double totalTime = 0;
            TyreType currentTyre = startTyre;
            int tyreAge = 0;
            int lapsRemaining = _raceLength;
            
            for (int lap = 1; lap <= _raceLength; lap++)
            {
                // Check if we pit on this lap
                var pitOnThisLap = adjustedPits.FirstOrDefault(p => p.lap == lap);
                
                if (pitOnThisLap != default)
                {
                    // Pit: add pit loss and start new stint
                    totalTime += _pitLoss;
                    currentTyre = pitOnThisLap.pitTo;
                    tyreAge = 0;
                }
                
                // Complete this lap
                var tyre = _tyres[currentTyre];
                int safeTyreAge = Math.Min(tyreAge, tyre.LapTimes.Length - 1);
                double baseLapTime = tyre.LapTimes[safeTyreAge];
                double fuelPenalty = lapsRemaining * _fuelPenaltyPerLap;
                totalTime += baseLapTime + fuelPenalty;
                
                tyreAge++;
                lapsRemaining--;
            }
            
            // Check if uses at least 2 compounds
            var usedCompounds = new HashSet<TyreType> { startTyre };
            usedCompounds.UnionWith(adjustedPits.Select(p => p.pitTo));
            if (usedCompounds.Count < 2) return null;
            
            var compoundSequence = string.Join("->", compounds);
            return new BasicStrategy(compoundSequence, adjustedPits, totalTime);
        }

        // Group similar strategies into windows (both by compound sequence and time proximity)
        public List<StrategyWithWindows> CreatePitWindowsFromStrategies(List<BasicStrategy> strategies)
        {
            var groupedStrategies = new List<StrategyWithWindows>();

            // Group by compound sequence
            var bySequence = strategies.GroupBy(s => s.CompoundSequence);

            foreach (var sequenceGroup in bySequence)
            {
                var sequenceStrategies = sequenceGroup.OrderBy(s => s.TotalTime).ToList();
                var compoundSequence = sequenceGroup.Key;

                // Sub-group by time proximity (within 2.5 seconds)
                var timeGroups = GroupByTimeProximity(sequenceStrategies, 2.5);

                foreach (var timeGroup in timeGroups)
                {
                    if (groupedStrategies.Count >= _numStrategies) break;

                    var timeGroupStrategies = timeGroup.OrderBy(s => s.TotalTime).ToList();
                    var bestTime = timeGroupStrategies.First().TotalTime;
                    var timeSpread = timeGroupStrategies.Last().TotalTime - bestTime;

                    // Create windows from all strategies in this time group
                    var windows = CreateWindowsForTimeGroup(timeGroupStrategies);

                    groupedStrategies.Add(new StrategyWithWindows(
                        CompoundSequence: compoundSequence,
                        PitWindowRanges: windows,
                        BestTime: bestTime,
                        TimeSpread: timeSpread
                    ));
                }
            }

            return groupedStrategies.OrderBy(s => s.BestTime).Take(_numStrategies).ToList();
        }

        // Group strategies by time proximity (within 2.5 seconds)
        private List<List<BasicStrategy>> GroupByTimeProximity(List<BasicStrategy> strategies, double maxTimeDiff)
        {
            var groups = new List<List<BasicStrategy>>();
            var remaining = new List<BasicStrategy>(strategies);

            while (remaining.Any())
            {
                var group = new List<BasicStrategy> { remaining[0] };
                remaining.RemoveAt(0);

                // Find all strategies within time range of this group
                var groupMinTime = group.Min(s => s.TotalTime);
                var groupMaxTime = group.Max(s => s.TotalTime);

                var toAdd = remaining.Where(s =>
                    s.TotalTime >= groupMinTime - maxTimeDiff &&
                    s.TotalTime <= groupMaxTime + maxTimeDiff).ToList();

                group.AddRange(toAdd);
                foreach (var strategy in toAdd)
                {
                    remaining.Remove(strategy);
                }

                groups.Add(group.OrderBy(s => s.TotalTime).ToList());
            }

            return groups;
        }

        // Create windows by grouping similar pit timings from strategies in the same time group
        private List<PitWindowRange> CreateWindowsForTimeGroup(List<BasicStrategy> strategies)
        {
            if (strategies.Count == 0)
                return new List<PitWindowRange>();

            // Find the maximum number of pit stops across all strategies in this group
            int maxPitStops = strategies.Max(s => s.PitStops.Count);

            var windows = new List<PitWindowRange>();

            // For each pit stop position, find the range of laps across all strategies
            for (int pitIndex = 0; pitIndex < maxPitStops; pitIndex++)
            {
                var pitLaps = new List<int>();
                TyreType? pitTo = null;

                // Collect all pit laps for this position across all strategies in the time group
                foreach (var strategy in strategies)
                {
                    if (strategy.PitStops.Count > pitIndex)
                    {
                        pitLaps.Add(strategy.PitStops[pitIndex].lap);
                        pitTo = strategy.PitStops[pitIndex].pitTo;
                    }
                }

                if (pitLaps.Any() && pitTo.HasValue)
                {
                    int minLap = pitLaps.Min();
                    int maxLap = pitLaps.Max();
                    
                    // Limit window size to maximum 15 laps
                    const int MAX_WINDOW_SIZE = 15;
                    int windowSize = maxLap - minLap;
                    
                    if (windowSize > MAX_WINDOW_SIZE)
                    {
                        // If window is too large, use a smaller window around the most common pit lap
                        int medianLap = pitLaps.OrderBy(l => l).ElementAt(pitLaps.Count / 2);
                        minLap = Math.Max(1, medianLap - MAX_WINDOW_SIZE / 2);
                        maxLap = Math.Min(_raceLength - 1, medianLap + MAX_WINDOW_SIZE / 2);
                    }

                    // Calculate time spread based on lap range (rough estimate)
                    double timeSpread = (maxLap - minLap) * 0.15; // 0.15 seconds per lap difference

                    windows.Add(new PitWindowRange(minLap, maxLap, pitTo.Value, timeSpread));
                }
            }

            return windows;
        }

        // Main method that combines both steps
        public List<StrategyWithWindows> FindMultipleStrategies()
        {
            var basicStrategies = FindBasicStrategies();
            return CreatePitWindowsFromStrategies(basicStrategies);
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

        // Represents a pit window range with timing flexibility
        public readonly record struct PitWindowRange(
            int MinLap,
            int MaxLap,
            TyreType PitTo,
            double TimeSpread  // Time difference between best and worst in this range
        );

        // Complete strategy with pit window ranges
        public readonly record struct StrategyWithWindows(
            string CompoundSequence,  // e.g., "Soft->Hard->Hard"
            List<PitWindowRange> PitWindowRanges,
            double BestTime,         // Best time in the strategy
            double TimeSpread        // Time difference across all windows
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




        // Add fuel penalty based on laps remaining
        private double GetFuelAdjustedLapTime(Tyre tyre, int tyreAge, RaceState state)
        {
            // Ensure tyre age is valid (minimum 0) and doesn't exceed available degradation data
            int safeTyreAge = Math.Max(0, Math.Min(tyreAge, tyre.LapTimes.Length - 1));

            // Additional safety check
            if (safeTyreAge >= tyre.LapTimes.Length || safeTyreAge < 0)
            {
                // Fallback to first available lap time if something goes wrong
                safeTyreAge = 0;
            }

            double baseLapTime = tyre.LapTimes[safeTyreAge];

            // Fuel penalty: laps remaining * 0.05 seconds
            double fuelPenalty = state.LapsRemaining * _fuelPenaltyPerLap;

            return baseLapTime + fuelPenalty;
        }
    }
}
