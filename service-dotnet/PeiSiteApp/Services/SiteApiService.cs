using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using PeiSiteApp.Models;

namespace PeiSiteApp.Services;

public class SiteApiService : IDisposable
{
    private readonly HttpClient _http;
    private string _apiUrl;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public SiteApiService(string apiUrl)
    {
        _apiUrl = apiUrl;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    }

    public async Task<bool> UpdateSiteNameAsync(int tenantId, int siteId, string siteName)
    {
        try
        {
            var url = $"{_apiUrl}/site/updateSiteName/{tenantId}/{siteId}";
            var response = await _http.PutAsJsonAsync(url, new { siteName });
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<bool> UpdateSiteIdAsync(int tenantId, int oldSiteId, int newSiteId)
    {
        try
        {
            var url = $"{_apiUrl}/site/updateSiteId/{tenantId}/{oldSiteId}";
            var response = await _http.PostAsJsonAsync(url, new { newSiteId = newSiteId.ToString() });
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<UptimeResponse?> GetUptimeAsync(int tenantId, int siteId, int days = 7)
    {
        try
        {
            var url = $"{_apiUrl}/site/uptime/{tenantId}/{siteId}?days={days}";
            var response = await _http.GetAsync(url);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<UptimeResponse>(json, JsonOptions);
            }
            return null;
        }
        catch { return null; }
    }

    public void Dispose()
    {
        _http.Dispose();
    }
}
