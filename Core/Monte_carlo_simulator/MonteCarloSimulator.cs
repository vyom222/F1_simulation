using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using F1_simulation.Core.Race_simulator;
using F1_simulation.Core.Strategy_solver;
using F1_simulation.Core.Tyres;

namespace F1_simulation.Core.Monte_carlo_simulator
{
    public class MonteCarloSimulator
    {
        private readonly Random _random;
        private readonly double _gaussianNoiseStdDev;
        private readonly double _safetyCarProbability;
        private readonly int _minSafetyCarLap;
        private readonly int _maxSafetyCarLap;

        public MonteCarloSimulator(
            double gaussianNoiseStdDev = 0.3,
            double safetyCarProbability = 0.3,
            int minSafetyCarLap = 5,
            int maxSafetyCarLap = 60,
            Random? random = null)
        {
            _random = random ?? new Random();
            _gaussianNoiseStdDev = gaussianNoiseStdDev;
            _safetyCarProbability = safetyCarProbability;
            _minSafetyCarLap = minSafetyCarLap;
            _maxSafetyCarLap = maxSafetyCarLap;
        }

        // Runs multiple Monte Carlo simulations and returns average positions
        public async Task<MonteCarloResult> RunSimulation(
            string country,
            int year,
            IEnumerable<Tyre> tyres,
            int raceLength = 66,
            double pitLoss = 25.0,
            double trafficPenalty = 0.5,
            int numSimulations = 1000)
        {
            var positionCounts = new Dictionary<int, Dictionary<int, int>>(); // driver -> position -> count
            var allFinalPositions = new List<List<RaceSimulator.DriverState>>();
            var allRaceInfos = new List<RaceInfo>();

            // Get initial driver data (qualifying and race pace)
            var driverData = await RaceSimulator.GetQualifyingData(country, year);
            if (!driverData.HasValue)
            {
                throw new Exception("Failed to get driver data from API");
            }

            var qualifying = driverData.Value.GetProperty("qualifying");
            var racePace = driverData.Value.GetProperty("race_pace");

            // Build driver list with race pace
            var driverNumbers = new List<int>();
            var racePaceDict = new Dictionary<int, double>();
            var startingPositions = new Dictionary<int, int>();

            foreach (var driver in qualifying.EnumerateArray())
            {
                var driverNum = driver.GetProperty("driver_number").GetInt32();
                var position = driver.GetProperty("position").GetInt32();
                driverNumbers.Add(driverNum);
                startingPositions[driverNum] = position;
            }

            foreach (var driver in racePace.EnumerateArray())
            {
                var driverNum = driver.GetProperty("driver_number").GetInt32();
                var gapStr = driver.GetProperty("gap_to_fastest").GetString();
                var gap = gapStr == "0.000" ? 0.0 : double.Parse(gapStr!.Replace("+", ""));
                racePaceDict[driverNum] = gap;
            }

            // Initialize position tracking
            foreach (var driverNum in driverNumbers)
            {
                positionCounts[driverNum] = new Dictionary<int, int>();
            }

            // Create Monte Carlo solver
            var monteCarloSolver = new MonteCarloSolver(tyres, raceLength, pitLoss, _random);
            var tyresDict = tyres.ToDictionary(t => t.Name switch
            {
                "Soft" => TyreType.Soft,
                "Medium" => TyreType.Medium,
                "Hard" => TyreType.Hard,
                _ => throw new ArgumentException($"Unknown tyre name {t.Name}")
            });

            // Run simulations
            for (int sim = 0; sim < numSimulations; sim++)
            {
                if (sim % 100 == 0)
                {
                    Console.WriteLine($"Running simulation {sim + 1}/{numSimulations}...");
                }

                var (result, raceInfo) = await RunSingleSimulation(
                    driverNumbers,
                    startingPositions,
                    racePaceDict,
                    tyres,
                    tyresDict,
                    raceLength,
                    pitLoss,
                    trafficPenalty,
                    monteCarloSolver);

                raceInfo.RaceNumber = sim + 1;
                allFinalPositions.Add(result.FinalPositions!);
                allRaceInfos.Add(raceInfo);

                // Track positions
                foreach (var driver in result.FinalPositions!)
                {
                    if (!positionCounts[driver.DriverNumber].ContainsKey(driver.Position))
                    {
                        positionCounts[driver.DriverNumber][driver.Position] = 0;
                    }
                    positionCounts[driver.DriverNumber][driver.Position]++;
                }
            }

            // Calculate average positions
            var averagePositions = new Dictionary<int, double>();
            foreach (var driverNum in driverNumbers)
            {
                double totalPosition = 0.0;
                int count = 0;
                foreach (var finalPos in allFinalPositions)
                {
                    var driver = finalPos.FirstOrDefault(d => d.DriverNumber == driverNum);
                    if (driver != null)
                    {
                        totalPosition += driver.Position;
                        count++;
                    }
                }
                averagePositions[driverNum] = count > 0 ? totalPosition / count : 0.0;
            }

            return new MonteCarloResult
            {
                AveragePositions = averagePositions,
                PositionCounts = positionCounts,
                AllSimulations = allFinalPositions,
                AllRaceInfos = allRaceInfos
            };
        }

