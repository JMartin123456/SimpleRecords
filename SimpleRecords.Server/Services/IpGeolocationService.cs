
using System.Net;
using System.Text.Json;
using System.Net.Http.Json;

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


    public async Task<Dictionary<string, string?>> GetCountryCodesAsync(
        IEnumerable<string?> ipAddresses)
    {
        var uniqueIps = ipAddresses
            .Where(ip => !string.IsNullOrWhiteSpace(ip))
            .Select(ip => ip!.Trim())
            .Where(ip => IPAddress.TryParse(ip, out _))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var results = new Dictionary<string, string?>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var ip in uniqueIps)
        {
            results[ip] = null;
        }

        // Batch API prijíma najviac 100 adries v jednej dávke.
        var batches = uniqueIps.Chunk(100).ToList();

        for (var i = 0; i < batches.Count; i++)
        {
            // Bezplatné API má limit 15 požiadaviek za minútu.
            // Medzi dávkami necháme rezervu.
            if (i > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(4));
            }

            var batch = batches[i];

            try
            {
                var url =
                    "http://ip-api.com/batch?fields=status,message,countryCode,query";

                using var response = await _httpClient.PostAsJsonAsync(
                    url,
                    batch);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning(
                        "IP geolokácia dávky zlyhala. HTTP status: {StatusCode}.",
                        response.StatusCode);

                    continue;
                }

                await using var stream =
                    await response.Content.ReadAsStreamAsync();

                using var document =
                    await JsonDocument.ParseAsync(stream);

                if (document.RootElement.ValueKind != JsonValueKind.Array)
                {
                    _logger.LogWarning(
                        "IP geolokácia vrátila neočakávaný formát odpovede.");

                    continue;
                }

                foreach (var item in document.RootElement.EnumerateArray())
                {
                    if (!item.TryGetProperty("query", out var queryElement) ||
                        queryElement.ValueKind != JsonValueKind.String)
                    {
                        continue;
                    }

                    var query = queryElement.GetString();

                    if (string.IsNullOrWhiteSpace(query) ||
                        !results.ContainsKey(query))
                    {
                        continue;
                    }

                    if (!item.TryGetProperty("status", out var statusElement) ||
                        statusElement.GetString() != "success")
                    {
                        _logger.LogWarning(
                            "Krajinu sa nepodarilo zistiť pre jednu IP adresu.");

                        continue;
                    }

                    if (item.TryGetProperty(
                            "countryCode", out var countryElement))
                    {
                        var countryCode = countryElement.GetString();

                        if (countryCode is { Length: 2 })
                        {
                            results[query] = countryCode.ToUpperInvariant();
                        }
                    }
                }
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(
                    ex,
                    "Nepodarilo sa kontaktovať IP geolocation API pre dávku.");
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogWarning(
                    ex,
                    "Časový limit pri IP geolokácii dávky vypršal.");
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(
                    ex,
                    "Odpoveď dávkovej IP geolokácie nie je platný JSON.");
            }
        }

        return results;
    }

}
