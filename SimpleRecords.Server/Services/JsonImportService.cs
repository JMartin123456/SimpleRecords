using Microsoft.EntityFrameworkCore;
using SimpleRecords.Server.Data;
using SimpleRecords.Server.Models;
using System.Text.Json;
using System.Globalization;

namespace SimpleRecords.Server.Services;

public class JsonImportService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<JsonImportService> _logger;
    private readonly IWebHostEnvironment _environment;
    private readonly IpGeolocationService _ipGeolocationService;

    public JsonImportService(
        ApplicationDbContext dbContext,
        ILogger<JsonImportService> logger,
        IWebHostEnvironment environment,
        IpGeolocationService ipGeolocationService)
    {
        _dbContext = dbContext;
        _logger = logger;
        _environment = environment;
        _ipGeolocationService = ipGeolocationService;
    }

    public async Task ImportPositionsAsync()
    {
        var filePath = Path.Combine(
            _environment.ContentRootPath,
            "ImportFiles",
            "postions.json");

        try
        {
            var json = await File.ReadAllTextAsync(filePath);
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object || 
                !document.RootElement.TryGetProperty("positions", out var positions) ||
                positions.ValueKind != JsonValueKind.Array)
            {
                throw new JsonException("JSON súbor neobsahuje platné pole 'positions'.");
            }

            await AddNewPositionsAsync(positions);
            _logger.LogInformation("Import pracovných pozícií bol úspešne dokončený.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Import pracovných pozícií zlyhal.");
            throw;
        }
    }

    private async Task AddNewPositionsAsync(JsonElement positions)
    {
        var existingPositions = await _dbContext.Positions.ToListAsync();
        var alreadyAdded = new List<string>();

        foreach (var item in positions.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                _logger.LogWarning("Pozícia bola preskočená: hodnota nie je text.");
                continue;
            }

            var positionName = item.GetString();
            if (string.IsNullOrWhiteSpace(positionName))
            {
                _logger.LogWarning("Pozícia bola preskočená: názov chýba alebo je prázdny.");
                continue;
            }

            positionName = positionName.Trim();

            var exists = existingPositions.Any(p => string.Equals(p.PositionName, positionName, StringComparison.OrdinalIgnoreCase));
            var alreadyAddedPosition = alreadyAdded.Any(p => string.Equals(p, positionName, StringComparison.OrdinalIgnoreCase));
            if (exists || alreadyAddedPosition)
            {
                _logger.LogInformation("Pozícia {PositionName} už existuje. Nebude pridaná znova.", positionName);
                continue;
            }

            Position position = new Position();
            position.PositionName = positionName;

            _dbContext.Positions.Add(position);
            alreadyAdded.Add(positionName);
        }

        await _dbContext.SaveChangesAsync();
        _logger.LogInformation("Import pracovných pozícií: pridaných {Count} nových pozícií.", alreadyAdded.Count);
    }

    public async Task ImportEmployeesAsync()
    {
        var filePath = Path.Combine(
            _environment.ContentRootPath,
            "ImportFiles",
            "employes.json");

        try
        {
            var json = await File.ReadAllTextAsync(filePath);

            using var document = JsonDocument.Parse(json);

            if (!document.RootElement.TryGetProperty("employees", out var employees) || employees.ValueKind != JsonValueKind.Array)
            {
                throw new JsonException("JSON súbor neobsahuje platné pole 'employees'.");
            }

            var existingEmployees = await _dbContext.Employees.ToListAsync();
            var existingPositions = await _dbContext.Positions.ToListAsync();

            var alreadyAdded = new List<string>();
            var importedCount = 0;
            var skippedCount = 0;
            var recordNumber = 0;

            foreach (var item in employees.EnumerateArray())
            {
                recordNumber++;

                if (item.ValueKind != JsonValueKind.Object)
                {
                    _logger.LogWarning("Záznam č. {RecordNumber} bol preskočený: záznam nie je JSON objekt.", recordNumber);

                    skippedCount++;
                    continue;
                }

                var name = GetStringProperty(item, "Name");
                var surname = GetStringProperty(item, "Surname");
                var birthdate = GetStringProperty(item, "BirthDate");
                var position = GetStringProperty(item, "Position");
                var ipaddress = GetStringProperty(item, "IpAddress");

                name = name?.Trim();
                surname = surname?.Trim();
                ipaddress = ipaddress?.Trim();

                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(surname) || string.IsNullOrWhiteSpace(birthdate) ||
                    string.IsNullOrWhiteSpace(ipaddress))
                {
                    _logger.LogWarning("Záznam č. {RecordNumber} bol preskočený: chýba povinný údaj.", recordNumber);
                    skippedCount++;
                    continue;
                }

                if (!DateTime.TryParseExact(
                    birthdate,
                    "yyyy/MM/dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var parsedBirthDate))
                {
                    _logger.LogWarning("Záznam č. {RecordNumber} bol preskočený: dátum narodenia nemá platný formát.", recordNumber);
                    skippedCount++;
                    continue;
                }

                if (parsedBirthDate > DateTime.Today)
                {
                    _logger.LogWarning("Záznam č. {RecordNumber} bol preskočený: dátum narodenia je v budúcnosti.", recordNumber);
                    skippedCount++;
                    continue;
                }

                if (!IpAddressValidator.IsValid(ipaddress))
                {
                    _logger.LogWarning("Záznam č. {RecordNumber} bol preskočený: IP adresa nemá platný formát.", recordNumber);
                    skippedCount++;
                    continue;
                }

                var employeeKey = $"{name.ToUpperInvariant()}|{surname.ToUpperInvariant()}|{parsedBirthDate:yyyyMMdd}";

                var duplicate = existingEmployees.Any(e =>
                    string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(e.Surname, surname, StringComparison.OrdinalIgnoreCase) &&
                    e.BirthDate.Date == parsedBirthDate.Date);

                var duplicateInFile = alreadyAdded.Contains(employeeKey);

                if (duplicate || duplicateInFile)
                {
                    _logger.LogWarning("Záznam č. {RecordNumber} bol preskočený: duplicitný zamestnanec.", recordNumber);
                    skippedCount++;
                    continue;
                }

                Position? foundPosition = null;
                if (!string.IsNullOrWhiteSpace(position))
                {
                    position = position.Trim();

                    foundPosition = existingPositions.FirstOrDefault(p => string.Equals(p.PositionName, position, 
                        StringComparison.OrdinalIgnoreCase));
                }

                if (foundPosition == null)
                {
                    _logger.LogWarning("Záznam č. {RecordNumber}: pracovná pozícia sa nenašla. Zamestnanec bude importovaný bez pozície.",
                        recordNumber);
                }

                var countryCode = await _ipGeolocationService.GetCountryCodeAsync(ipaddress);

                if (countryCode == null)
                {
                    _logger.LogWarning("Záznam č. {RecordNumber}: krajinu IP adresy sa nepodarilo zistiť.",
                        recordNumber);
                }

                var employee = new Employee();

                employee.Name = name;
                employee.Surname = surname;
                employee.BirthDate = parsedBirthDate;
                employee.PositionID = foundPosition?.PositionID;
                employee.IPaddress = ipaddress;
                employee.IPCountryCode = countryCode;

                _dbContext.Employees.Add(employee);
                alreadyAdded.Add(employeeKey);
                importedCount++;
            }

            await _dbContext.SaveChangesAsync();
            _logger.LogInformation("Import zamestnancov bol dokončený. Pridaných: {ImportedCount}, preskočených: {SkippedCount}.",
                importedCount, skippedCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Import zamestnancov zlyhal pri čítaní alebo spracovaní JSON súboru.");
            throw;
        }
    }

    private string? GetStringProperty(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return property.GetString();
    }

}
