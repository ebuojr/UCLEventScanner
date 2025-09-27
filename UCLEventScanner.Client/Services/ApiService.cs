using UCLEventScanner.Shared.DTOs;

namespace UCLEventScanner.Client.Services;

public interface IApiService
{
    Task<List<EventDto>> GetEventsAsync();
    Task<List<ScannerDto>> GetActiveScannersAsync();
    Task<ScanResponseDto> ProcessScanAsync(ScanRequestDto scanRequest);
}

public class ApiService : IApiService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ApiService> _logger;
    private readonly IConfiguration _configuration;

    public ApiService(HttpClient httpClient, ILogger<ApiService> logger, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _logger = logger;
        _configuration = configuration;
    }

    private string GetApiUrl(string endpoint)
    {
        var baseUrl = _configuration["ApiBaseUrl"] ?? "https://localhost:49675/";
        return $"{baseUrl.TrimEnd('/')}/{endpoint}";
    }

    public async Task<List<EventDto>> GetEventsAsync()
    {
        try
        {
            var url = GetApiUrl("api/events");
            var events = await _httpClient.GetFromJsonAsync<List<EventDto>>(url);
            return events ?? new List<EventDto>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch events");
            throw;
        }
    }

    public async Task<List<ScannerDto>> GetActiveScannersAsync()
    {
        try
        {
            var url = GetApiUrl("api/scanners/active");
            var scanners = await _httpClient.GetFromJsonAsync<List<ScannerDto>>(url);
            return scanners ?? new List<ScannerDto>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch active scanners");
            throw;
        }
    }

    public async Task<ScanResponseDto> ProcessScanAsync(ScanRequestDto scanRequest)
    {
        try
        {
            var url = GetApiUrl("api/scan");
            var response = await _httpClient.PostAsJsonAsync(url, scanRequest);
            response.EnsureSuccessStatusCode();
            
            var result = await response.Content.ReadFromJsonAsync<ScanResponseDto>();
            if (result == null)
            {
                throw new InvalidOperationException("Invalid response from scan API");
            }
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process scan for student {StudentId} at scanner {ScannerId}", 
                scanRequest.StudentId, scanRequest.ScannerId);
            throw;
        }
    }
}