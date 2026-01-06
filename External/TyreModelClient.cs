namespace F1_simulation.External;

using System.Net.Http;
using System.Text;
using System.Text.Json;

public static class TyreModelClient
{
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

    public static async Task<bool> IsApiHealthy()
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


    public static async Task<List<TyreResult>?> CallTyreModelAsync(string Country = "Spain", int Year = 2024)
    {
        var client = new HttpClient();

        // Case matters in the request to ensure no 422 error
        var request = new TyreRequest
        {
            country = Country,
            year = Year
        };

        string json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await client.PostAsync(
            "http://127.0.0.1:8000/tyre_model",
            content
        );

        response.EnsureSuccessStatusCode();

        string responseJson = await response.Content.ReadAsStringAsync();

        return JsonSerializer.Deserialize<List<TyreResult>>(responseJson);
    }
}