
using System.Net;
using System.Text.Json;

namespace SimpleRecords.Server.Services;

public class IpGeolocationService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<IpGeolocationService> _logger;

    public IpGeolocationService(
        HttpClient httpClient,
        ILogger<IpGeolocationService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<string?> GetCountryCodeAsync(string? ipAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
        {
            return null;
        }

        if (!IPAddress.TryParse(ipAddress.Trim(), out _))
        {
            _logger.LogWarning(
                "Neplatná IP adresa: {IpAddress}", ipAddress);

            return null;
        }

        try
        {
            var url =
                $"http://ip-api.com/json/{Uri.EscapeDataString(ipAddress.Trim())}?fields=status,message,countryCode";

            using var response = await _httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "IP geolokácia vrátila HTTP status {StatusCode}.",
                    response.StatusCode);

                return null;
            }

            await using var stream =
                await response.Content.ReadAsStreamAsync();

            using var document =
                await JsonDocument.ParseAsync(stream);

            var root = document.RootElement;

            if (!root.TryGetProperty("status", out var status) ||
                status.GetString() != "success")
            {
                _logger.LogWarning(
                    "Externé API nedokázalo určiť krajinu pre IP adresu.");

                return null;
            }

            if (!root.TryGetProperty(
                    "countryCode", out var countryCode))
            {
                return null;
            }

            var result = countryCode.GetString();

            return result is { Length: 2 }
                ? result.ToUpperInvariant()
                : null;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(
                ex, "Nepodarilo sa kontaktovať IP geolocation API.");

            return null;
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(
                ex, "Požiadavka na IP geolocation API vypršala.");

            return null;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(
                ex, "Odpoveď IP geolocation API nie je platný JSON.");

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Neočekávaná chyba pri zisťovaní krajiny IP adresy.");

            return null;
        }

    }
}
