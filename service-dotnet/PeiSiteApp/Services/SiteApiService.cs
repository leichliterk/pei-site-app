using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PeiSiteApp.Models;

namespace PeiSiteApp.Services;

public class SiteApiService : IDisposable
{
    private readonly HttpClient _http;
    private readonly ILogger<SiteApiService> _logger;
    private string _apiUrl;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public SiteApiService(string apiUrl, ILogger<SiteApiService> logger)
    {
        _apiUrl = apiUrl;
        _logger = logger;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    }

    public async Task<bool> UpdateSiteNameAsync(int tenantId, string siteId, string siteName)
    {
        var url = $"{_apiUrl}/site/updateSiteName/{tenantId}/{siteId}";
        try
        {
            var response = await _http.PutAsJsonAsync(url, new { siteName });
            if (!response.IsSuccessStatusCode)
                _logger.LogWarning("PUT {Url} returned {Status}", url, (int)response.StatusCode);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PUT {Url} failed", url);
            return false;
        }
    }

    public async Task<bool> UpdateSiteIdAsync(int tenantId, string oldSiteId, string newSiteId)
    {
        var url = $"{_apiUrl}/site/updateSiteId/{tenantId}/{oldSiteId}";
        try
        {
            var response = await _http.PostAsJsonAsync(url, new { newSiteId });
            if (!response.IsSuccessStatusCode)
                _logger.LogWarning("POST {Url} returned {Status}", url, (int)response.StatusCode);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "POST {Url} failed", url);
            return false;
        }
    }

    public async Task<UptimeResponse?> GetUptimeAsync(int tenantId, string siteId, int days = 7)
    {
        var url = $"{_apiUrl}/site/uptime/{tenantId}/{siteId}?days={days}";
        try
        {
            var response = await _http.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("GET {Url} returned {Status}", url, (int)response.StatusCode);
                return null;
            }
            var json = await response.Content.ReadAsStringAsync();
            try
            {
                return JsonSerializer.Deserialize<UptimeResponse>(json, JsonOptions);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to deserialize response from GET {Url}", url);
                return null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET {Url} failed", url);
            return null;
        }
    }

    public void Dispose()
    {
        _http.Dispose();
    }
}
