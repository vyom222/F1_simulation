using F1_simulation.External;
using F1_simulation.Core.Tyres;
using F1_simulation.Core.Strategy_solver;
using F1_simulation.Core.Race_simulator;
using F1_simulation.Core.Monte_carlo_simulator;

namespace F1_simulation
{
    class Program
    {
        // Note use of async and Task
        static async Task Main(string[] args)
        {
            Console.WriteLine("=== RACE SIMULATOR: QUALIFYING DATA ===");

            // Test the RaceSimulator component
            await RaceSimulator.RunQualifyingSimulation("Spain", 2024);

            Console.WriteLine("\n=== MAIN APPLICATION ===");
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

            // Fetch driver qualifying and race pace from practice data only
            var driverData = await TyreModelClient.CallDriverDataAsync("Spain", 2024);

            if (driverData != null)
            {
                Console.WriteLine("\n--- Driver Qualifying Simulation (from Practice Data) ---\n");

                if (driverData.qualifying != null && driverData.qualifying.Count > 0)
                {
                    foreach (var driver in driverData.qualifying)
                    {
                        Console.WriteLine($"{driver.position}. Driver {driver.driver_number}: ({driver.gap})");
                    }
                }
                else
                {
                    Console.WriteLine("No qualifying data available.");
                }

                Console.WriteLine("\n--- Driver Race Pace (Residuals vs Baseline Model) ---\n");

                if (driverData.race_pace != null && driverData.race_pace.Count > 0)
                {
                    foreach (var driver in driverData.race_pace)
                    {
                        Console.WriteLine($"{driver.position}. Driver {driver.driver_number}: ({driver.gap_to_fastest})");
                    }
                }
                else
                {
                    Console.WriteLine("No race pace data available (insufficient practice data for regression).");
                }
            }
            else
            {
                Console.WriteLine("No driver data received from API.");
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

            // Run race simulation
            Console.WriteLine("\n=== RACE SIMULATION ===");
            var raceResult = await RaceSimulator.SimulateRace("Spain", 2024, tyres, 66);

            Console.WriteLine("\n=== FINAL RACE RESULTS ===");
            Console.WriteLine("Pos\tDriver\tTotal Time\tPit Stops");
            Console.WriteLine("---\t------\t----------\t----------");

            foreach (var driver in raceResult.FinalPositions)
            {
                var pitCount = raceResult.PitStops.GetValueOrDefault(driver.DriverNumber, new List<(int, TyreType)>()).Count;
                Console.WriteLine($"{driver.Position}\t{driver.DriverNumber}\t{driver.TotalTime:F1}s\t\t{pitCount}");
            }

            // Show winning driver's strategy
            var winner = raceResult.FinalPositions.First();
            var winnerPitStops = raceResult.PitStops.GetValueOrDefault(winner.DriverNumber, new List<(int, TyreType)>());

            Console.WriteLine("\n=== WINNING DRIVER STRATEGY ===");
            Console.WriteLine($"Driver {winner.DriverNumber} - Champion!");
            Console.WriteLine($"Starting Tyre: {winner.StartingTyre}");

            // Show the complete tyre strategy
            Console.WriteLine("Complete Strategy:");
            var strategySequence = new List<string> { winner.StartingTyre.ToString() };
            foreach (var (lap, newTyre) in winnerPitStops)
            {
                strategySequence.Add($"→ {newTyre} (Pit Lap {lap})");
            }
            Console.WriteLine($"  {string.Join(" ", strategySequence)}");

            if (winnerPitStops.Any())
            {
                Console.WriteLine("Detailed Pit Stops:");
                for (int i = 0; i < winnerPitStops.Count; i++)
                {
                    var (lap, newTyre) = winnerPitStops[i];
                    Console.WriteLine($"  Pit {i + 1}: Lap {lap} - {winner.StartingTyre}{(i > 0 ? $"(after {i} stops)" : "")} → {newTyre}");
                }
            }
            else
            {
                Console.WriteLine("Strategy: No pit stops - stayed on starting tyres throughout");
            }

            Console.WriteLine($"Final Position: {winner.Position}");
            Console.WriteLine($"Total Race Time: {winner.TotalTime:F1} seconds");

            // Run monte carlo simulation
            Console.WriteLine("\n=== MONTE CARLO SIMULATION ===");
            Console.WriteLine("Running Monte Carlo simulation with randomized strategies...");
            
            var monteCarloSimulator = new MonteCarloSimulator(
                gaussianNoiseStdDev: 0.3,
                safetyCarProbability: 0.3,
                minSafetyCarLap: 5,
                maxSafetyCarLap: 60
            );

            var monteCarloResult = await monteCarloSimulator.RunSimulation(
                country: "Spain",
                year: 2024,
                tyres: tyres,
                raceLength: 66,
                pitLoss: 25.0,
                trafficPenalty: 0.5,
                numSimulations: 1000
            );

            // Print average positions
            monteCarloResult.PrintAveragePositions();

            // Print position distribution for top 3 drivers by average position
            var topDrivers = monteCarloResult.AveragePositions
                .OrderBy(kvp => kvp.Value)
                .Take(3)
                .Select(kvp => kvp.Key)
                .ToList();

            Console.WriteLine("\n=== Position Distributions for Top 3 Drivers ===");
            foreach (var driverNum in topDrivers)
            {
                monteCarloResult.PrintPositionDistribution(driverNum);
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


// CREATE FRONTEND - choose your race, compare your strat, simulate the race and quali?
