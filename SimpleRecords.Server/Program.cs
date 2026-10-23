using Microsoft.EntityFrameworkCore;
using SimpleRecords.Server.Data;
using SimpleRecords.Server.Models;
using SimpleRecords.Server.Services;
using Serilog;
using System.Globalization;
using System.Net;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, logger) =>
{
    var configuration = context.Configuration;

    var minimumLevel = Enum.TryParse<
        Serilog.Events.LogEventLevel>(
            configuration["LoggingSettings:MinimumLevel"],
            true,
            out var parsedLevel)
        ? parsedLevel
        : Serilog.Events.LogEventLevel.Information;

    var filePath =
        configuration["LoggingSettings:FilePath"]
        ?? "logs/simple-records-.log";

    logger
        .MinimumLevel.Is(minimumLevel)
        .Enrich.FromLogContext()
        .WriteTo.Console()
        .WriteTo.File(
            filePath,
            rollingInterval: RollingInterval.Day);
});

/*
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.ReferenceHandler =
        System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
});
*/

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddDbContext<ApplicationDbContext>( opt => opt.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddHttpClient<IpGeolocationService>();
builder.Services.AddScoped<JsonImportService>();

var allowedOrigins =
    builder.Configuration
        .GetSection("Cors:AllowedOrigins")
        .Get<string[]>() ?? Array.Empty<string>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("BlazorClient", policy =>
    {
        policy
            .WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

var app = builder.Build();


// Logger error
app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var exceptionFeature =
            context.Features.Get<
                Microsoft.AspNetCore.Diagnostics.IExceptionHandlerPathFeature>();

        if (exceptionFeature?.Error is not null)
        {
            var logger = context.RequestServices
                .GetRequiredService<ILogger<Program>>();

            logger.LogError(
                exceptionFeature.Error,
                "Neočekávaná chyba pri spracovaní požiadavky {Path}",
                context.Request.Path);
        }

        context.Response.StatusCode =
            StatusCodes.Status500InternalServerError;

        await context.Response.WriteAsJsonAsync(new
        {
            message = "Nastala neočakávaná chyba. Skúste to znova."
        });
    });
});

app.UseSerilogRequestLogging();


// GET positions
app.MapGet("/api/positions", async (ApplicationDbContext dbContext) =>
{
    var positions = await dbContext.Positions
        .OrderBy(p => p.PositionName)
        .ToListAsync();

    return Results.Ok(positions);
});

// POST positions
app.MapPost("/api/positions", async (
    Position position,
    ApplicationDbContext dbContext) =>
{
    if (string.IsNullOrWhiteSpace(position.PositionName))
    {
        return Results.BadRequest("Názov pracovnej pozície je povinný.");
    }

    position.PositionName = position.PositionName.Trim();

    var positionExists = await dbContext.Positions
        .AnyAsync(p => p.PositionName == position.PositionName);

    if (positionExists)
    {
        return Results.Conflict("Táto pracovná pozícia už existuje.");
    }

    dbContext.Positions.Add(position);
    await dbContext.SaveChangesAsync();

    return Results.Created(
        $"/api/positions/{position.PositionID}",
        position);
});

// GET employees
app.MapGet("/api/employees", async (ApplicationDbContext dbContext) =>
{
    var employees = await dbContext.Employees
        .Include(e => e.Position)
        .OrderBy(e => e.Surname)
        .ThenBy(e => e.Name)
        .Select(e => new
        {
            e.EmployeeID,
            e.Name,
            e.Surname,
            e.BirthDate,
            e.PositionID,
            PositionName = e.Position != null
                ? e.Position.PositionName
                : null,
            e.IPaddress,
            e.IPCountryCode
        })
        .ToListAsync();

    return Results.Ok(employees);
});

// GET employees by his number example: /api/employees/1
app.MapGet("/api/employees/{id:int}", async (
    int id,
    ApplicationDbContext dbContext) =>
{
    var employee = await dbContext.Employees
        .Include(e => e.Position)
        .FirstOrDefaultAsync(e => e.EmployeeID == id);

    if (employee is null)
    {
        return Results.NotFound();
    }

    return Results.Ok(employee);
});