        private async Task<(RaceSimulator.RaceSimulationResult result, RaceInfo raceInfo)> RunSingleSimulation(
            List<int> driverNumbers,
            Dictionary<int, int> startingPositions,
            Dictionary<int, double> racePaceDict,
            IEnumerable<Tyre> tyres,
            Dictionary<TyreType, Tyre> tyresDict,
            int raceLength,
            double pitLoss,
            double trafficPenalty,
            MonteCarloSolver monteCarloSolver)
        {
            // Initialize drivers with random strategies
            var drivers = new List<RaceSimulator.DriverState>();
            var driverStrategies = new Dictionary<int, OptimalStrategy.StrategyWithWindows>();
            var driverPitPlans = new Dictionary<int, List<(int lap, TyreType pitTo)>>();

            foreach (var driverNum in driverNumbers)
            {
                // Select random strategy for this driver
                var strategy = monteCarloSolver.SelectRandomStrategy();
                driverStrategies[driverNum] = strategy;

                // Randomize pit windows
                var pitPlan = monteCarloSolver.RandomizePitWindows(strategy, raceLength);
                driverPitPlans[driverNum] = pitPlan;

                // Get starting tyre
                var startingTyre = monteCarloSolver.GetStartingTyre(strategy);

                drivers.Add(new RaceSimulator.DriverState
                {
                    DriverNumber = driverNum,
                    Position = startingPositions[driverNum],
                    CurrentTyre = startingTyre,
                    StartingTyre = startingTyre,
                    TyreAge = 0,
                    RacePace = racePaceDict.GetValueOrDefault(driverNum, 5.0),
                    TotalTime = 0.0,
                    HasDRS = false,
                    Lap = 0,
                    UsedTyres = ToUsageFlag(startingTyre),
                    FuelRemaining = raceLength
                });
            }

            // Sort by starting position
            drivers = drivers.OrderBy(d => d.Position).ToList();

            // Determine safety car laps (random)
            var safetyCarLaps = new HashSet<int>();
            if (_random.NextDouble() < _safetyCarProbability)
            {
                int safetyCarLap = _random.Next(_minSafetyCarLap, _maxSafetyCarLap + 1);
                // Safety car typically lasts 2-5 laps
                int safetyCarDuration = _random.Next(2, 6);
                for (int i = 0; i < safetyCarDuration && safetyCarLap + i <= raceLength; i++)
                {
                    safetyCarLaps.Add(safetyCarLap + i);
                }
            }

            // Create strategy solvers for safety car recalculation
            var optimalStrategy = new OptimalStrategy(tyres, raceLength, pitLoss);
            var raceSolver = new RaceSolver(tyres, optimalStrategy, pitLoss, horizon: 10);

            // Run race simulation
            var raceResult = await SimulateRaceLapByLap(
                drivers,
                driverStrategies,
                driverPitPlans,
                tyresDict,
                raceLength,
                pitLoss,
                trafficPenalty,
                safetyCarLaps,
                optimalStrategy,
                raceSolver,
                monteCarloSolver);

            var raceInfo = new RaceInfo
            {
                FinalPositions = raceResult.FinalPositions!,
                Strategies = driverStrategies,
                PitStops = raceResult.PitStops!,
                SafetyCarLaps = safetyCarLaps.OrderBy(l => l).ToList()
            };

            return (raceResult, raceInfo);
        }

