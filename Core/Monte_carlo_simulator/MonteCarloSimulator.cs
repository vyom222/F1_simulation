using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using F1_simulation.Core.Race_simulator;
using F1_simulation.Core.Strategy_solver;
using F1_simulation.Core.Tyres;
using F1_simulation.Database;

namespace F1_simulation.Core.Monte_carlo_simulator
{
    public class MonteCarloSimulator
    {
        private readonly Random _random;
        private readonly double _gaussianNoiseStdDev;
        private readonly double _safetyCarProbability;
        private readonly int _minSafetyCarLap;
        private readonly int _maxSafetyCarLap;
        private readonly double _firstLapChaosStdDev;
        private readonly double _overtakeProbabilityBase;

        public MonteCarloSimulator(
            double gaussianNoiseStdDev = 0.3,
            double safetyCarProbability = 0.3,
            int minSafetyCarLap = 5,
            int maxSafetyCarLap = 60,
            double firstLapChaosStdDev = 2.0,
            double overtakeProbabilityBase = 0.3,
            Random? random = null)
        {
            _random = random ?? new Random();
            _gaussianNoiseStdDev = gaussianNoiseStdDev;
            _safetyCarProbability = safetyCarProbability;
            _minSafetyCarLap = minSafetyCarLap;
            _maxSafetyCarLap = maxSafetyCarLap;
            _firstLapChaosStdDev = firstLapChaosStdDev;
            _overtakeProbabilityBase = overtakeProbabilityBase;
        }