// POST employee
app.MapPost("/api/employees", async (
    Employee employee,
    ApplicationDbContext dbContext,
    IpGeolocationService ipGeolocationService) =>
{
    if (string.IsNullOrWhiteSpace(employee.Name))
    {
        return Results.BadRequest("Meno zamestnanca je povinné.");
    }

    if (string.IsNullOrWhiteSpace(employee.Surname))
    {
        return Results.BadRequest("Priezvisko zamestnanca je povinné.");
    }

    if (employee.BirthDate == default)
    {
        return Results.BadRequest("Dátum narodenia je povinný.");
    }

    if (employee.BirthDate > DateTime.Today)
    {
        return Results.BadRequest(
            "Dátum narodenia nemôže byť v budúcnosti.");
    }

    employee.Name = employee.Name.Trim();
    employee.Surname = employee.Surname.Trim();
    employee.IPaddress = employee.IPaddress?.Trim() ?? string.Empty;

    // Employee duplicity
    var employeeExists = await dbContext.Employees.AnyAsync(e =>
        e.Name == employee.Name &&
        e.Surname == employee.Surname &&
        e.BirthDate == employee.BirthDate);

    if (employeeExists)
    {
        return Results.Conflict(
            "Zamestnanec s týmto menom, priezviskom a dátumom narodenia už existuje.");
    }

    // Position check
    if (employee.PositionID.HasValue)
    {
        var positionExists = await dbContext.Positions.AnyAsync(p =>
            p.PositionID == employee.PositionID.Value);

        if (!positionExists)
        {
            return Results.BadRequest(
                "Vybraná pracovná pozícia neexistuje.");
        }
    }

    // Name and surename check
    if (!IsValidPersonName(employee.Name) || !IsValidPersonName(employee.Surname))
    {
        return Results.BadRequest(new
        {
            message = "Meno a priezvisko môžu obsahovať iba písmená, medzery, pomlčky a apostrofy."
        });
    }

    // Ip check
    var ip = employee.IPaddress?.Trim();

    if (!IpAddressValidator.IsValid(ip))
    {
        return Results.BadRequest(new
        {
            message = "Zadaj platnú IP adresu, napríklad 8.8.8.8."
        });
    }

    employee.IPaddress = ip!;
    employee.IPCountryCode =
        await ipGeolocationService.GetCountryCodeAsync(employee.IPaddress);


    // Db save
    dbContext.Employees.Add(employee);
    await dbContext.SaveChangesAsync();

    return Results.Created(
        $"/api/employees/{employee.EmployeeID}",
        new
        {
            employee.EmployeeID,
            employee.Name,
            employee.Surname,
            employee.BirthDate,
            employee.PositionID,
            employee.IPaddress,
            employee.IPCountryCode
        });
});

