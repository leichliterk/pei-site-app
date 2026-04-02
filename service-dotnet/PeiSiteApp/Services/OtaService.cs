using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace PeiSiteApp.Services;

public class OtaDownloadResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public string? FilePath { get; init; }
}

public class OtaService : IDisposable
{
    private readonly string _apiUrl;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromHours(1) };

    public OtaService(string apiUrl)
    {
        _apiUrl = apiUrl.TrimEnd('/');
    }

    public async Task<OtaDownloadResult> DownloadAsync(
        string releaseId,
        string downloadToken,
        string? expectedSha256,
        IProgress<double> progress,
        CancellationToken ct = default)
    {
        var url = $"{_apiUrl}/ota/download/{releaseId}";
        var tempPath = Path.Combine(Path.GetTempPath(), $"PEI-Setup-{releaseId}.exe");

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", downloadToken);

            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
                return new OtaDownloadResult { Success = false, Error = $"Server returned {(int)response.StatusCode}" };

            // X-SHA256 header takes precedence over the notification payload value
            var serverSha = response.Headers.TryGetValues("X-SHA256", out var vals)
                ? vals.FirstOrDefault()
                : expectedSha256;

            var totalBytes = response.Content.Headers.ContentLength ?? 0;

            using var sha256 = SHA256.Create();
            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var file = File.Create(tempPath);

            var buffer = new byte[65536];
            long downloaded = 0;
            int read;
            while ((read = await stream.ReadAsync(buffer, ct)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read), ct);
                sha256.TransformBlock(buffer, 0, read, null, 0);
                downloaded += read;
                if (totalBytes > 0)
                    progress.Report((double)downloaded / totalBytes * 100.0);
            }
            sha256.TransformFinalBlock(Array.Empty<byte>(), 0, 0);

            if (!string.IsNullOrEmpty(serverSha))
            {
                var actual = Convert.ToHexString(sha256.Hash!).ToLowerInvariant();
                if (actual != serverSha.ToLowerInvariant())
                {
                    File.Delete(tempPath);
                    return new OtaDownloadResult { Success = false, Error = "SHA256 verification failed — file may be corrupted." };
                }
            }

            return new OtaDownloadResult { Success = true, FilePath = tempPath };
        }
        catch (OperationCanceledException)
        {
            TryDelete(tempPath);
            return new OtaDownloadResult { Success = false, Error = "Download cancelled." };
        }
        catch (Exception ex)
        {
            TryDelete(tempPath);
            return new OtaDownloadResult { Success = false, Error = ex.Message };
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    public void Dispose() => _http.Dispose();
}
