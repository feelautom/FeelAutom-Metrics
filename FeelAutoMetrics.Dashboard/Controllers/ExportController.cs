using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Headers;

namespace FeelAutoMetrics.Dashboard.Controllers;

[Authorize]
[Route("api/proxy/export")]
public class ExportController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;

    public ExportController(IConfiguration configuration, IHttpClientFactory httpClientFactory)
    {
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
    }

    [HttpGet("{type}")]
    public async Task<IActionResult> Export(string type, [FromQuery] string format, [FromQuery] string? domain = null, [FromQuery] string? from = null, [FromQuery] string? to = null)
    {
        var ingestorBaseUrl = _configuration["IngestorBaseUrl"] ?? "http://localhost:5001";
        var ingestorApiKey = _configuration["IngestorApiKey"] ?? "";

        var url = $"{ingestorBaseUrl}/api/export/{type}?format={format}";
        if (!string.IsNullOrEmpty(domain)) url += $"&domain={domain}";
        if (!string.IsNullOrEmpty(from)) url += $"&from={from}";
        if (!string.IsNullOrEmpty(to)) url += $"&to={to}";
        if (!string.IsNullOrEmpty(ingestorApiKey)) url += $"&apikey={ingestorApiKey}";

        using var client = _httpClientFactory.CreateClient();
        var response = await client.GetAsync(url);

        if (!response.IsSuccessStatusCode)
            return StatusCode((int)response.StatusCode, "Erreur lors de la récupération des données de l'Ingestor.");

        var content = await response.Content.ReadAsByteArrayAsync();
        var contentType = response.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";
        
        var fileName = $"{type}_{DateTime.Now:yyyyMMdd_HHmm}.{format}";
        return File(content, contentType, fileName);
    }
}
