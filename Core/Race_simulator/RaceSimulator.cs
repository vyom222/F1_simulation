using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using F1_simulation.Core.Strategy_solver;
using F1_simulation.Core.Tyres;
using System.Numerics;
using F1_simulation.Database;

namespace F1_simulation.Core.Race_simulator
{
    public class RaceSimulator
    {
        public static async Task RunQualifyingSimulation(string circuit, int year)
        {
            // Get qualifying data from the API
            var qualiData = await GetQualifyingData(circuit, year, null);
            if (qualiData.HasValue)
            {
                PrintQualifyingResults(qualiData.Value);
            }
            else
            {
                Console.WriteLine("No qualifying data available");
            }
        }

        public static async Task<JsonElement?> GetQualifyingData(string circuit, int year, F1_cache? cache = null)
        {
            try
            {
                // Check cache first if available
                if (cache != null)
                {
                    var cachedQualifying = cache.GetQualifying(circuit, year);
                    var cachedRacePace = cache.GetRacePace(circuit, year);
                    
                    if (cachedQualifying.Count > 0 && cachedRacePace.Count > 0)
                    {
                        // Build JSON structure from cached data
                        var result = new
                        {
                            qualifying = cachedQualifying,
                            race_pace = cachedRacePace
                        };
                        
                        var jsonString = JsonSerializer.Serialize(result);
                        return JsonDocument.Parse(jsonString).RootElement;
                    }
                }

                using var client = new HttpClient();
                client.BaseAddress = new Uri("http://127.0.0.1:8000");

                // First, get session keys
                var sessionRequest = new
                {
                    circuit = circuit,
                    year = year
                };

                var sessionJson = JsonSerializer.Serialize(sessionRequest);
                var sessionContent = new StringContent(sessionJson, Encoding.UTF8, "application/json");

                var sessionResponse = await client.PostAsync("/session_keys", sessionContent);

                if (!sessionResponse.IsSuccessStatusCode)
                {
                    Console.WriteLine($"Session keys API request failed: {sessionResponse.StatusCode}");
                    return null;
                }

                var sessionKeysString = await sessionResponse.Content.ReadAsStringAsync();
                var sessionKeys = JsonSerializer.Deserialize<List<int>>(sessionKeysString);

                if (sessionKeys == null || sessionKeys.Count == 0)
                {
                    Console.WriteLine("No session keys found");
                    return null;
                }

                // Now get driver data using session keys
                var driverDataRequest = new
                {
                    session_keys = sessionKeys
                };

                var json = JsonSerializer.Serialize(driverDataRequest);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await client.PostAsync("/driver_data", content);

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"Driver data API request failed: {response.StatusCode}");
                    return null;
                }

                var responseString = await response.Content.ReadAsStringAsync();
                var jsonElement = JsonDocument.Parse(responseString).RootElement;
                
                // Cache the data if cache is available
                if (cache != null)
                {
                    // Save session keys
                    cache.AddSessions(circuit, year, sessionKeys);
                    
                    // Parse and save qualifying data
                    if (jsonElement.TryGetProperty("qualifying", out var qualifying))
                    {
                        var qualifyingList = new List<Dictionary<string, object>>();
                        foreach (var q in qualifying.EnumerateArray())
                        {
                            qualifyingList.Add(new Dictionary<string, object>
                            {
                                ["position"] = q.GetProperty("position").GetInt32(),
                                ["driver_number"] = q.GetProperty("driver_number").GetInt32(),
                                ["gap"] = q.GetProperty("gap").GetString() ?? "0.000"
                            });
                        }
                        cache.AddQualifying(circuit, year, qualifyingList);
                    }
                    
                    // Parse and save race pace data
                    if (jsonElement.TryGetProperty("race_pace", out var racePace))
                    {
                        var racePaceList = new List<Dictionary<string, object>>();
                        foreach (var rp in racePace.EnumerateArray())
                        {
                            racePaceList.Add(new Dictionary<string, object>
                            {
                                ["position"] = rp.GetProperty("position").GetInt32(),
                                ["driver_number"] = rp.GetProperty("driver_number").GetInt32(),
                                ["gap_to_fastest"] = rp.GetProperty("gap_to_fastest").GetString() ?? "0.000"
                            });
                        }
                        cache.AddRacePace(circuit, year, racePaceList);
                    }
                }
                
