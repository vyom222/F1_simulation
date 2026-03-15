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
            
            // Clear error handling
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

        // Gets the starting tyre for a given strategy
        public TyreType GetStartingTyre(OptimalStrategy.StrategyWithWindows strategy)
        {
            // Extract starting tyre from compound sequence "Soft->Hard->Hard"
            var compounds = strategy.CompoundSequence.Split("->");
            // Tryparse - defensive programming
            if (compounds.Length > 0 && Enum.TryParse<TyreType>(compounds[0], out var startingTyre))
            {
                return startingTyre;
            }
            
            // Fallback tyre - defensive programming
            return TyreType.Medium;
        }
    }
}
