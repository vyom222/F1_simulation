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

namespace F1_simulation.Core.Race_simulator
{
    public class RaceSimulator
    {
        public static async Task RunQualifyingSimulation(string country, int year)
        {
            // Get qualifying data from the API
            var qualiData = await GetQualifyingData(country, year);
            if (qualiData.HasValue)
            {
                PrintQualifyingResults(qualiData.Value);
            }
            else
            {
                Console.WriteLine("No qualifying data available");
            }
        }

        public static async Task<JsonElement?> GetQualifyingData(string country, int year)
        {
            try
            {
                using var client = new HttpClient();
                client.BaseAddress = new Uri("http://127.0.0.1:8000");

                var requestData = new
                {
                    country = country,
                    year = year
                };

                var json = JsonSerializer.Serialize(requestData);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await client.PostAsync("/driver_data", content);

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"API request failed: {response.StatusCode}");
                    return null;
                }

                var responseString = await response.Content.ReadAsStringAsync();
                return JsonDocument.Parse(responseString).RootElement;
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
            public TyreType StartingTyre { get; init; } // Track the starting tyre
            public int TyreAge { get; init; }
            public double RacePace { get; init; } // Race pace gap to fastest driver (seconds)
            public double TotalTime { get; init; } // Cumulative race time
            public bool HasDRS { get; init; }
            public int Lap { get; init; }
            public LinkedListNode<DriverState>? Node { get; init; } // Reference to linked list node
            public TyreUsage UsedTyres { get; init; } // Track which tyres have been used
            public double FuelRemaining { get; init; } // Fuel in terms of laps worth of fuel
        }

        public record RaceSimulationResult
        {
            public List<DriverState>? FinalPositions { get; init; }
            public List<List<DriverState>>? LapByLapPositions { get; init; }
            public Dictionary<int, List<(int lap, TyreType pitTo)>>? PitStops { get; init; }
        }


        public static async Task<RaceSimulationResult> SimulateRace(
            string country,
            int year,
            IEnumerable<Tyre> tyres,
            int raceLength = 66,
            double pitLoss = 25.0,
            double trafficPenalty = 0.5) // seconds lost when stuck behind another car
        {
            // Get qualifying and race pace data
            var driverData = await GetQualifyingData(country, year);
            if (!driverData.HasValue)
            {
                throw new Exception("Failed to get driver data from API");
            }

            // Parse driver data
            var qualifying = driverData.Value.GetProperty("qualifying");
            var racePace = driverData.Value.GetProperty("race_pace");

            var drivers = new List<DriverState>();
            var racePaceDict = new Dictionary<int, double>();

            // Create starting tyre optimizer
            var startingTyreOptimizer = new StartingTyreOptimizer(tyres, raceLength, pitLoss);

            // Process qualifying data for starting positions
            foreach (var driver in qualifying.EnumerateArray())
            {
                var driverNum = driver.GetProperty("driver_number").GetInt32();
                var position = driver.GetProperty("position").GetInt32();

                var startingTyre = startingTyreOptimizer.FindOptimalStartingTyre(position);

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
                var gap = gapStr == "0.000" ? 0.0 : double.Parse(gapStr.Replace("+", ""));

                racePaceDict[driverNum] = gap;
            }

            // Update drivers with race pace
            drivers = drivers.Select(d => d with { RacePace = racePaceDict.GetValueOrDefault(d.DriverNumber, 5.0) }).ToList();

            // Sort by qualifying position
            drivers = drivers.OrderBy(d => d.Position).ToList();

            // Create strategy solvers
            var solver = new OptimalStrategy(tyres, raceLength, pitLoss);
            var raceSolver = new RaceSolver(tyres, solver, pitLoss, horizon: 10);


            // Starting tyres are already set in driver creation above

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
                Console.WriteLine($"\n=== LAP {lap} ===");

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

                    var pitDecision = raceSolver.Decide(
                        absoluteLap: lap,
                        raceLength: raceLength,
                        tyre: driverCopy.CurrentTyre,
                        tyreAge: driverCopy.TyreAge,
                        usedTyres: driverCopy.UsedTyres,
                        trafficPenaltyThisLap: trafficLoss,
                        fuelRemaining: driverCopy.FuelRemaining
                    );

                    if (pitDecision.action == StrategyAction.Pit && pitDecision.pitTo.HasValue)
                    {
                        baseLapTime += pitLoss;

                        if (!pitStops.ContainsKey(driverCopy.DriverNumber))
                            pitStops[driverCopy.DriverNumber] = new();

                        pitStops[driverCopy.DriverNumber].Add((lap, pitDecision.pitTo.Value));

                        driverCopy = driverCopy with
                        {
                            CurrentTyre = pitDecision.pitTo.Value,
                            TyreAge = 0,
                            UsedTyres = driverCopy.UsedTyres | ToUsageFlag(pitDecision.pitTo.Value)
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

                // Print lap summary
                Console.WriteLine("Positions after lap:");
                foreach (var driver in currentDrivers.Take(5))
                {
                    Console.WriteLine($"P{driver.Position}: Driver {driver.DriverNumber} ({driver.TotalTime:F1}s, {driver.CurrentTyre})");
                }
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

            // Check if driver would overtake naturally (their lap time is faster)
            if (driverLapTimeWithoutTraffic < carAheadLapTime)
            {
                // They would overtake anyway, so no traffic penalty
                return 0.0;
            }

            // Check if within 1 second gap (close racing)
            double timeGap = driver.TotalTime - carAhead.TotalTime;
            if (timeGap <= 1.0)
            {
                return trafficPenalty; // Full traffic penalty
            }

            // Reduce penalty for larger gaps but still close racing
            if (timeGap <= 3.0)
            {
                return trafficPenalty * 0.5; // Half penalty for being somewhat stuck
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

    public class StartingTyreOptimizer
    {
        private readonly OptimalStrategy _strategySolver;

        public StartingTyreOptimizer(IEnumerable<Tyre> tyres, int raceLength, double pitLoss = 25.0)
        {
            _strategySolver = new OptimalStrategy(tyres, raceLength, pitLoss);
        }

        // Find optimal starting tyre based on race length and strategy
        public TyreType FindOptimalStartingTyre(int qualifyingPosition)
        {
            // Try each starting tyre and see which gives the best race strategy
            var strategies = new List<(TyreType tyre, double raceTime)>();

            foreach (var startingTyre in new[] { TyreType.Soft, TyreType.Medium, TyreType.Hard })
            {
                // Create initial race state with this starting tyre
                var initialState = new RaceState(
                    Tyre: startingTyre,
                    TyreAge: 0,
                    LapsRemaining: 70, // Assume standard race length for strategy calculation
                    Usage: startingTyre switch
                    {
                        TyreType.Soft => TyreUsage.Soft,
                        TyreType.Medium => TyreUsage.Medium,
                        TyreType.Hard => TyreUsage.Hard,
                        _ => TyreUsage.Soft
                    }
                );

                var strategy = _strategySolver.Solve(initialState);
                strategies.Add((startingTyre, strategy.TotalTime));
            }

            // Return the tyre with the best (lowest) race time
            return strategies.OrderBy(s => s.raceTime).First().tyre;
        }
    }
}