                return jsonElement;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting qualifying data: {ex.Message}");
                return null;
            }
        }

        public static void PrintQualifyingResults(JsonElement data)
        {
            try
            {
                if (!data.TryGetProperty("qualifying", out JsonElement qualifying) || qualifying.ValueKind != JsonValueKind.Array)
                {
                    Console.WriteLine("No qualifying results found");
                    return;
                }

                Console.WriteLine("=== QUALIFYING RESULTS ===");
                Console.WriteLine("Pos\tDriver\tTime\t\tGap");
                Console.WriteLine("---\t------\t----\t\t---");

                foreach (var result in qualifying.EnumerateArray())
                {
                    var position = result.GetProperty("position").GetInt32();
                    var driverNumber = result.GetProperty("driver_number").GetInt32();
                    var time = result.GetProperty("time").GetString();
                    var gap = result.GetProperty("gap").GetString();

                    Console.WriteLine($"{position}\t{driverNumber}\t{time}\t{gap}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error printing qualifying results: {ex.Message}");
            }
        }


        public record DriverState
        {
            public int DriverNumber { get; init; }
            public int Position { get; init; }
            public TyreType CurrentTyre { get; init; }
            public TyreType StartingTyre { get; init; }
            public int TyreAge { get; init; }
            public double RacePace { get; init; }
            public double TotalTime { get; init; }
            public bool HasDRS { get; init; }
            public int Lap { get; init; }
            public LinkedListNode<DriverState>? Node { get; init; }
            public TyreUsage UsedTyres { get; init; } 
            public double FuelRemaining { get; init; } // laps of fuel remaining
        }

        public record RaceSimulationResult
        {
            public List<DriverState>? FinalPositions { get; init; }
            public List<List<DriverState>>? LapByLapPositions { get; init; }
            public Dictionary<int, List<(int lap, TyreType pitTo)>>? PitStops { get; init; }
        }


        public static async Task<RaceSimulationResult> SimulateRace(
            string circuit,
            int year,
            IEnumerable<Tyre> tyres,
            int raceLength = 66,
            double pitLoss = 25.0,
            double trafficPenalty = 0.1, 
            F1_cache? cache = null) 
        {
            // Get qualifying and race pace data
            var driverData = await GetQualifyingData(circuit, year, cache);
            if (!driverData.HasValue)
            {
                throw new Exception("Failed to get driver data from API");
            }

            // Parse driver data
            var qualifying = driverData.Value.GetProperty("qualifying");
            var racePace = driverData.Value.GetProperty("race_pace");

            var drivers = new List<DriverState>();
            var racePaceDict = new Dictionary<int, double>();

            var solver = new OptimalStrategy(tyres, raceLength, pitLoss);
            var raceSolver = new RaceSolver(tyres, solver, pitLoss, horizon: 10);

            // Process qualifying data for starting positions
            foreach (var driver in qualifying.EnumerateArray())
            {
                var driverNum = driver.GetProperty("driver_number").GetInt32();
                var position = driver.GetProperty("position").GetInt32();

                 // Try each starting tyre and see which gives the best race strategy
                var strategies = new List<(TyreType tyre, double raceTime)>();

                foreach (var startingTyreOption in new[] { TyreType.Soft, TyreType.Medium, TyreType.Hard })
                {
                    // Create initial race state with this starting tyre
                    var initialState = new RaceState(
                        Tyre: startingTyreOption,
                        TyreAge: 0,
                        LapsRemaining: raceLength,
                        Usage: startingTyreOption switch
                        {
                            TyreType.Soft => TyreUsage.Soft,
                            TyreType.Medium => TyreUsage.Medium,
                            TyreType.Hard => TyreUsage.Hard,
                            _ => TyreUsage.Soft
                        }
                    );

                    var strategy = solver.Solve(initialState);
                    strategies.Add((startingTyreOption, strategy.TotalTime));
                }

                // Choose the tyre with the lowest race time
                var startingTyre = strategies.OrderBy(s => s.raceTime).First().tyre;

                drivers.Add(new DriverState
                {
                    DriverNumber = driverNum,
                    Position = position,
                    CurrentTyre = startingTyre,
                    StartingTyre = startingTyre,
                    TyreAge = 0,
                    RacePace = 0.0, // Will be set from race_pace data
                    TotalTime = 0.0,
                    HasDRS = false,
                    Lap = 0,
                    UsedTyres = ToUsageFlag(startingTyre),
                    FuelRemaining = raceLength // Start with full tank (laps worth of fuel)
                });
            }

            // Process race pace data
            foreach (var driver in racePace.EnumerateArray())
            {
                var driverNum = driver.GetProperty("driver_number").GetInt32();
                var gapStr = driver.GetProperty("gap_to_fastest").GetString();
                var gap = gapStr == "0.000" ? 0.0 : double.Parse(gapStr!.Replace("+", ""));

                racePaceDict[driverNum] = gap;
            }

            // Update drivers with race pace
            drivers = drivers.Select(d => d with { RacePace = racePaceDict.GetValueOrDefault(d.DriverNumber, 5.0) }).ToList();

            // Sort by qualifying position
            drivers = drivers.OrderBy(d => d.Position).ToList();

            // Run race simulation
            return await Task.Run(() => SimulateRaceLapByLap(drivers, solver, raceSolver, tyres.ToDictionary(t => t.Name switch
            {
                "Soft" => TyreType.Soft,
                "Medium" => TyreType.Medium,
                "Hard" => TyreType.Hard,
                _ => throw new ArgumentException($"Unknown tyre name {t.Name}")
            }), raceLength, pitLoss, trafficPenalty));
        }

        private static async Task<RaceSimulationResult> SimulateRaceLapByLap(
            List<DriverState> drivers,
            OptimalStrategy solver,
            RaceSolver raceSolver,
            Dictionary<TyreType, Tyre> tyres,
            int raceLength,
            double pitLoss,
            double trafficPenalty)
        {
            var lapByLapPositions = new List<List<DriverState>>();
            var pitStops = new Dictionary<int, List<(int lap, TyreType pitTo)>>();

            var currentDrivers = new List<DriverState>(drivers);

            for (int lap = 1; lap <= raceLength; lap++)
            {
                // Enable DRS from lap 2 onwards
                currentDrivers = currentDrivers.Select(d => d with { HasDRS = lap >= 2 }).ToList();

                // Convert to linked list for easy navigation
                var driverLinkedList = new LinkedList<DriverState>(currentDrivers);

                // Each driver makes pitting decision and calculates lap time
                var lapTimes = new List<(DriverState driver, double lapTime)>();

                foreach (var driver in currentDrivers)
                {
                    var driverCopy = driver; // Create a mutable copy

                    // Find this driver in the linked list to get position context
                    var node = driverLinkedList.Find(driver);

                    // Calculate traffic penalty - only if they can't overtake
                    double trafficLoss = node != null ? CalculateTrafficPenalty(driverCopy, node, tyres, trafficPenalty) : 0.0;

                    // Get base lap time from tyre + race pace + traffic
                    double baseLapTime = GetLapTime(driver, tyres, driver.RacePace) + trafficLoss;

                    // Apply DRS bonus if applicable
                    if (driverCopy.HasDRS && node != null && IsWithinDRSDistance(node))
                    {
                        baseLapTime -= 0.4; // DRS gives 0.4 second advantage
                    }

                    // Check if driver wants to pit using DP-based expected cost optimization
                    var gapToCarAhead = node?.Previous != null ?
                        driverCopy.TotalTime - node.Previous.Value.TotalTime : 0.0;
                    var gapToCarBehind = node?.Next != null ?
                        node.Next.Value.TotalTime - driverCopy.TotalTime : double.MaxValue;

                    // Get driver ahead's starting tyre for strategy simulation
                    var driverAheadStartTyre = node?.Previous != null ?
                        node.Previous.Value.CurrentTyre : TyreType.Medium;

                    var (pitAction, pitTo, _) = raceSolver.Decide(
                        absoluteLap: lap,
                        raceLength: raceLength,
                        tyre: driverCopy.CurrentTyre,
                        tyreAge: driverCopy.TyreAge,
                        usedTyres: driverCopy.UsedTyres,
                        initialGapToAhead: Math.Max(0, gapToCarAhead),
                        fuelRemaining: driverCopy.FuelRemaining,
                        driverAheadStartTyre: driverAheadStartTyre
                    );

                    if (pitAction == StrategyAction.Pit && pitTo.HasValue)
                    {
                        baseLapTime += pitLoss;

                        if (!pitStops.ContainsKey(driverCopy.DriverNumber))
                            pitStops[driverCopy.DriverNumber] = new();

                        pitStops[driverCopy.DriverNumber].Add((lap, pitTo.Value));

                        driverCopy = driverCopy with
                        {
                            CurrentTyre = pitTo.Value,
                            TyreAge = 0,
                            UsedTyres = driverCopy.UsedTyres | ToUsageFlag(pitTo.Value)
                        };
                    }
                    else
                    {
                        driverCopy = driverCopy with { TyreAge = driverCopy.TyreAge + 1 };
                    }
                    // Update cumulative time and decrease fuel
                    driverCopy = driverCopy with
                    {
                        TotalTime = driverCopy.TotalTime + baseLapTime,
                        Lap = lap,
                        FuelRemaining = Math.Max(0, driverCopy.FuelRemaining - 1) // Decrease fuel by 1 lap worth
                    };

                    lapTimes.Add((driverCopy, baseLapTime));
                }

                // Sort by total time to determine positions and handle overtakes
                currentDrivers = lapTimes
                    .OrderBy(x => x.driver.TotalTime)
                    .Select((x, index) => x.driver with { Position = index + 1 })
                    .ToList();

                lapByLapPositions.Add(new List<DriverState>(currentDrivers));
            }

            return new RaceSimulationResult
            {
                FinalPositions = currentDrivers,
                LapByLapPositions = lapByLapPositions,
                PitStops = pitStops
            };
        }

        private static double CalculateTrafficPenalty(DriverState driver, LinkedListNode<DriverState> driverNode, Dictionary<TyreType, Tyre> tyres, double trafficPenalty)
        {
            // Leader has no traffic
            if (driverNode.Previous == null) return 0.0;

            var carAhead = driverNode.Previous.Value;

            // Calculate lap times without traffic
            double driverLapTimeWithoutTraffic = GetLapTime(driver, tyres, driver.RacePace);
            double carAheadLapTime = GetLapTime(carAhead, tyres, carAhead.RacePace);

            // Check if driver would overtake
            if (driverLapTimeWithoutTraffic < carAheadLapTime)
            {
                return 0.0;
            }

            // Check if within 1 second gap - close racing
            double timeGap = driver.TotalTime - carAhead.TotalTime;
            if (timeGap <= 1.0)
            {
                return trafficPenalty;
            }

            // Reduce penalty for larger gaps but still close racing
            if (timeGap <= 3.0)
            {
                return trafficPenalty * 0.5;
            }

            return 0.0;
        }

        private static double GetLapTime(DriverState driver, Dictionary<TyreType, Tyre> tyres, double racePace)
        {
            if (!tyres.TryGetValue(driver.CurrentTyre, out var tyre))
                return 90.0; // Default lap time

            int safeTyreAge = Math.Min(driver.TyreAge, tyre.LapTimes.Length - 1);
            double baseTime = tyre.LapTimes[safeTyreAge];

            // Apply fuel penalty: 0.05 seconds per lap of fuel remaining
            double fuelPenalty = driver.FuelRemaining * 0.05;

            return baseTime + racePace + fuelPenalty;
        }

        private static bool IsWithinDRSDistance(LinkedListNode<DriverState> driverNode)
        {
            if (driverNode.Previous == null) return false;

            var driver = driverNode.Value;
            var carAhead = driverNode.Previous.Value;

            double timeGap = driver.TotalTime - carAhead.TotalTime;
            return timeGap <= 1.0;
        }

        // Helper methods
        private static TyreUsage ToUsageFlag(TyreType tyre) => tyre switch
        {
            TyreType.Soft => TyreUsage.Soft,
            TyreType.Medium => TyreUsage.Medium,
            TyreType.Hard => TyreUsage.Hard,
            _ => throw new ArgumentOutOfRangeException()
        };
    }
}