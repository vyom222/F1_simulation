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

        private IActionResult? CheckCancelledRace(string circuit, int year)
        {
            if (circuit.Equals("Imola", StringComparison.OrdinalIgnoreCase) && year == 2023)
            {
                return BadRequest(new { error = "2023 Imola Grand Prix was cancelled" });
            }
            return null;
        }

        [HttpGet("health")]
        public IActionResult Health()
        {
            return Ok(new { status = "ok", service = "C# F1 Simulation API" });
        }

        [HttpGet("drivers")]
        public IActionResult GetDrivers()
        {
            try
            {
                var drivers = _cache.GetAllDrivers();
                return Ok(new { success = true, drivers = drivers });
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpGet("teams")]
        public IActionResult GetTeams()
        {
            try
            {
                var teams = _cache.GetAllTeams();
                return Ok(new { success = true, teams = teams });
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpGet("driver-teams")]
        public IActionResult GetDriverTeams([FromQuery] int year = 2024)
        {
            try
            {
                var driverTeams = _cache.GetDriverTeamsByYear(year);
                return Ok(new { success = true, driver_teams = driverTeams });
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpGet("laps")]
        [HttpPost("laps")]
        public async Task<IActionResult> GetLapsData([FromQuery] string circuit = "Catalunya")
        {
            try
            {
                // Check cache for session keys first
                int laps = _cache.GetLaps(circuit);
                return Ok(new { success = true, data = laps });

            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }


        [HttpGet("driver-data")]
        [HttpPost("driver-data")]
        public async Task<IActionResult> GetDriverData([FromQuery] string circuit = "Catalunya", [FromQuery] int year = 2024)
        {
            try
            {
                var cancelled = CheckCancelledRace(circuit, year);
                if (cancelled != null) return cancelled;

                // Check if race exists for this circuit and year
                if (!_cache.RaceExists(circuit, year))
                {
                    return NotFound(new { error = $"No {year} {circuit} Grand Prix" });
                }

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
                    var ApiKeys = await TyreModelClient.CallSessionsDataAsync(circuit, year);
                    if (ApiKeys == null || ApiKeys.Count == 0)
                        return NotFound(new { error = "No session keys found for the specified circuit and year" });
                    keys = ApiKeys;
                    _cache.AddSessions(circuit, year, keys);
                }

                // Check if it's a sprint race (only 1 practice session)
                if (keys.Count == 1)
                {
                    return BadRequest(new { error = $"Insufficient data: {year} {circuit} Grand Prix was a Sprint Race" });
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
                var cancelled = CheckCancelledRace(circuit, year);
                if (cancelled != null) return cancelled;

                // Check if race exists for this circuit and year
                if (!_cache.RaceExists(circuit, year))
                {
                    return NotFound(new { error = $"No {year} {circuit} Grand Prix" });
                }

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

                // Check if it's a sprint race (only 1 practice session)
                if (keys.Count == 1)
                {
                    return BadRequest(new { error = $"Insufficient data: {year} {circuit} Grand Prix was a Sprint Race" });
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
        public async Task<IActionResult> GetMonteCarlo([FromQuery] string circuit = "Catalunya", [FromQuery] int year = 2024, [FromQuery] int numSimulations = 500)
        {
            try
            {
                var cancelled = CheckCancelledRace(circuit, year);
                if (cancelled != null) return cancelled;

                // Check if race exists for this circuit and year
                if (!_cache.RaceExists(circuit, year))
                {
                    return NotFound(new { error = $"No {year} {circuit} Grand Prix" });
                }

                // Check cache for session keys
                var keys = _cache.GetSessionKeys(circuit, year);
                if (keys.Count == 0)
                {
                    keys = await TyreModelClient.CallSessionsDataAsync(circuit, year);
                    if (keys == null || keys.Count == 0)
                        return NotFound(new { error = "No session keys found for the specified circuit and year" });
                    
                    _cache.AddSessions(circuit, year, keys);
                }

                // Check if it's a sprint race (only 1 practice session)
                if (keys.Count == 1)
                {
                    return BadRequest(new { error = $"Insufficient data: {year} {circuit} Grand Prix was a Sprint Race" });
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
                    var apiResults = await TyreModelClient.CallTyreModelAsync(keys);
                    if (apiResults == null)
                        return NotFound(new { error = "No tyre model data found" });
                    
                    results = apiResults;
                    
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

                int laps = _cache.GetLaps(circuit);

                var monteCarloSimulator = new MonteCarloSimulator();
                var monteCarloResult = await monteCarloSimulator.RunSimulation(
                    circuit: circuit,
                    year: year,
                    tyres: tyres,
                    raceLength: laps,
                    pitLoss: 25.0,
                    trafficPenalty: 0.5,
                    numSimulations: numSimulations,
                    cache: _cache
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
                var cancelled = CheckCancelledRace(circuit, year);
                if (cancelled != null) return cancelled;

                // Check if race exists for this circuit and year
                if (!_cache.RaceExists(circuit, year))
                {
                    return NotFound(new { error = $"No {year} {circuit} Grand Prix" });
                }

                // Check cache for top strategies
                var cachedStrategies = _cache.GetTopStrategies(circuit, year);
                if (cachedStrategies.Count > 0)
                {
                    // Convert cached data to expected format
                    var outList = new List<object>();
                    
                    foreach (var strategyData in cachedStrategies)
                    {
                        var stintsData = strategyData["stints"] as List<Dictionary<string, object>>;
                        var windowsData = strategyData["windows"] as List<Dictionary<string, object>>;
                        
                        // Build compounds array from strategy name
                        var compounds = strategyData["strategy_name"].ToString()!.Split("->");
                        
                        // Convert stints to expected format
                        var stints = new List<object>();
                        if (stintsData != null)
                        {
                            foreach (var stint in stintsData)
                            {
                                int start = (int)stint["start"];
                                int end = (int)stint["end"];
                                int length = end - start + 1;
                                
                                stints.Add(new { 
                                    compound = stint["compound"].ToString(), 
                                    length = length 
                                });
                            }
                        }
                        
                        // Convert windows to expected format
                        var windows = new List<object>();
                        if (windowsData != null)
                        {
                            foreach (var window in windowsData)
                            {
                                windows.Add(new { 
                                    min = (int)window["min"], 
                                    max = (int)window["max"],
                                    pitTo = "" // We don't store pitTo in cache, but it's not critical for display
                                });
                            }
                        }
                        
                        // Calculate pit laps from stints
                        var pitLaps = new List<int>();
                        if (stintsData != null && stintsData.Count > 1)
                        {
                            for (int i = 0; i < stintsData.Count - 1; i++)
                            {
                                pitLaps.Add((int)stintsData[i]["end"] + 1);
                            }
                        }
                        
                        outList.Add(new {
                            compounds = compounds,
                            stints = stints,
                            pit_laps = pitLaps,
                            windows = windows,
                            best_time = strategyData["best_time"],
                            time_spread = 0.0 // Not stored in cache
                        });
                    }
                    
                    return Ok(new { success = true, strategies = outList });
                }

                // If not cached, compute strategies
                var keys = _cache.GetSessionKeys(circuit, year);
                if (keys.Count == 0)
                {
                    keys = await TyreModelClient.CallSessionsDataAsync(circuit, year);
                    if (keys == null || keys.Count == 0)
                        return NotFound(new { error = "No session keys found for the specified circuit and year" });
                    
                    _cache.AddSessions(circuit, year, keys);
                }

                // Check if it's a sprint race (only 1 practice session)
                if (keys.Count == 1)
                {
                    return BadRequest(new { error = $"Insufficient data: {year} {circuit} Grand Prix was a Sprint Race" });
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
                    var apiResults = await TyreModelClient.CallTyreModelAsync(keys);
                    if (apiResults == null)
                        return NotFound(new { error = "No tyre model data found" });
                    
                    results = apiResults;
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

                int laps = _cache.GetLaps(circuit);

                var solver = new OptimalStrategy(
                    tyres,
                    laps,
                    pitLoss,
                    fuelPenalty,
                    windowSize,
                    numStrategies
                );

                // Get strategies with windows
                var strategiesWithWindows = solver.FindMultipleStrategies();

                var ordered = strategiesWithWindows.OrderBy(s => s.BestTime).Take(3).ToList();

                var outList2 = new List<object>();
                var strategiesToCache = new List<Dictionary<string, object>>();

                foreach (var s in ordered)
                {
                    // Use center of pit windows for pit laps (most representative)
                    var pitLaps = s.PitWindowRanges.Select(w => (w.MinLap + w.MaxLap) / 2).ToList();

                    // Compute stint lengths based on pit laps
                    var compounds = s.CompoundSequence.Split("->");
                    var stints = new List<object>();
                    var stintsForCache = new List<Dictionary<string, object>>();
                    int currentLap = 1;
                    int stintNumber = 1;
                    
                    for (int i = 0; i < compounds.Length; i++)
                    {
                        int stintLength;
                        int stintEnd;
                        int stintStart = currentLap;
                        
                        if (i < pitLaps.Count)
                        {
                            // Pit on lap pitLaps[i]: complete lap pitLaps[i]-1 on current tyres, 
                            // pit during lap pitLaps[i], start lap pitLaps[i] on new tyres
                            stintLength = pitLaps[i] - currentLap;
                            stintEnd = pitLaps[i] - 1;
                            currentLap = pitLaps[i];
                        }
                        else
                        {
                            // Final stint goes to the end
                            stintLength = laps - currentLap + 1;
                            stintEnd = laps;
                        }

                        stints.Add(new { compound = compounds[i], length = stintLength });
                        stintsForCache.Add(new Dictionary<string, object>
                        {
                            ["stint_number"] = stintNumber,
                            ["compound"] = compounds[i],
                            ["start"] = stintStart,
                            ["end"] = stintEnd
                        });
                        stintNumber++;
                    }

                    // Build pit windows list
                    var windows = s.PitWindowRanges.Select(w => new { 
                        min = w.MinLap, 
                        max = w.MaxLap, 
                        pitTo = w.PitTo.ToString() 
                    }).ToList();
                    
                    var windowsForCache = s.PitWindowRanges.Select(w => new Dictionary<string, object>
                    {
                        ["min"] = w.MinLap,
                        ["max"] = w.MaxLap
                    }).ToList();

                    outList2.Add(new {
                        compounds = compounds,
                        stints = stints,
                        pit_laps = pitLaps,
                        windows = windows,
                        best_time = s.BestTime,
                        time_spread = s.TimeSpread
                    });
                    
                    // Prepare data for caching
                    strategiesToCache.Add(new Dictionary<string, object>
                    {
                        ["strategy_name"] = s.CompoundSequence,
                        ["best_time"] = s.BestTime,
                        ["stints"] = stintsForCache,
                        ["windows"] = windowsForCache
                    });
                }

                // Cache the strategies
                if (strategiesToCache.Count > 0)
                {
                    _cache.AddTopStrategies(circuit, year, strategiesToCache);
                }

                return Ok(new { success = true, strategies = outList2 });
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
                var cancelled = CheckCancelledRace(circuit, year);
                if (cancelled != null) return cancelled;

                // Check if race exists for this circuit and year
                if (!_cache.RaceExists(circuit, year))
                {
                    return NotFound(new { error = $"No {year} {circuit} Grand Prix" });
                }

                // Check cache for qualifying data
                var cachedQualifying = _cache.GetQualifying(circuit, year);
                if (cachedQualifying.Count > 0)
                {
                    // Convert cached data to match DriverDataResult structure
                    var result = new TyreModelClient.DriverDataResult
                    {
                        qualifying = cachedQualifying.Select(q => new TyreModelClient.DriverQualifyingData
                        {
                            position = (int)q["position"],
                            driver_number = (int)q["driver_number"],
                            gap = q["gap"].ToString()
                        }).ToList(),
                        race_pace = null
                    };
                    return Ok(new { success = true, qualifying = result });
                }

                // If not cached, fetch from API
                var keys = _cache.GetSessionKeys(circuit, year);
                if (keys.Count == 0)
                {
                    keys = await TyreModelClient.CallSessionsDataAsync(circuit, year);
                    if (keys == null || keys.Count == 0)
                        return NotFound(new { error = "No session keys found for the specified circuit and year" });
                    
                    _cache.AddSessions(circuit, year, keys);
                }
                    
                var driverData = await TyreModelClient.CallDriverDataAsync(keys);
                
                // Cache the qualifying data
                if (driverData?.qualifying != null && driverData.qualifying.Count > 0)
                {
                    var qualifyingList = driverData.qualifying.Select(q => new Dictionary<string, object>
                    {
                        ["position"] = q.position,
                        ["driver_number"] = q.driver_number,
                        ["gap"] = q.gap ?? "0.000"
                    }).ToList();
                    
                    _cache.AddQualifying(circuit, year, qualifyingList);
                }
                
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
                var cancelled = CheckCancelledRace(circuit, year);
                if (cancelled != null) return cancelled;

                // Check if race exists for this circuit and year
                if (!_cache.RaceExists(circuit, year))
                {
                    return NotFound(new { error = $"No {year} {circuit} Grand Prix" });
                }

                // Check cache for race pace data
                var cachedRacePace = _cache.GetRacePace(circuit, year);
                if (cachedRacePace.Count > 0)
                {
                    // Convert cached data to match DriverDataResult structure
                    var result = new TyreModelClient.DriverDataResult
                    {
                        qualifying = null,
                        race_pace = cachedRacePace.Select(rp => new TyreModelClient.DriverRaceData
                        {
                            position = (int)rp["position"],
                            driver_number = (int)rp["driver_number"],
                            gap_to_fastest = rp["gap_to_fastest"].ToString()
                        }).ToList()
                    };
                    return Ok(new { success = true, racePace = result });
                }

                // If not cached, fetch from API
                var keys = _cache.GetSessionKeys(circuit, year);
                if (keys.Count == 0)
                {
                    keys = await TyreModelClient.CallSessionsDataAsync(circuit, year);
                    if (keys == null || keys.Count == 0)
                        return NotFound(new { error = "No session keys found for the specified circuit and year" });
                    
                    _cache.AddSessions(circuit, year, keys);
                }
                    
                var racePaceData = await TyreModelClient.CallDriverDataAsync(keys);
                
                // Cache the race pace data
                if (racePaceData?.race_pace != null && racePaceData.race_pace.Count > 0)
                {
                    var racePaceList = racePaceData.race_pace.Select(rp => new Dictionary<string, object>
                    {
                        ["position"] = rp.position,
                        ["driver_number"] = rp.driver_number,
                        ["gap_to_fastest"] = rp.gap_to_fastest ?? "0.000"
                    }).ToList();
                    
                    _cache.AddRacePace(circuit, year, racePaceList);
                }
                
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
                var cancelled = CheckCancelledRace(circuit, year);
                if (cancelled != null) return cancelled;

                // Check if race exists for this circuit and year
                if (!_cache.RaceExists(circuit, year))
                {
                    return NotFound(new { error = $"No {year} {circuit} Grand Prix" });
                }

                // Check cache for race simulation data
                var cachedRaceSimulation = _cache.GetRaceSimulation(circuit, year);
                if (cachedRaceSimulation.Count > 0)
                {
                    // Convert cached data to expected format
                    var raceResults = new List<object>();
                    double? firstPlaceTime = null;

                    foreach (var entry in cachedRaceSimulation)
                    {
                        if (firstPlaceTime == null)
                            firstPlaceTime = Convert.ToDouble(entry["totalTime"]);

                        var deltaToFirst = (int)entry["position"] == 1 ? 0.0 : Convert.ToDouble(entry["totalTime"]) - firstPlaceTime.Value;

                        raceResults.Add(new {
                            position = entry["position"],
                            driverNumber = entry["driverNumber"],
                            strategy = entry["strategy"],
                            totalTime = entry["totalTime"],
                            deltaToFirst = deltaToFirst
                        });
                    }

                    return Ok(new {
                        success = true,
                        raceResults = raceResults
                    });
                }

                // If not cached, run simulation
                // Check cache for session keys
                var keys = _cache.GetSessionKeys(circuit, year);
                if (keys.Count == 0)
                {
                    keys = await TyreModelClient.CallSessionsDataAsync(circuit, year);
                    if (keys == null || keys.Count == 0)
                        return NotFound(new { error = "No session keys found for the specified circuit and year" });
                    
                    _cache.AddSessions(circuit, year, keys);
                }
                // Check if it's a sprint race (only 1 practice session)
                if (keys.Count == 1)
                {
                    return BadRequest(new { error = $"Insufficient data: {year} {circuit} Grand Prix was a Sprint Race" });
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
                    var apiResults = await TyreModelClient.CallTyreModelAsync(keys);
                    if (apiResults == null)
                        return NotFound(new { error = "No tyre model data found" });
                    
                    results = apiResults;
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
                int laps = _cache.GetLaps(circuit);
                var raceResult = await RaceSimulator.SimulateRace(circuit, year, tyres, laps, cache: _cache);

                // Build race results data
                var raceResults2 = new List<object>();
                var raceSimDataToCache = new List<Dictionary<string, object>>();
                double? firstPlaceTime2 = null;

                foreach (var driver in raceResult.FinalPositions!.OrderBy(d => d.Position))
                {
                    if (firstPlaceTime2 == null)
                        firstPlaceTime2 = driver.TotalTime;

                    // Get strategy from pit stops
                    var pitStops = raceResult.PitStops!.GetValueOrDefault(driver.DriverNumber, new List<(int, TyreType)>());
                    var strategyParts = new List<string> { driver.StartingTyre.ToString()[0].ToString() };
                    foreach (var pitStop in pitStops)
                    {
                        strategyParts.Add(pitStop.Item2.ToString()[0].ToString());
                    }
                    var strategyString = string.Join("-", strategyParts);

                    var deltaToFirst = driver.Position == 1 ? 0.0 : driver.TotalTime - firstPlaceTime2.Value;

                    raceResults2.Add(new {
                        position = driver.Position,
                        driverNumber = driver.DriverNumber,
                        strategy = strategyString,
                        totalTime = driver.TotalTime,
                        deltaToFirst = deltaToFirst
                    });

                    // Prepare data for caching
                    raceSimDataToCache.Add(new Dictionary<string, object>
                    {
                        ["position"] = driver.Position,
                        ["driverNumber"] = driver.DriverNumber,
                        ["strategy"] = strategyString,
                        ["totalTime"] = driver.TotalTime
                    });
                }

                // Cache the race simulation results
                if (raceSimDataToCache.Count > 0)
                {
                    _cache.AddRaceSimulation(circuit, year, raceSimDataToCache);
                }

                return Ok(new {
                    success = true,
                    raceResults = raceResults2
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
