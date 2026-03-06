namespace F1_simulation.External;

using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
public static class TyreModelClient
{
    public class ErrorResponse
    {
        [JsonPropertyName("error")]
        public string? Error { get; set; }
    }

    public class TyreRequest
    {
        [JsonPropertyName("session_keys")]
        public List<int>? Session_keys {get; set;}
    }

    public class SessionRequest
    {
        [JsonPropertyName("circuit")]
        public string? Circuit {get; set;}
        
        [JsonPropertyName("year")]
        public int Year {get; set;}
    }

    public class TyreResult
    {
        public string? Compound { get; set; }
        public double Slope { get; set; }
        public double Intercept { get; set; }
    }

    public class DriverQualifyingData
    {
        public int position { get; set; }
        public int driver_number { get; set; }
        public string? time { get; set; }
        public string? gap { get; set; }
    }

    public class DriverRaceData
    {
        public int position { get; set; }
        public int driver_number { get; set; }
        public string? avg_lap_time { get; set; }
        public string? gap_to_fastest { get; set; }
    }

    public class DriverDataResult
    {
        public List<DriverQualifyingData>? qualifying { get; set; }
        public List<DriverRaceData>? race_pace { get; set; }
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


    public static async Task<List<TyreResult>?> CallTyreModelAsync(List<int> session_keys)
    {
        var client = new HttpClient();

        // Case matters in the request to ensure no 422 error
        var request = new TyreRequest
        {
            Session_keys = session_keys
        };

        string json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await client.PostAsync(
            "http://127.0.0.1:8000/tyre_model",
            content
        );

        if (!response.IsSuccessStatusCode)
        {
            string errorJson = await response.Content.ReadAsStringAsync();
            var errorResponse = JsonSerializer.Deserialize<ErrorResponse>(errorJson);
            throw new Exception(errorResponse?.Error ?? "Unknown error from tyre model API");
        }

        string responseJson = await response.Content.ReadAsStringAsync();

        return JsonSerializer.Deserialize<List<TyreResult>>(responseJson);
    }

    public static async Task<DriverDataResult?> CallDriverDataAsync(List<int> session_keys)
    {
        var client = new HttpClient();

        // Get driver qualifying pace and race pace from practice data
        // Qualifying: fastest lap, Race pace: residuals vs baseline model
        var request = new TyreRequest
        {
            Session_keys = session_keys
        };

        string json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await client.PostAsync(
            "http://127.0.0.1:8000/driver_data",
            content
        );

        if (!response.IsSuccessStatusCode)
        {
            string errorJson = await response.Content.ReadAsStringAsync();
            var errorResponse = JsonSerializer.Deserialize<ErrorResponse>(errorJson);
            throw new Exception(errorResponse?.Error ?? "Unknown error from driver data API");
        }

        string responseJson = await response.Content.ReadAsStringAsync();

        return JsonSerializer.Deserialize<DriverDataResult>(responseJson);
    }
    public static async Task<List<int>?> CallSessionsDataAsync(string circuit, int year)
    {
        var client = new HttpClient();

        var request = new SessionRequest
        {
            Circuit = circuit,
            Year = year
        };

        string json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await client.PostAsync(
            "http://127.0.0.1:8000/session_keys",
            content
        );

        if (!response.IsSuccessStatusCode)
        {
            string errorJson = await response.Content.ReadAsStringAsync();
            var errorResponse = JsonSerializer.Deserialize<ErrorResponse>(errorJson);
            throw new Exception(errorResponse?.Error ?? "Unknown error from session keys API");
        }

        string responseJson = await response.Content.ReadAsStringAsync();

        return JsonSerializer.Deserialize<List<int>>(responseJson);
    }
}