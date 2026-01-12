using F1_simulation.External;
using F1_simulation.Core.Tyres;
using F1_simulation.Core.Strategy_solver;

namespace F1_simulation
{
    class Program
    {
        // Note use of async and Task
        static async Task Main(string[] args)
        {
            Console.WriteLine("Hello, World!");

            // Check API health
            if (!await TyreModelClient.IsApiHealthy())
            {
                Console.WriteLine("Tyre API not available"); // uvicorn Python.api:app --reload
                return;
            }

            // Fetch tyre model
            var results = await TyreModelClient.CallTyreModelAsync("Spain", 2024);

            if (results is null)
            {
                Console.WriteLine("No results returned");
                return;
            }

            // Build tyre objects
            var tyres = new List<Tyre>();

            // Exclamation mark because I know not null (from my own API)
            Console.WriteLine("\n--- Tyre Parameters from API ---\n");
            foreach (var r in results)
            {
                Console.WriteLine($"{r.Compound}: Slope = {r.Slope:F6}, Intercept = {r.Intercept:F6}");
                var tyre = TyreCreation.Create(r.Compound!, r.Slope, r.Intercept);
                tyres.Add(tyre);
            }
            

            // Create solver
            int raceLength = 66;      // Spain GP laps
            double pitLoss = 25.0;    // seconds (same unit as lap times)
            double fuelPenalty = 0.05;  // Seconds lost per lap of fuel remaining
            double windowSize = 2.5;  // 2.5 second window for grouping strategies
            int numStrategies = 3;     // Find top 3 different compound sequences

            var solver = new OptimalStrategy(
                tyres,
                raceLength,
                pitLoss,
                fuelPenalty,
                windowSize,
                numStrategies
            );

            // Find multiple different strategies with pit windows
            var strategies = solver.FindMultipleStrategies();

            if (strategies.Count == 0)
            {
                Console.WriteLine("No valid strategies found.");
                return;
            }

            // Display each strategy with its pit window ranges
            for (int i = 0; i < strategies.Count; i++)
            {
                var strategy = strategies[i];
                Console.WriteLine($"\n--- Strategy #{i + 1}: {strategy.CompoundSequence} ---");
                Console.WriteLine($"Best race time: {strategy.BestTime:F2} seconds");
                Console.WriteLine($"Time spread across windows: {strategy.TimeSpread:F1} seconds");

                if (strategy.PitWindowRanges.Any())
                {
                    Console.WriteLine("Pit window ranges:");
                    for (int j = 0; j < strategy.PitWindowRanges.Count; j++)
                    {
                        var window = strategy.PitWindowRanges[j];
                        string lapRange = window.MinLap == window.MaxLap ?
                            $"lap {window.MinLap}" :
                            $"laps {window.MinLap}-{window.MaxLap}";
                        Console.WriteLine($"  Pit {j + 1}: {lapRange} for {window.PitTo} (spread: {window.TimeSpread:F1}s)");
                    }
                }
                else
                {
                    Console.WriteLine("No pit stops (single compound strategy)");
                }
            }

            Console.WriteLine("\nDone.");
        }

        private static TyreUsage ToUsageFlag(TyreType tyre) => tyre switch
        {
            TyreType.Soft => TyreUsage.Soft,
            TyreType.Medium => TyreUsage.Medium,
            TyreType.Hard => TyreUsage.Hard,
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    // Switch statement for cleaner code and readability and allows for later extension
    static class TyreCreation
    {
        public static Tyre Create(string compound, double slope, double intercept)
        {
            return compound.ToUpperInvariant() switch
            {
                "SOFT" => new SoftTyre(slope, intercept),
                "MEDIUM" => new MediumTyre(slope, intercept),
                "HARD" => new HardTyre(slope, intercept),
                _ => throw new ArgumentException($"Unknown compound: {compound}")
            };
        }
    }
}


// NEXT JOB GET IT TO FIND THE BEST STRATEGY AND OUTPUT IT
// THEN GET IT TO ALSO OUTPUT THE DRIVER'S LAP TIMES
// GET IT TO SIMULATE THE RACE - look into the thing where you simulate many different outcomes?
// CREATE FRONTEND - choose your race, compare your strat, simulate the race and quali?