// Update employee
app.MapPut("/api/employees/{id:int}", async (
    int id,
    Employee updatedEmployee,
    ApplicationDbContext dbContext,
    IpGeolocationService ipGeolocationService) =>
{
    // Nájdeme existujúceho zamestnanca
    var employee = await dbContext.Employees
        .FirstOrDefaultAsync(e => e.EmployeeID == id);

    if (employee is null)
    {
        return Results.NotFound(
            "Zamestnanec s daným ID neexistuje.");
    }

    // Kontrola povinných údajov
    if (string.IsNullOrWhiteSpace(updatedEmployee.Name))
    {
        return Results.BadRequest("Meno zamestnanca je povinné.");
    }

    if (string.IsNullOrWhiteSpace(updatedEmployee.Surname))
    {
        return Results.BadRequest("Priezvisko zamestnanca je povinné.");
    }

    if (updatedEmployee.BirthDate == default)
    {
        return Results.BadRequest("Dátum narodenia je povinný.");
    }

    if (updatedEmployee.BirthDate > DateTime.Today)
    {
        return Results.BadRequest(
            "Dátum narodenia nemôže byť v budúcnosti.");
    }

    // Upravenie textových údajov
    var name = updatedEmployee.Name.Trim();
    var surname = updatedEmployee.Surname.Trim();
    var ipAddress = updatedEmployee.IPaddress?.Trim()
                    ?? string.Empty;

    // Kontrola mena a priezviska
    if (!IsValidPersonName(name) ||
        !IsValidPersonName(surname))
    {
        return Results.BadRequest(new
        {
            message = "Meno a priezvisko môžu obsahovať iba písmená, medzery, pomlčky a apostrofy."
        });
    }

    // IP check
    if (!IpAddressValidator.IsValid(ipAddress))
    {
        return Results.BadRequest(new
        {
            message = "Zadaj platnú IP adresu, napríklad 8.8.8.8."
        });
    }

    // Kontrola duplicity, okrem aktuálneho zamestnanca
    var duplicateExists = await dbContext.Employees.AnyAsync(e =>
        e.EmployeeID != id &&
        e.Name == name &&
        e.Surname == surname &&
        e.BirthDate == updatedEmployee.BirthDate);

    if (duplicateExists)
    {
        return Results.Conflict(
            "Zamestnanec s týmto menom, priezviskom a dátumom narodenia už existuje.");
    }

    // Overenie pracovnej pozície
    if (updatedEmployee.PositionID.HasValue)
    {
        var positionExists = await dbContext.Positions.AnyAsync(p =>
            p.PositionID == updatedEmployee.PositionID.Value);

        if (!positionExists)
        {
            return Results.BadRequest(
                "Vybraná pracovná pozícia neexistuje.");
        }
    }

    // Aktualizácia existujúceho záznamu
    employee.Name = name;
    employee.Surname = surname;
    employee.BirthDate = updatedEmployee.BirthDate;
    employee.PositionID = updatedEmployee.PositionID;
    employee.IPaddress = ipAddress;

    employee.IPCountryCode = await ipGeolocationService.GetCountryCodeAsync(employee.IPaddress);   

    await dbContext.SaveChangesAsync();

    return Results.Ok(new
    {
        employee.EmployeeID,
        employee.Name,
        employee.Surname,
        employee.BirthDate,
        employee.PositionID,
        employee.IPaddress,
        employee.IPCountryCode
    });
});

// Delete employee
app.MapDelete("/api/employees/{id:int}", async (
    int id,
    ApplicationDbContext dbContext) =>
{
    // Nájdeme zamestnanca podľa ID
    var employee = await dbContext.Employees
        .FirstOrDefaultAsync(e => e.EmployeeID == id);

    // Ak zamestnanec neexistuje, vrátime 404
    if (employee is null)
    {
        return Results.NotFound(
            "Zamestnanec s daným ID neexistuje.");
    }

    // Vymažeme zamestnanca z databázy
    dbContext.Employees.Remove(employee);

    await dbContext.SaveChangesAsync();

    return Results.NoContent();
});

// Post Import test positions
app.MapPost("/api/import/positions", async (
    JsonImportService importService) =>
{
    await importService.ImportPositionsAsync();

    return Results.Ok(new
    {
        message = "Import pozícií bol úspešný."
    });
});

// Post Import test employees
app.MapPost("/api/import/employees", async (JsonImportService importService) =>
{
    await importService.ImportEmployeesAsync();

    return Results.Ok(new
    {
        message = "Import zamestnancov bol úspešný."
    });
});

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseCors("BlazorClient");

app.Run();


static bool IsValidPersonName(string? value)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        return false;
    }

    bool hasLetter = false;

    foreach (char character in value.Trim())
    {
        var category = char.GetUnicodeCategory(character);

        if (char.IsLetter(character) ||
            category == UnicodeCategory.NonSpacingMark)
        {
            hasLetter = true;
        }
        else if (character != ' ' &&
                 character != '-' &&
                 character != '\'' &&
                 character != '’')
        {
            return false;
        }
    }

    return hasLetter;
}
