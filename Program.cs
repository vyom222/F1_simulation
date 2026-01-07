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
            foreach (var r in results)
            {
                var tyre = TyreCreation.Create(r.Compound!, r.Slope, r.Intercept);
                tyres.Add(tyre);
            }

            // --------------------------------
            // Create solver
            // --------------------------------

            int raceLength = 66;      // Spain GP laps
            double pitLoss = 25.0;    // seconds (same unit as lap times)

            var solver = new OptimalStrategy(
                tyres,
                raceLength,
                pitLoss
            );

            // --------------------------------
            // Try ALL starting tyres
            // --------------------------------

            OptimalStrategy.StrategyResult? bestFinal = null;
            RaceState? bestStartState = null;

            foreach (var tyre in tyres)
            {
                var tyreType = tyre.Name switch
                {
                    "Soft" => TyreType.Soft,
                    "Medium" => TyreType.Medium,
                    "Hard" => TyreType.Hard,
                    _ => throw new ArgumentException()
                };

                var startState = new RaceState(
                    Tyre: tyreType,
                    TyreAge: 0,                 // brand new
                    LapsRemaining: raceLength,
                    Usage: ToUsageFlag(tyreType)
                );

                var result = solver.Solve(startState);

                Console.WriteLine(
                    $"Start on {tyreType,-6} → total time = {result.TotalTime:F2}"
                );

                if (bestFinal is null || result.TotalTime < bestFinal.Value.TotalTime)
                {
                    bestFinal = result;
                    bestStartState = startState;
                }
            }

            if (bestStartState is null)
            {
                Console.WriteLine("No valid strategy found.");
                return;
            }

            // --------------------------------
            // Reconstruct and print strategy
            // --------------------------------

            Console.WriteLine("\n--- Optimal Strategy ---\n");

            var strategy = solver.GetFullStrategy(bestStartState.Value);

            int lap = 1;
            foreach (var step in strategy)
            {
                if (step.Action == OptimalStrategy.StrategyAction.StayOut)
                {
                    //Console.WriteLine($"Lap {lap}: Stay out");
                }
                else
                {
                    Console.WriteLine($"Lap {lap}: Pit for {step.PitTo}");
                }

                lap++;
            }

            Console.WriteLine(
                $"\nTotal race time: {bestFinal.Value.TotalTime:F2}"
            );

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
// AFTER FIX THE ANOMALY AND LINES SO THAT THEY ARE ACCURATE
// THEN GET IT TO ALSO OUTPUT THE DRIVER'S LAP TIMES
// GET IT TO SIMULATE THE RACE - look into the thing where you simulate many different outcomes?
// CREATE FRONTEND - choose your race, compare your strat, simulate the race and quali?