        private async Task<RaceSimulator.RaceSimulationResult> SimulateRaceLapByLap(
            List<RaceSimulator.DriverState> drivers,
            Dictionary<int, OptimalStrategy.StrategyWithWindows> driverStrategies,
            Dictionary<int, List<(int lap, TyreType pitTo)>> driverPitPlans,
            Dictionary<TyreType, Tyre> tyres,
            int raceLength,
            double pitLoss,
            double trafficPenalty,
            HashSet<int> safetyCarLaps,
            OptimalStrategy optimalStrategy,
            RaceSolver raceSolver,
            MonteCarloSolver monteCarloSolver)
        {
            var lapByLapPositions = new List<List<RaceSimulator.DriverState>>();
            var pitStops = new Dictionary<int, List<(int lap, TyreType pitTo)>>();

            var currentDrivers = new List<RaceSimulator.DriverState>(drivers);
            bool safetyCarActive = false;

            for (int lap = 1; lap <= raceLength; lap++)
            {
                safetyCarActive = safetyCarLaps.Contains(lap);
                double currentPitLoss = safetyCarActive ? pitLoss / 2.0 : pitLoss;

                // Enable DRS from lap 2 onwards (disabled during safety car)
                bool drsEnabled = lap >= 2 && !safetyCarActive;
                currentDrivers = currentDrivers.Select(d => d with { HasDRS = drsEnabled }).ToList();

                var driverLinkedList = new LinkedList<RaceSimulator.DriverState>(currentDrivers);

                // Each driver makes pitting decision and calculates lap time
                var lapTimes = new List<(RaceSimulator.DriverState driver, double lapTime)>();

                foreach (var driver in currentDrivers)
                {
                    var driverCopy = driver;

                    var node = driverLinkedList.Find(driver);

                    // Calculate traffic penalty (only if not in safety car)
                    double trafficLoss = 0.0;
                    if (!safetyCarActive && node != null)
                    {
                        trafficLoss = CalculateTrafficPenalty(driverCopy, node, tyres, trafficPenalty);
                    }

                    // Get base lap time from tyre + race pace + traffic
                    double baseLapTime = GetLapTime(driver, tyres, driver.RacePace) + trafficLoss;

                    // Add Gaussian noise
                    double noise = SampleGaussian(_random, 0.0, _gaussianNoiseStdDev);
                    baseLapTime += noise;

                    // Add DRS if applicable
                    if (driverCopy.HasDRS && node != null && IsWithinDRSDistance(node))
                    {
                        baseLapTime -= 0.4;
                    }

                    // Safety car slows everyone down
                    if (safetyCarActive)
                    {
                        // All cars run at safety car pace. 50% of normal pace
                        baseLapTime = baseLapTime * 1.5; 
                    }

                    // Determine pit decision
                    bool shouldPit = false;
                    TyreType? pitTo = null;

                    if (safetyCarActive)
                    {
                        // During safety car, recalculate strategy using horizon method
                        var pitDecision = raceSolver.Decide(
                            absoluteLap: lap,
                            raceLength: raceLength,
                            tyre: driverCopy.CurrentTyre,
                            tyreAge: driverCopy.TyreAge,
                            usedTyres: driverCopy.UsedTyres,
                            trafficPenaltyThisLap: 0.0,
                            fuelRemaining: driverCopy.FuelRemaining
                        );

                        if (pitDecision.action == StrategyAction.Pit && pitDecision.pitTo.HasValue)
                        {
                            shouldPit = true;
                            pitTo = pitDecision.pitTo.Value;
                        }
                    }
                    else
                    {
                        // Normal race: follow randomized strategy
                        if (driverPitPlans.TryGetValue(driver.DriverNumber, out var pitPlan))
                        {
                            if (monteCarloSolver.ShouldPitThisLap(lap, pitPlan, driverCopy.CurrentTyre, driverCopy.TyreAge))
                            {
                                shouldPit = true;
                                pitTo = monteCarloSolver.GetNextPitTyre(lap, pitPlan);
                            }
                        }
                    }

                    if (shouldPit && pitTo.HasValue)
                    {
                        baseLapTime += currentPitLoss;

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
                        FuelRemaining = Math.Max(0, driverCopy.FuelRemaining - 1)
                    };

                    lapTimes.Add((driverCopy, baseLapTime));
                }

                // Sort by total time to determine positions
                currentDrivers = lapTimes
                    .OrderBy(x => x.driver.TotalTime)
                    .Select((x, index) => x.driver with { Position = index + 1 })
                    .ToList();

                lapByLapPositions.Add(new List<RaceSimulator.DriverState>(currentDrivers));
            }

            return new RaceSimulator.RaceSimulationResult
            {
                FinalPositions = currentDrivers,
                LapByLapPositions = lapByLapPositions,
                PitStops = pitStops
            };
        }

