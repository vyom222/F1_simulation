using System;
using System.Collections.Generic;
using System.Linq;
using F1_simulation.Core.Strategy_solver;
using F1_simulation.Core.Tyres;

namespace F1_simulation.Core.Monte_carlo_simulator
{
    public class MonteCarloSolver
    {
        private readonly OptimalStrategy _optimalStrategy;
        private readonly Random _random;
        private readonly List<OptimalStrategy.StrategyWithWindows> _availableStrategies;

        public MonteCarloSolver(
            IEnumerable<Tyre> tyres,
            int raceLength,
            double pitLoss,
            Random? random = null)
        {
            _random = random ?? new Random();
            _optimalStrategy = new OptimalStrategy(tyres, raceLength, pitLoss);
            
            // Find the top 3 optimal strategies
            _availableStrategies = _optimalStrategy.FindMultipleStrategies();
            
            if (_availableStrategies.Count == 0)
            {
                throw new InvalidOperationException("No valid strategies found for Monte Carlo simulation");
            }
        }

        // Randomly selects one of the top 3 optimal strategies
        public OptimalStrategy.StrategyWithWindows SelectRandomStrategy()
        {
            if (_availableStrategies.Count == 0)
                throw new InvalidOperationException("No strategies available");

            int index = _random.Next(_availableStrategies.Count);
            return _availableStrategies[index];
        }

        // Randomizes pit windows within the strategy's allowed ranges
        // Returns a list of (lap, tyre) pit stops
        public List<(int lap, TyreType pitTo)> RandomizePitWindows(
            OptimalStrategy.StrategyWithWindows strategy,
            int raceLength)
        {
            var pitStops = new List<(int lap, TyreType pitTo)>();

            foreach (var window in strategy.PitWindowRanges)
            {
                // Randomly select a lap within the window range
                int pitLap = _random.Next(window.MinLap, window.MaxLap + 1);
                
                // Ensure pit lap is within race bounds
                pitLap = Math.Max(1, Math.Min(pitLap, raceLength - 1));
                
                pitStops.Add((pitLap, window.PitTo));
            }

            // Sort by lap number to ensure correct order
            return pitStops.OrderBy(p => p.lap).ToList();
        }

        // Gets the starting tyre for a given strategy
        public TyreType GetStartingTyre(OptimalStrategy.StrategyWithWindows strategy)
        {
            // Extract starting tyre from compound sequence (e.g., "Soft->Hard->Hard")
            var compounds = strategy.CompoundSequence.Split("->");
            if (compounds.Length > 0 && Enum.TryParse<TyreType>(compounds[0], out var startingTyre))
            {
                return startingTyre;
            }
            
            // Fallback to first available tyre
            return TyreType.Medium;
        }

        // Checks if a driver should pit based on their randomized strategy
        public bool ShouldPitThisLap(
            int currentLap,
            List<(int lap, TyreType pitTo)> plannedPitStops,
            TyreType currentTyre,
            int tyreAge)
        {
            // Find the next planned pit stop
            var nextPit = plannedPitStops.FirstOrDefault(p => p.lap > currentLap);
            
            if (nextPit.lap == 0) // No more pit stops
                return false;

            // Pit if we've reached the planned lap
            if (currentLap >= nextPit.lap)
            {
                return true;
            }

            return false;
        }

        // Gets the tyre to pit to for the next pit stop
        public TyreType? GetNextPitTyre(
            int currentLap,
            List<(int lap, TyreType pitTo)> plannedPitStops)
        {
            var nextPit = plannedPitStops.FirstOrDefault(p => p.lap > currentLap);
            return nextPit.lap > 0 ? nextPit.pitTo : null;
        }
    }
}
