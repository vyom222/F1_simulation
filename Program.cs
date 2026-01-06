namespace F1_simulation;

using F1_simulation.External;
using F1_simulation.Core.Tyres;

class Program
{
    // Note use of async and Task
    static async Task Main(string[] args)
    {
        Console.WriteLine("Hello, World!");

        if (!await TyreModelClient.IsApiHealthy())
        {
            Console.WriteLine("Tyre API not available"); // uvicorn Python.api:app --reload
            return;
        }


        var results = await TyreModelClient.CallTyreModelAsync("Spain", 2024);

        if (results is null)
        {
            Console.WriteLine("No results returned");
            return;
        }

        var tyres = new List<Tyre>();

        // Exclamation mark because I know not null (from my own API)
        foreach (var r in results)
        {
            var tyre = TyreCreation.Create(r.Compound!, r.Slope, r.Intercept);
            tyres.Add(tyre);
        }

    } 
}

static class TyreCreation
{
    public static Tyre Create(string compound, double slope, double intercept)
    {
        // Switch statement for cleaner code and readability and allows for later extension
        return compound.ToUpperInvariant() switch
        {
            "SOFT" => new SoftTyre(slope, intercept),
            "MEDIUM" => new MediumTyre(slope, intercept),
            "HARD" => new HardTyre(slope, intercept),
            _ => throw new ArgumentException($"Unknown compound: {compound}")
        };
    }
}

// NEXT JOB GET IT TO FIND THE BEST STRATEGY AND OUTPUT IT
// UNIT TESTS
// AFTER FIX THE ANOMALY AND LINES SO THAT THEY ARE ACCURATE
// THEN GET IT TO ALSO OUTPUT THE DRIVER'S LAP TIMES
// GET IT TO SIMULATE THE RACE - look into thte thing where you simulate many different outcomes?
// CREATE FRONTEND - choose your race, compare your strat, simulate the race and quali?