        private double CalculateTrafficPenalty(RaceSimulator.DriverState driver, LinkedListNode<RaceSimulator.DriverState> driverNode, Dictionary<TyreType, Tyre> tyres, double trafficPenalty)
        {
            if (driverNode.Previous == null) return 0.0;

            var carAhead = driverNode.Previous.Value;

            double driverLapTimeWithoutTraffic = GetLapTime(driver, tyres, driver.RacePace);
            double carAheadLapTime = GetLapTime(carAhead, tyres, carAhead.RacePace);

            if (driverLapTimeWithoutTraffic < carAheadLapTime)
            {
                return 0.0;
            }

            double timeGap = driver.TotalTime - carAhead.TotalTime;
            if (timeGap <= 1.0)
            {
                return trafficPenalty;
            }

            if (timeGap <= 3.0)
            {
                return trafficPenalty * 0.5;
            }

            return 0.0;
        }

        private double GetLapTime(RaceSimulator.DriverState driver, Dictionary<TyreType, Tyre> tyres, double racePace)
        {
            if (!tyres.TryGetValue(driver.CurrentTyre, out var tyre))
                return 90.0;

            int safeTyreAge = Math.Min(driver.TyreAge, tyre.LapTimes.Length - 1);
            double baseTime = tyre.LapTimes[safeTyreAge];

            double fuelPenalty = driver.FuelRemaining * 0.05;

            return baseTime + racePace + fuelPenalty;
        }

        private bool IsWithinDRSDistance(LinkedListNode<RaceSimulator.DriverState> driverNode)
        {
            if (driverNode.Previous == null) return false;

            var driver = driverNode.Value;
            var carAhead = driverNode.Previous.Value;

            double timeGap = driver.TotalTime - carAhead.TotalTime;
            return timeGap <= 1.0;
        }

        private static TyreUsage ToUsageFlag(TyreType tyre) => tyre switch
        {
            TyreType.Soft => TyreUsage.Soft,
            TyreType.Medium => TyreUsage.Medium,
            TyreType.Hard => TyreUsage.Hard,
            _ => throw new ArgumentOutOfRangeException()
        };

        // Box-Muller transform for Gaussian random numbers
        private static double SampleGaussian(Random random, double mean, double stdDev)
        {
            double u1 = 1.0 - random.NextDouble();
            double u2 = 1.0 - random.NextDouble();
            double randStdNormal = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
            return mean + stdDev * randStdNormal;
        }
    }

    public class RaceInfo
    {
        public int RaceNumber { get; set; }
        public List<RaceSimulator.DriverState> FinalPositions { get; set; } = new();
        public Dictionary<int, OptimalStrategy.StrategyWithWindows> Strategies { get; set; } = new();
        public Dictionary<int, List<(int lap, TyreType pitTo)>> PitStops { get; set; } = new();
        public List<int> SafetyCarLaps { get; set; } = new();
    }

    public class MonteCarloResult
    {
        public Dictionary<int, double> AveragePositions { get; set; } = new();
        public Dictionary<int, Dictionary<int, int>> PositionCounts { get; set; } = new();
        public List<List<RaceSimulator.DriverState>> AllSimulations { get; set; } = new();
        public List<RaceInfo> AllRaceInfos { get; set; } = new();

        // Prints the average positions in a formatted table
        public void PrintAveragePositions()
        {
            Console.WriteLine("\n=== MONTE CARLO SIMULATION RESULTS ===");
            Console.WriteLine("Average Final Positions:");
            Console.WriteLine("Driver\tAvg Position");
            Console.WriteLine("------\t------------");

            var sortedByPosition = AveragePositions
                .OrderBy(kvp => kvp.Value)
                .ToList();

            foreach (var (driverNum, avgPos) in sortedByPosition)
            {
                Console.WriteLine($"{driverNum}\t{avgPos:F2}");
            }
        }

        // Prints position distribution for a specific driver
        public void PrintPositionDistribution(int driverNumber)
        {
            if (!PositionCounts.ContainsKey(driverNumber))
            {
                Console.WriteLine($"No data for driver {driverNumber}");
                return;
            }

            Console.WriteLine($"\n=== Position Distribution for Driver {driverNumber} ===");
            var distribution = PositionCounts[driverNumber]
                .OrderBy(kvp => kvp.Key)
                .ToList();

            int totalSimulations = distribution.Sum(kvp => kvp.Value);

            Console.WriteLine("Position\tCount\tPercentage");
            Console.WriteLine("--------\t-----\t----------");
            foreach (var (position, count) in distribution)
            {
                double percentage = (count / (double)totalSimulations) * 100.0;
                Console.WriteLine($"{position}\t\t{count}\t{percentage:F1}%");
            }
        }
    }
}
