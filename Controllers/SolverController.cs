using F1_simulation.External;
using F1_simulation.Core.Tyres;
using F1_simulation.Core.Strategy_solver;
using F1_simulation.Core.Race_simulator;
using F1_simulation.Core.Monte_carlo_simulator;
using F1_simulation.Database;

using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using Microsoft.IdentityModel.Tokens;
using System.Diagnostics.Metrics;

namespace F1_simulation.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SolverController : ControllerBase
    {
        private readonly F1_cache _cache;

        public SolverController(F1_cache cache)
        {
            _cache = cache;
        }

        [HttpGet("health")]
        public IActionResult Health()
        {
            return Ok(new { status = "ok", service = "C# F1 Simulation API" });
        }

        [HttpGet("driver-data")]
        [HttpPost("driver-data")]
        public async Task<IActionResult> GetDriverData([FromQuery] string circuit = "Catalunya", [FromQuery] int year = 2024)
        {
            try
            {
                // Check cache for session keys first
                var cachedKeys = _cache.GetSessionKeys(circuit, year);
                List<int> keys;
                
                if (cachedKeys.Count > 0)
                {
                    keys = cachedKeys;
                }
                else
                {
                    // Fetch from API and cache
                    keys = await TyreModelClient.CallSessionsDataAsync(circuit, year);
                    if (keys == null || keys.Count == 0)
                        return NotFound(new { error = "No session keys found for the specified circuit and year" });
                    
                    _cache.AddSessions(circuit, year, keys);
                }
                    
                var driverData = await TyreModelClient.CallDriverDataAsync(keys);
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
        public async Task<IActionResult> GetTyreCurves([FromQuery] string circuit = "Catalunya", [FromQuery] int year = 2024)
        {
            try
            {
                // Check cache for tyre curves first
                var cachedCurves = _cache.GetTyreCurves(circuit, year);
                
                if (cachedCurves.Count > 0)
                {
                    // Parse cached curves
                    var results = new List<TyreModelClient.TyreResult>();
                    foreach (var curveStr in cachedCurves)
                    {
                        var parts = curveStr.Split(' ');
                        if (parts.Length == 3)
                        {
                            results.Add(new TyreModelClient.TyreResult
                            {
                                Compound = parts[0],
                                Slope = double.Parse(parts[1]),
                                Intercept = double.Parse(parts[2])
                            });
                        }
                    }
                    return Ok(new { success = true, curves = results });
                }

                // Not in cache, fetch from API
                var keys = _cache.GetSessionKeys(circuit, year);
                if (keys.Count == 0)
                {
                    keys = await TyreModelClient.CallSessionsDataAsync(circuit, year);
                    if (keys == null || keys.Count == 0)
                        return NotFound(new { error = "No session keys found for the specified circuit and year" });
                    
                    _cache.AddSessions(circuit, year, keys);
                }
                    
                var apiResults = await TyreModelClient.CallTyreModelAsync(keys);
                if (apiResults == null)
                    return NotFound(new { error = "No tyre curve data found" });

                // Cache the results
                foreach (var result in apiResults)
                {
                    _cache.AddTyreCurves(circuit, year, result.Compound!, result.Slope, result.Intercept);
                }

                return Ok(new { success = true, curves = apiResults });
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpGet("montecarlo")]
        public async Task<IActionResult> GetMonteCarlo([FromQuery] string circuit = "Catalunya", [FromQuery] int year = 2024, [FromQuery] int numSimulations = 1000)
        {
            try
            {
                // Check cache for session keys
                var keys = _cache.GetSessionKeys(circuit, year);
                if (keys.Count == 0)
                {
                    keys = await TyreModelClient.CallSessionsDataAsync(circuit, year);
                    if (keys == null || keys.Count == 0)
                        return NotFound(new { error = "No session keys found for the specified circuit and year" });
                    
                    _cache.AddSessions(circuit, year, keys);
                }

                // Check cache for tyre curves
                var cachedCurves = _cache.GetTyreCurves(circuit, year);
                List<TyreModelClient.TyreResult> results;
                
                if (cachedCurves.Count > 0)
                {
                    results = new List<TyreModelClient.TyreResult>();
                    foreach (var curveStr in cachedCurves)
                    {
                        var parts = curveStr.Split(' ');
                        if (parts.Length == 3)
                        {
                            results.Add(new TyreModelClient.TyreResult
                            {
                                Compound = parts[0],
                                Slope = double.Parse(parts[1]),
                                Intercept = double.Parse(parts[2])
                            });
                        }
                    }
                }
                else
                {
                    results = await TyreModelClient.CallTyreModelAsync(keys);
                    if (results == null)
                        return NotFound(new { error = "No tyre model data found" });
                    
                    // Cache the results
                    foreach (var result in results)
                    {
                        _cache.AddTyreCurves(circuit, year, result.Compound!, result.Slope, result.Intercept);
                    }
                }

                var tyres = new List<Tyre>();
                foreach (var r in results)
                {
                    tyres.Add(TyreCreation.Create(r.Compound!, r.Slope, r.Intercept));
                }

                var monteCarloSimulator = new MonteCarloSimulator();
                var monteCarloResult = await monteCarloSimulator.RunSimulation(
                    circuit: circuit,
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
        public async Task<IActionResult> GetTopStrategies([FromQuery] string circuit = "Catalunya", [FromQuery] int year = 2024, [FromQuery] int raceLength = 66)
        {
            try
            {
                // Check cache for session keys
                var keys = _cache.GetSessionKeys(circuit, year);
                if (keys.Count == 0)
                {
                    keys = await TyreModelClient.CallSessionsDataAsync(circuit, year);
                    if (keys == null || keys.Count == 0)
                        return NotFound(new { error = "No session keys found for the specified circuit and year" });
                    
                    _cache.AddSessions(circuit, year, keys);
                }

                // Check cache for tyre curves
                var cachedCurves = _cache.GetTyreCurves(circuit, year);
                List<TyreModelClient.TyreResult> results;
                
                if (cachedCurves.Count > 0)
                {
                    results = new List<TyreModelClient.TyreResult>();
                    foreach (var curveStr in cachedCurves)
                    {
                        var parts = curveStr.Split(' ');
                        if (parts.Length == 3)
                        {
                            results.Add(new TyreModelClient.TyreResult
                            {
                                Compound = parts[0],
                                Slope = double.Parse(parts[1]),
                                Intercept = double.Parse(parts[2])
                            });
                        }
                    }
                }
                else
                {
                    results = await TyreModelClient.CallTyreModelAsync(keys);
                    if (results == null)
                        return NotFound(new { error = "No tyre model data found" });
                    
                    // Cache the results
                    foreach (var result in results)
                    {
                        _cache.AddTyreCurves(circuit, year, result.Compound!, result.Slope, result.Intercept);
                    }
                }

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

                // Get strategies with windows
                var strategiesWithWindows = solver.FindMultipleStrategies();

                var ordered = strategiesWithWindows.OrderBy(s => s.BestTime).Take(3).ToList();

                var outList = new List<object>();

                foreach (var s in ordered)
                {
                    // Use center of pit windows for pit laps (most representative)
                    var pitLaps = s.PitWindowRanges.Select(w => (w.MinLap + w.MaxLap) / 2).ToList();

                    // Compute stint lengths based on pit laps
                    var compounds = s.CompoundSequence.Split("->");
                    var stints = new List<object>();
                    int currentLap = 1;
                    
                    for (int i = 0; i < compounds.Length; i++)
                    {
                        int stintLength;
                        if (i < pitLaps.Count)
                        {
                            // Pit on lap pitLaps[i]: complete lap pitLaps[i]-1 on current tyres, 
                            // pit during lap pitLaps[i], start lap pitLaps[i] on new tyres
                            stintLength = pitLaps[i] - currentLap;
                            currentLap = pitLaps[i];
                        }
                        else
                        {
                            // Final stint goes to the end
                            stintLength = raceLength - currentLap + 1;
                        }

                        stints.Add(new { compound = compounds[i], length = stintLength });
                    }

                    // Build pit windows list
                    var windows = s.PitWindowRanges.Select(w => new { 
                        min = w.MinLap, 
                        max = w.MaxLap, 
                        pitTo = w.PitTo.ToString() 
                    }).ToList();

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
        public async Task<IActionResult> GetQualifying([FromQuery] string circuit = "Catalunya", [FromQuery] int year = 2024)
        {
            try
            {
                // Check cache for session keys
                var keys = _cache.GetSessionKeys(circuit, year);
                if (keys.Count == 0)
                {
                    keys = await TyreModelClient.CallSessionsDataAsync(circuit, year);
                    if (keys == null || keys.Count == 0)
                        return NotFound(new { error = "No session keys found for the specified circuit and year" });
                    
                    _cache.AddSessions(circuit, year, keys);
                }
                    
                var driverData = await TyreModelClient.CallDriverDataAsync(keys);
                return Ok(new {success = true, qualifying = driverData});
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            
        }

        [HttpGet("race-pace")]
        public async Task<IActionResult> GetRacePace([FromQuery] string circuit = "Catalunya", [FromQuery] int year = 2024)
        {
            try
            {   
                // Check cache for session keys
                var keys = _cache.GetSessionKeys(circuit, year);
                if (keys.Count == 0)
                {
                    keys = await TyreModelClient.CallSessionsDataAsync(circuit, year);
                    if (keys == null || keys.Count == 0)
                        return NotFound(new { error = "No session keys found for the specified circuit and year" });
                    
                    _cache.AddSessions(circuit, year, keys);
                }
                    
                var racePaceData = await TyreModelClient.CallDriverDataAsync(keys);
                return Ok(new {success = true, racePace = racePaceData});
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            
        }

        [HttpGet("race-simulation")]
        public async Task<IActionResult> GetRaceSimulation([FromQuery] string circuit = "Catalunya", [FromQuery] int year = 2024, [FromQuery] int raceLength = 66)
        {
            try
            {
                // Check cache for session keys
                var keys = _cache.GetSessionKeys(circuit, year);
                if (keys.Count == 0)
                {
                    keys = await TyreModelClient.CallSessionsDataAsync(circuit, year);
                    if (keys == null || keys.Count == 0)
                        return NotFound(new { error = "No session keys found for the specified circuit and year" });
                    
                    _cache.AddSessions(circuit, year, keys);
                }

                // Check cache for tyre curves
                var cachedCurves = _cache.GetTyreCurves(circuit, year);
                List<TyreModelClient.TyreResult> results;
                
                if (cachedCurves.Count > 0)
                {
                    results = new List<TyreModelClient.TyreResult>();
                    foreach (var curveStr in cachedCurves)
                    {
                        var parts = curveStr.Split(' ');
                        if (parts.Length == 3)
                        {
                            results.Add(new TyreModelClient.TyreResult
                            {
                                Compound = parts[0],
                                Slope = double.Parse(parts[1]),
                                Intercept = double.Parse(parts[2])
                            });
                        }
                    }
                }
                else
                {
                    results = await TyreModelClient.CallTyreModelAsync(keys);
                    if (results == null)
                        return NotFound(new { error = "No tyre model data found" });
                    
                    // Cache the results
                    foreach (var result in results)
                    {
                        _cache.AddTyreCurves(circuit, year, result.Compound!, result.Slope, result.Intercept);
                    }
                }

                var tyres = new List<Tyre>();
                foreach (var r in results)
                {
                    tyres.Add(TyreCreation.Create(r.Compound!, r.Slope, r.Intercept));
                }

                // Run race simulation
                var raceResult = await RaceSimulator.SimulateRace(circuit, year, tyres, raceLength);

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
