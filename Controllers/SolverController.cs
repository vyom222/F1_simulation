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

        [HttpGet("montecarlo")]
        public async Task<IActionResult> GetMonteCarlo([FromQuery] string country = "Spain", [FromQuery] int year = 2024, [FromQuery] int numSimulations = 1000)
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

                var monteCarloSimulator = new MonteCarloSimulator();
                var monteCarloResult = await monteCarloSimulator.RunSimulation(
                    country: country,
                    year: year,
                    tyres: tyres,
                    raceLength: 66,
                    pitLoss: 25.0,
                    trafficPenalty: 0.5,
                    numSimulations: numSimulations
                );

                // Convert PositionCounts to a serializable structure
                var positionCountsOut = new Dictionary<int, Dictionary<int, int>>();
                foreach (var kvp in monteCarloResult.PositionCounts)
                {
                    positionCountsOut[kvp.Key] = kvp.Value.ToDictionary(x => x.Key, x => x.Value);
                }

                return Ok(new {
                    success = true,
                    averagePositions = monteCarloResult.AveragePositions,
                    positionCounts = positionCountsOut,
                    simulations = monteCarloResult.AllSimulations?.Count ?? 0,
                    medianPosition = monteCarloResult.MedianPosition
                });
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

        [HttpGet("race-simulation")]
        public async Task<IActionResult> GetRaceSimulation([FromQuery] string country = "Spain", [FromQuery] int year = 2024, [FromQuery] int raceLength = 66)
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

                // Run race simulation
                var raceResult = await RaceSimulator.SimulateRace(country, year, tyres, raceLength);

                // Build race results data
                var raceResults = new List<object>();
                double? firstPlaceTime = null;

                foreach (var driver in raceResult.FinalPositions!.OrderBy(d => d.Position))
                {
                    if (firstPlaceTime == null)
                        firstPlaceTime = driver.TotalTime;

                    // Get strategy from pit stops
                    var pitStops = raceResult.PitStops!.GetValueOrDefault(driver.DriverNumber, new List<(int, TyreType)>());
                    var strategyParts = new List<string> { driver.StartingTyre.ToString()[0].ToString() };
                    foreach (var pitStop in pitStops)
                    {
                        strategyParts.Add(pitStop.Item2.ToString()[0].ToString());
                    }
                    var strategyString = string.Join("-", strategyParts);

                    var deltaToFirst = driver.Position == 1 ? 0.0 : driver.TotalTime - firstPlaceTime.Value;

                    raceResults.Add(new {
                        position = driver.Position,
                        driverNumber = driver.DriverNumber,
                        strategy = strategyString,
                        totalTime = driver.TotalTime,
                        deltaToFirst = deltaToFirst
                    });
                }

                return Ok(new {
                    success = true,
                    raceResults = raceResults
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }
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
