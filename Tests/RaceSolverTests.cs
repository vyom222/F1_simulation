using F1_simulation.Core.Race_simulator;
using F1_simulation.Core.Strategy_solver;
using F1_simulation.Core.Tyres;
using F1_simulation.Core.Monte_carlo_simulator;
using System.Diagnostics;

namespace Tests;

internal sealed class TestTyre : Tyre
{
	public TestTyre(string name, double slope, double intercept, int totalLaps = 72)
		: base(name, slope, intercept, totalLaps)
	{
	}
}

internal static class TestTyreFactory
{
	public static List<Tyre> CreateDeterministicRaceTyres(int totalLaps = 72)
	{
		return
		[
			new TestTyre("Soft", slope: 1.4, intercept: 75.0, totalLaps),
			new TestTyre("Medium", slope: 0.7, intercept: 76.5, totalLaps),
			new TestTyre("Hard", slope: 0.3, intercept: 78.0, totalLaps)
		];
	}
}


[TestClass]
public sealed class RaceSolverTests
{
    [TestMethod]
    // T.22
    public void Returns_StayOut_On_Final_Lap()
    {
        var tyres = TestTyreFactory.CreateDeterministicRaceTyres();
        var optimal = new OptimalStrategy(tyres, raceLength: 12, pitLossSeconds: 15.0);
        var raceSolver = new RaceSolver(tyres, optimal, pitLoss: 15.0, horizon: 6);

        var decision = raceSolver.Decide(
            absoluteLap: 12,
            raceLength: 12,
            tyre: TyreType.Soft,
            tyreAge: 5,
            usedTyres: TyreUsage.Soft | TyreUsage.Medium,
            initialGapToAhead: 0.8,
            fuelRemaining: 1);

        Assert.AreEqual(StrategyAction.StayOut, decision.action);
        Assert.IsNull(decision.pitTo);
        Assert.IsNull(decision.aheadInfo);
    }

    [TestMethod]
    // T.23
    public void Does_Not_Pit_When_Tyre_Age_Is_Zero()
    {
        var tyres = TestTyreFactory.CreateDeterministicRaceTyres();
        var optimal = new OptimalStrategy(tyres, raceLength: 15, pitLossSeconds: 2.0);
        var raceSolver = new RaceSolver(tyres, optimal, pitLoss: 2.0, horizon: 5);

        var decision = raceSolver.Decide(
            absoluteLap: 3,
            raceLength: 15,
            tyre: TyreType.Soft,
            tyreAge: 0,
            usedTyres: TyreUsage.Soft,
            initialGapToAhead: 0.4,
            fuelRemaining: 13,
            driverAheadStartTyre: TyreType.Medium);

        Assert.AreEqual(StrategyAction.StayOut, decision.action);
    }

    [TestMethod]
    // T.24
    public void Pits_When_Current_Tyre_Is_Severely_Degraded_And_PitLoss_Is_Low()
    {
        var tyres = new List<Tyre>
        {
            new TestTyre("Soft", slope: 12.0, intercept: 70.0, totalLaps: 25),
            new TestTyre("Medium", slope: 3.0, intercept: 73.0, totalLaps: 25),
            new TestTyre("Hard", slope: 1.0, intercept: 76.0, totalLaps: 25)
        };

        var optimal = new OptimalStrategy(tyres, raceLength: 18, pitLossSeconds: 1.0);
        var raceSolver = new RaceSolver(tyres, optimal, pitLoss: 1.0, horizon: 8);

        var decision = raceSolver.Decide(
            absoluteLap: 5,
            raceLength: 18,
            tyre: TyreType.Soft,
            tyreAge: 6,
            usedTyres: TyreUsage.Soft,
            initialGapToAhead: 2.0,
            fuelRemaining: 13,
            driverAheadStartTyre: TyreType.Hard);

        Assert.AreEqual(StrategyAction.Pit, decision.action);
        Assert.IsTrue(decision.pitTo.HasValue);
    }

    [TestMethod]
    // T.21
    public async Task MonteCarlo_500Runs_Under_40Seconds()
    {
        var simulator = new MonteCarloSimulator(maxSafetyCarLap: 50, random: new Random(42));
        var tyres = TestTyreFactory.CreateDeterministicRaceTyres();

        var stopwatch = Stopwatch.StartNew();

        _ = await simulator.RunSimulation(
            circuit: "Catalunya",
            year: 2024,
            tyres: tyres,
            raceLength: 66,
            pitLoss: 25.0,
            trafficPenalty: 0.1,
            numSimulations: 500);

        stopwatch.Stop();

        var elapsedSeconds = stopwatch.Elapsed.TotalSeconds;
        Console.WriteLine($"Monte Carlo runtime for 500 runs: {elapsedSeconds:F2} seconds");

        Assert.IsTrue(elapsedSeconds < 40.0, $"Expected < 40s, took {elapsedSeconds:F2}s");
    }
}