        // Runs multiple Monte Carlo simulations and returns average positions
        public async Task<MonteCarloResult> RunSimulation(
            string circuit,
            int year,
            IEnumerable<Tyre> tyres,
            int raceLength = 66,
            double pitLoss = 25.0,
            double trafficPenalty = 0.5,
            int numSimulations = 1000,
            F1_cache? cache = null)
        {
            var positionCounts = new Dictionary<int, Dictionary<int, int>>(); // driver -> position -> count
            var allFinalPositions = new List<List<RaceSimulator.DriverState>>();
            var allRaceInfos = new List<RaceInfo>();

            // Get initial driver data (qualifying and race pace)
            var driverData = await RaceSimulator.GetQualifyingData(circuit, year, cache);
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

            var medianPosition = averagePositions.Values
                .OrderBy(x => x)
                .Skip(averagePositions.Count / 2)
                .First();

            return new MonteCarloResult
            {
                AveragePositions = averagePositions,
                PositionCounts = positionCounts,
                AllSimulations = allFinalPositions,
                AllRaceInfos = allRaceInfos,
                MedianPosition = medianPosition
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
            // Initialize drivers with optimal starting tyres
            var drivers = new List<RaceSimulator.DriverState>();
            var driverStrategies = new Dictionary<int, OptimalStrategy.StrategyWithWindows>();

            foreach (var driverNum in driverNumbers)
            {
                // Select random strategy for initial starting tyre only
                var strategy = monteCarloSolver.SelectRandomStrategy();
                driverStrategies[driverNum] = strategy;

                // Get starting tyre from the strategy
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
                tyresDict,
                raceLength,
                pitLoss,
                trafficPenalty,
                safetyCarLaps,
                optimalStrategy,
                raceSolver);

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
            Dictionary<TyreType, Tyre> tyres,
            int raceLength,
            double pitLoss,
            double trafficPenalty,
            HashSet<int> safetyCarLaps,
            OptimalStrategy optimalStrategy,
            RaceSolver raceSolver)
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

                    // Determine pit decision using dynamic optimization
                    // Always use RaceSolver to make optimal decisions based on current race state
                    bool shouldPit = false;
                    TyreType? pitTo = null;

                    var pitDecision = raceSolver.Decide(
                        absoluteLap: lap,
                        raceLength: raceLength,
                        tyre: driverCopy.CurrentTyre,
                        tyreAge: driverCopy.TyreAge,
                        usedTyres: driverCopy.UsedTyres,
                        trafficPenaltyThisLap: trafficLoss,
                        fuelRemaining: driverCopy.FuelRemaining
                    );

                    // Add stochastic element: drivers sometimes pit earlier/later than optimal
                    // This creates strategy variance across simulations
                    if (pitDecision.action == StrategyAction.Pit && pitDecision.pitTo.HasValue)
                    {
                        // 70% chance to follow optimal strategy exactly
                        // 30% chance to deviate by waiting 1-2 more laps
                        double randomChoice = _random.NextDouble();
                        
                        if (randomChoice > 0.7 && driverCopy.TyreAge < 25) // Don't delay if tyres are very old
                        {
                            // Delay pit stop by 1-2 laps
                            int delay = _random.Next(1, 3);

                            // Use driver number as a hash to create consistency within a driver's race
                            int pitThreshold = (driver.DriverNumber + lap) % delay;
                            if (pitThreshold == 0)
                            {
                                shouldPit = true;
                                pitTo = pitDecision.pitTo.Value;
                            }
                        }
                        else
                        {
                            // Follow optimal strategy
                            shouldPit = true;
                            pitTo = pitDecision.pitTo.Value;
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

                // Apply first lap chaos for lap 1
                if (lap == 1)
                {
                    currentDrivers = ApplyFirstLapChaos(lapTimes.Select(lt => lt.driver).ToList());
                }
                else if (safetyCarActive)
                {
                    // During safety car: bunch up all cars by neutralizing gaps
                    currentDrivers = BunchUpCarsUnderSafetyCar(lapTimes);
                }
                else
                {
                    // Apply overtaking logic based on pace differential and randomness
                    currentDrivers = SimulateOvertakes(lapTimes, currentDrivers);
                }

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

        // Bunches up cars under safety car by neutralizing gaps
        private List<RaceSimulator.DriverState> BunchUpCarsUnderSafetyCar(
            List<(RaceSimulator.DriverState driver, double lapTime)> lapTimes)
        {
            // Sort by current total time to maintain race order
            var sortedDrivers = lapTimes.OrderBy(x => x.driver.TotalTime).ToList();
            
            if (sortedDrivers.Count == 0) return new List<RaceSimulator.DriverState>();
            
            // Leader's time is the reference
            double leaderTime = sortedDrivers[0].driver.TotalTime;
            
            var result = new List<RaceSimulator.DriverState>();
            
            for (int i = 0; i < sortedDrivers.Count; i++)
            {
                var driver = sortedDrivers[i].driver;
                
                if (i == 0)
                {
                    // Leader maintains their time
                    result.Add(driver with { Position = 1 });
                }
                else
                {
                    // All other cars are bunched up ~1 second behind each other
                    // Small random variation (0.5-1.5 seconds) to simulate bunching
                    double gapToCarAhead = 0.5 + (_random.NextDouble() * 1.0);
                    double newTotalTime = leaderTime + (i * gapToCarAhead);
                    
                    // Update driver with neutralized gap
                    result.Add(driver with 
                    { 
                        Position = i + 1,
                        TotalTime = newTotalTime
                    });
                }
            }
            
            return result;
        }

        // Simulates first lap chaos with random position changes
        private List<RaceSimulator.DriverState> ApplyFirstLapChaos(List<RaceSimulator.DriverState> drivers)
        {
            var positions = drivers.OrderBy(d => d.TotalTime).ToList();
            
            // Each driver gets a random position delta based on Gaussian distribution
            var positionDeltas = new Dictionary<int, int>();
            
            foreach (var driver in positions)
            {
                // Sample from Gaussian - larger std dev for first lap
                double delta = SampleGaussian(_random, 0.0, _firstLapChaosStdDev);
                positionDeltas[driver.DriverNumber] = (int)Math.Round(delta);
            }
            
            // Apply deltas while keeping positions in valid range
            var newPositions = new List<(RaceSimulator.DriverState driver, int targetPosition)>();
            
            for (int i = 0; i < positions.Count; i++)
            {
                var driver = positions[i];
                int currentPos = i + 1;
                int delta = positionDeltas[driver.DriverNumber];
                int targetPos = Math.Clamp(currentPos + delta, 1, positions.Count);
                
                newPositions.Add((driver, targetPos));
            }
            
            // Sort by target position and resolve conflicts
            var sortedByTarget = newPositions.OrderBy(x => x.targetPosition).ThenBy(x => x.driver.TotalTime).ToList();
            
            var result = new List<RaceSimulator.DriverState>();
            for (int i = 0; i < sortedByTarget.Count; i++)
            {
                var driver = sortedByTarget[i].driver;
                int positionsGained = driver.Position - (i + 1);
                
                // Adjust time slightly based on position change to maintain consistency
                // (gaining positions = slightly better lap, losing = slightly worse)
                double timeAdjustment = positionsGained * -0.1;
                
                result.Add(driver with 
                { 
                    Position = i + 1,
                    TotalTime = driver.TotalTime + timeAdjustment
                });
            }
            
            return result;
        }

        private List<RaceSimulator.DriverState> SimulateOvertakes(
            List<(RaceSimulator.DriverState driver, double lapTime)> lapTimes,
            List<RaceSimulator.DriverState> previousPositions)
        {
            var driversByTime = lapTimes.OrderBy(x => x.driver.TotalTime).ToList();
            var result = new List<RaceSimulator.DriverState>();
            
            // Convert to linked list for easy position swapping
            var positions = new LinkedList<RaceSimulator.DriverState>();
            foreach (var (driver, _) in driversByTime)
            {
                positions.AddLast(driver);
            }
            
            // Process each adjacent pair for potential overtakes
            var current = positions.First;
            while (current != null && current.Next != null)
            {
                var ahead = current.Value;
                var behind = current.Next.Value;
                
                // Find their lap times
                double aheadLapTime = lapTimes.First(x => x.driver.DriverNumber == ahead.DriverNumber).lapTime;
                double behindLapTime = lapTimes.First(x => x.driver.DriverNumber == behind.DriverNumber).lapTime;
                
                // Calculate pace differential (how much faster the car behind is)
                double paceDifferential = aheadLapTime - behindLapTime;
                
                // Calculate overtake probability based on pace differential
                double overtakeProbability = 0.0;
                
                if (paceDifferential > 0.3)  // Behind car is significantly faster
                {
                    // Higher pace differential = higher overtake chance
                    overtakeProbability = Math.Min(0.8, _overtakeProbabilityBase + (paceDifferential * 0.1));
                }
                
                // Random element - sometimes overtakes happen, sometimes they don't
                if (paceDifferential > 0.1 && _random.NextDouble() < overtakeProbability)
                {
                    // Swap positions
                    var temp = current.Value;
                    current.Value = current.Next.Value;
                    current.Next.Value = temp;
                    
                    // Add small time penalty to the overtaken car (defending)
                    current.Next.Value = current.Next.Value with 
                    { 
                        TotalTime = current.Next.Value.TotalTime + 0.2 
                    };
                }
                
                current = current.Next;
            }
            
            // Convert back to list with updated positions
            int pos = 1;
            foreach (var driver in positions)
            {
                result.Add(driver with { Position = pos });
                pos++;
            }
            
            return result;
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

        public double MedianPosition {get; set;} = new();

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
