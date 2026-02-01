using F1_simulation.External;
using F1_simulation.Core.Tyres;
using F1_simulation.Core.Strategy_solver;
using F1_simulation.Core.Race_simulator;
using F1_simulation.Core.Monte_carlo_simulator;
using Microsoft.AspNetCore.Mvc;

namespace F1_simulation.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SolverController : ControllerBase
    {
        [HttpGet("health")]
        public IActionResult Health()
        {
            return Ok(new { status = "ok", service = "C# F1 Simulation API" });
        }

        [HttpGet("run-solver")]
        [HttpPost("run-solver")]
        public async Task<IActionResult> RunSolver([FromQuery] string country = "Spain", [FromQuery] int year = 2025)
        {
            try
            {
                var output = new System.Text.StringBuilder();
                output.AppendLine("=== RACE SIMULATOR: QUALIFYING DATA ===");

                // Test the RaceSimulator component
                await RaceSimulator.RunQualifyingSimulation(country, year);

                output.AppendLine("\n=== MAIN APPLICATION ===");
                output.AppendLine("Hello from C# Web API!");

                // Check API health
                if (!await TyreModelClient.IsApiHealthy())
                {
                    //output.AppendLine("Tyre API not available");
                    return Ok(new { success = false, output = output.ToString() });
                }

                // Fetch tyre model
                var results = await TyreModelClient.CallTyreModelAsync(country, year);

                if (results is null)
                {
                    //output.AppendLine("No results returned");
                    return Ok(new { success = false, output = output.ToString() });
                }

                var driverData = await TyreModelClient.CallDriverDataAsync(country, year);

                if (driverData != null)
                {
                    //output.AppendLine("\n--- Driver Qualifying Simulation (from Practice Data) ---\n");

                    if (driverData.qualifying != null && driverData.qualifying.Count > 0)
                    {
                        foreach (var driver in driverData.qualifying)
                        {
                            //output.AppendLine($"{driver.position}. Driver {driver.driver_number}: ({driver.gap})");
                        }
                    }
                    else
                    {
                        //output.AppendLine("No qualifying data available.");
                    }

                    //output.AppendLine("\n--- Driver Race Pace (Residuals vs Baseline Model) ---\n");

                    if (driverData.race_pace != null && driverData.race_pace.Count > 0)
                    {
                        foreach (var driver in driverData.race_pace)
                        {
                            //output.AppendLine($"{driver.position}. Driver {driver.driver_number}: ({driver.gap_to_fastest})");
                        }
                    }
                    else
                    {
                        //output.AppendLine("No race pace data available (insufficient practice data for regression).");
                    }
                }
                else
                {
                    //output.AppendLine("No driver data received from API.");
                }

                // Build tyre objects
                var tyres = new List<Tyre>();

                foreach (var r in results)
                {
                    // output.AppendLine($"{r.Compound}: Slope = {r.Slope:F6}, Intercept = {r.Intercept:F6}");
                    var tyre = TyreCreation.Create(r.Compound!, r.Slope, r.Intercept);
                    tyres.Add(tyre);
                }

                // Create solver
                int raceLength = 66;
                double pitLoss = 25.0;
                double fuelPenalty = 0.05;
                double windowSize = 2.5;
                int numStrategies = 3;

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
                    //output.AppendLine("No valid strategies found.");
                    return Ok(new { success = false, output = output.ToString() });
                }

                // Display each strategy with its pit window ranges
                for (int i = 0; i < strategies.Count; i++)
                {
                    var strategy = strategies[i];
                    // output.AppendLine($"\n--- Strategy #{i + 1}: {strategy.CompoundSequence} ---");
                    // output.AppendLine($"Best race time: {strategy.BestTime:F2} seconds");
                    // output.AppendLine($"Time spread across windows: {strategy.TimeSpread:F1} seconds");

                    if (strategy.PitWindowRanges.Any())
                    {
                        // output.AppendLine("Pit window ranges:");
                        for (int j = 0; j < strategy.PitWindowRanges.Count; j++)
                        {
                            var window = strategy.PitWindowRanges[j];
                            string lapRange = window.MinLap == window.MaxLap ?
                                $"lap {window.MinLap}" :
                                $"laps {window.MinLap}-{window.MaxLap}";
                            // output.AppendLine($"  Pit {j + 1}: {lapRange} for {window.PitTo} (spread: {window.TimeSpread:F1}s)");
                        }
                    }
                    else
                    {
                        // output.AppendLine("No pit stops (single compound strategy)");
                    }
                }

                // Run race simulation
                output.AppendLine("\n=== RACE SIMULATION ===");
                var raceResult = await RaceSimulator.SimulateRace(country, year, tyres, 66);

                output.AppendLine("\n=== FINAL RACE RESULTS ===");
                output.AppendLine("Pos\tDriver\tTotal Time\tPit Stops");
                output.AppendLine("---\t------\t----------\t----------");

                foreach (var driver in raceResult.FinalPositions!)
                {
                    var pitCount = raceResult.PitStops!.GetValueOrDefault(driver.DriverNumber, new List<(int, TyreType)>()).Count;
                    output.AppendLine($"{driver.Position}\t{driver.DriverNumber}\t{driver.TotalTime:F1}s\t\t{pitCount}");
                }

                // Show winning driver's strategy
                var winner = raceResult.FinalPositions.First();
                var winnerPitStops = raceResult.PitStops!.GetValueOrDefault(winner.DriverNumber, new List<(int, TyreType)>());

                output.AppendLine("\n=== WINNING DRIVER STRATEGY ===");
                output.AppendLine($"Driver {winner.DriverNumber} - Champion!");
                output.AppendLine($"Starting Tyre: {winner.StartingTyre}");

                output.AppendLine("Complete Strategy:");
                var strategySequence = new List<string> { winner.StartingTyre.ToString() };
                foreach (var (lap, newTyre) in winnerPitStops)
                {
                    strategySequence.Add($"→ {newTyre} (Pit Lap {lap})");
                }
                output.AppendLine($"  {string.Join(" ", strategySequence)}");

                if (winnerPitStops.Any())
                {
                    output.AppendLine("Detailed Pit Stops:");
                    for (int i = 0; i < winnerPitStops.Count; i++)
                    {
                        var (lap, newTyre) = winnerPitStops[i];
                        output.AppendLine($"  Pit {i + 1}: Lap {lap} - {winner.StartingTyre}{(i > 0 ? $"(after {i} stops)" : "")} → {newTyre}");
                    }
                }
                else
                {
                    output.AppendLine("Strategy: No pit stops - stayed on starting tyres throughout");
                }

                output.AppendLine($"Final Position: {winner.Position}");
                output.AppendLine($"Total Race Time: {winner.TotalTime:F1} seconds");

                // Run monte carlo simulation
                output.AppendLine("\n=== MONTE CARLO SIMULATION ===");
                output.AppendLine("Running Monte Carlo simulation with randomized strategies...");

                var monteCarloSimulator = new MonteCarloSimulator(
                    gaussianNoiseStdDev: 0.3,
                    safetyCarProbability: 0.3,
                    minSafetyCarLap: 5,
                    maxSafetyCarLap: 60
                );

                var monteCarloResult = await monteCarloSimulator.RunSimulation(
                    country: country,
                    year: year,
                    tyres: tyres,
                    raceLength: 66,
                    pitLoss: 25.0,
                    trafficPenalty: 0.5,
                    numSimulations: 1000
                );

                output.AppendLine("\n=== Monte Carlo Results ===");
                output.AppendLine("Top 5 Average Positions:");
                var topPositions = monteCarloResult.AveragePositions
                    .OrderBy(kvp => kvp.Value)
                    .Take(20);

                foreach (var kvp in topPositions)
                {
                    output.AppendLine($"Driver {kvp.Key}: Position {kvp.Value:F2}");
                }

                return Ok(new { success = true, output = output.ToString() });
            }
            catch (Exception ex)
            {
                return Ok(new
                {
                    success = false,
                    output = $"Error: {ex.Message}\n\nStackTrace:\n{ex.StackTrace}"
                });
            }
        }

        [HttpGet("tyre-model")]
        [HttpPost("tyre-model")]
        public async Task<IActionResult> GetTyreModel([FromQuery] string country = "Spain", [FromQuery] int year = 2024)
        {
            try
            {
                var results = await TyreModelClient.CallTyreModelAsync(country, year);
                if (results == null)
                    return NotFound(new { error = "No tyre model data found" });

                return Ok(new { success = true, data = results });
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpGet("driver-data")]
        [HttpPost("driver-data")]
        public async Task<IActionResult> GetDriverData([FromQuery] string country = "Spain", [FromQuery] int year = 2024)
        {
            try
            {
                var driverData = await TyreModelClient.CallDriverDataAsync(country, year);
                if (driverData == null)
                    return NotFound(new { error = "No driver data found" });

                return Ok(new { success = true, data = driverData });
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpGet("tyre-curves")]
        public async Task<IActionResult> GetTyreCurves([FromQuery] string country = "Spain", [FromQuery] int year = 2024)
        {
            try
            {
                var results = await TyreModelClient.CallTyreModelAsync(country, year);
                if (results == null)
                    return NotFound(new { error = "No tyre curve data found" });

                return Ok(new { success = true, curves = results });
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpGet("top-strategies")]
        public async Task<IActionResult> GetTopStrategies([FromQuery] string country = "Spain", [FromQuery] int year = 2024, [FromQuery] int raceLength = 66)
        {
            try
            {
                var results = await TyreModelClient.CallTyreModelAsync(country, year);
                if (results == null)
                    return NotFound(new { error = "No tyre model data found" });

                var tyres = new List<Tyre>();
                foreach (var r in results)
                {
                    tyres.Add(TyreCreation.Create(r.Compound!, r.Slope, r.Intercept));
                }

                double pitLoss = 25.0;
                double fuelPenalty = 0.05;
                double windowSize = 2;
                int numStrategies = 3;

                var solver = new OptimalStrategy(
                    tyres,
                    raceLength,
                    pitLoss,
                    fuelPenalty,
                    windowSize,
                    numStrategies
                );

                // Get strategies with windows and basic strategies for exact pit laps
                var strategiesWithWindows = solver.FindMultipleStrategies();
                var basicStrategies = solver.FindBasicStrategies();

                var ordered = strategiesWithWindows.OrderBy(s => s.BestTime).Take(3).ToList();

                var outList = new List<object>();

                foreach (var s in ordered)
                {
                    // Try to find a matching basic strategy to get exact pit laps
                    var match = basicStrategies.FirstOrDefault(b => b.CompoundSequence == s.CompoundSequence);

                    var pitLaps = new List<int>();
                    if (match.PitStops != null && match.PitStops.Count > 0)
                    {
                        pitLaps = match.PitStops.Select(p => p.lap).ToList();
                    }
                    else
                    {
                        // Fallback: use center of pit windows
                        pitLaps = s.PitWindowRanges.Select(w => (w.MinLap + w.MaxLap) / 2).ToList();
                    }

                    // Compute stints lengths
                    var compounds = s.CompoundSequence.Split("->");
                    var stints = new List<object>();
                    int prevLap = 1;
                    for (int i = 0; i < compounds.Length; i++)
                    {
                        int stintLength;
                        if (i < pitLaps.Count)
                        {
                            stintLength = pitLaps[i] - prevLap + 1; // inclusive
                            prevLap = pitLaps[i] + 1;
                        }
                        else
                        {
                            stintLength = raceLength - prevLap + 1;
                        }

                        stints.Add(new { compound = compounds[i], length = stintLength });
                    }

                    // Build pit windows list
                    var windows = s.PitWindowRanges.Select(w => new { min = w.MinLap, max = w.MaxLap, pitTo = w.PitTo.ToString() }).ToList();

                    outList.Add(new {
                        compounds = compounds,
                        stints = stints,
                        pit_laps = pitLaps,
                        windows = windows,
                        best_time = s.BestTime,
                        time_spread = s.TimeSpread
                    });
                }

                return Ok(new { success = true, strategies = outList });
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpGet("python-health")]
        public async Task<IActionResult> CheckPythonHealth()
        {
            try
            {
                var isHealthy = await TyreModelClient.IsApiHealthy();
                return Ok(new { success = isHealthy, message = isHealthy ? "Python API is healthy" : "Python API is unreachable" });
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpGet("qualifying")]
        public async Task<IActionResult> GetQualifying([FromQuery] string country = "Spain", [FromQuery] int year = 2024)
        {
            try
            {
                var driverData = await TyreModelClient.CallDriverDataAsync(country, year);
                return Ok(new {success = true, qualifying = driverData});
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            
        }

        [HttpGet("race-pace")]
        public async Task<IActionResult> GetRacePace([FromQuery] string country = "Spain", [FromQuery] int year = 2024)
        {
            try
            {
                var racePaceData = await TyreModelClient.CallDriverDataAsync(country, year);
                return Ok(new {success = true, racePace = racePaceData});
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            
        }


        private static TyreUsage ToUsageFlag(TyreType tyre) => tyre switch
        {
            TyreType.Soft => TyreUsage.Soft,
            TyreType.Medium => TyreUsage.Medium,
            TyreType.Hard => TyreUsage.Hard,
            _ => throw new ArgumentOutOfRangeException()
        };
    }

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
