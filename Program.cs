namespace F1_simulation;

using System.Net.Http;
using System.Text;
using System.Text.Json;

class Program
{
    // Note use of async and Task
    static async Task Main(string[] args)
    {
        Console.WriteLine("Hello, World!");

        if (!await IsApiHealthy())
        {
            Console.WriteLine("Tyre API not available");
            return;
        }

        await CallTyreModelAsync();


    }

    public class TyreRequest
    {
        public string? country { get; set; }
        public int year { get; set; }
    }

    public class TyreResult
    {
        public string? Compound { get; set; }
        public double Slope { get; set; }
        public double Intercept { get; set; }
    }
    
    static async Task<bool> IsApiHealthy()
    {
        var client = new HttpClient();

        try
        {
            var response = await client.GetAsync("http://127.0.0.1:8000/health");
            if (!response.IsSuccessStatusCode)
                return false;

            string json = await response.Content.ReadAsStringAsync();
            return json.Contains("ok");
        }
        catch
        {
            return false;
        }
    }


    static async Task CallTyreModelAsync()
    {
        var client = new HttpClient();

        // Case matters in the request to ensure no 422 error
        var request = new TyreRequest
        {
            country = "Spain",
            year = 2024
        };

        string json = JsonSerializer.Serialize(request);

        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await client.PostAsync(
            "http://127.0.0.1:8000/tyre_model",
            content
        );

        response.EnsureSuccessStatusCode();

        string responseJson = await response.Content.ReadAsStringAsync();

        var results = JsonSerializer.Deserialize<List<TyreResult>>(responseJson);

        foreach (var r in results)
        {
            Console.WriteLine($"{r.Compound}: slope={r.Slope}, intercept={r.Intercept}");
        }
    }
}

// NEXT JOB GET IT TO FIND THE BEST STRATEGY AND OUTPUT IT
// AFTER FIX THE ANOMALY AND LINES SO THAT THEY ARE ACCURATE
// THEN GET IT TO ALSO OUTPUT THE DRIVER'S LAP TIMES
// GET IT TO SIMULATE THE RACE - look into thte thing where you simulate many different outcomes?
// CREATE FRONTEND - choose your race, compare your strat, simulate the race and quali?