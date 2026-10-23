# SimpleRecords – Evidencia zamestnancov

## Použité technológie

- C# a .NET
- Blazor WebAssembly
- ASP.NET Core Web API
- Entity Framework Core – Code First
- Microsoft SQL Server
- Serilog

## Požiadavky

Pred spustením aplikácie je potrebné mať nainštalované:
- .NET SDK kompatibilné s projektom
- Microsoft SQL Server
- Git (ak sa projekt sťahuje z repozitára)

## Konfigurácia databázy

1. Vytvor databázu `SimpleRecords` na SQL Serveri.
2. Nastav prihlasovacie údaje do databázy pomocou .NET User Secrets:

```powershell
dotnet user-secrets init --project SimpleRecords.Server
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=localhost;Database=SimpleRecords;User ID=SimpleRecordsApp;Password=YOUR_PASSWORD;Encrypt=True;TrustServerCertificate=True;" --project SimpleRecords.Server
```

Namiesto `YOUR_PASSWORD` použi svoje lokálne heslo. Heslo nevkladaj do Git repozitára.

Ak používaš inú konfiguráciu SQL Servera, uprav connection string podľa svojho prostredia.

## Vytvorenie databázy

V hlavnom priečinku riešenia spusti:

```powershell
dotnet ef database update --project SimpleRecords.Server
```

## Spustenie aplikácie

V prvom termináli spusti backend:

```powershell
dotnet run --project SimpleRecords.Server
```

V druhom termináli spusti klientsku aplikáciu:

```powershell
dotnet run --project SimpleRecords.Client
```

Použi adresy vypísané v termináloch. Pri lokálnej konfigurácii projektu sa očakávajú tieto adresy:

- Backend API: `http://localhost:5227`
- Blazor klient: `http://localhost:5181`

Ak sa porty zmenia, skontroluj nastavenia spúšťania a povolené CORS origins v konfigurácii servera.

## Import údajov zo súborov JSON

Importné súbory sa nachádzajú v priečinku `SimpleRecords.Server/ImportFiles`.

Očakávané názvy súborov:

- `postions.json` – pracovné pozície
- `employes.json` – zamestnanci

Najskôr spusti import pracovných pozícií a potom import zamestnancov:

```powershell
Invoke-RestMethod -Method Post -Uri "http://localhost:5227/api/import/positions"
Invoke-RestMethod -Method Post -Uri "http://localhost:5227/api/import/employees"
```

Import kontroluje duplicity a platnosť dátumov a IP adries. Ak sa pracovná pozícia zamestnanca nenájde, zamestnanec môže byť importovaný bez pracovnej pozície.

## Importné súbory

Súbory JSON používané na import nie sú zahrnuté v Git repozitári, pretože môžu obsahovať citlivé alebo osobné údaje.

Ak chceš používať funkciu importu lokálne:

1. Vytvor priečinok `SimpleRecords.Server/ImportFiles/`, ak ešte neexistuje.
2. Vlož doň vlastné súbory `employes.json` a `postions.json`.
3. Uisti sa, že súbory majú správnu štruktúru JSON, ktorú aplikácia očakáva.

Tieto súbory sú vylúčené z Git repozitára pomocou `.gitignore` a nemali by sa nahrávať na GitHub.

**Poznámka:** Importné súbory JSON nie sú súčasťou tohto repozitára. Na lokálne používanie aplikácie si musíš zabezpečiť vlastné súbory.

## Logovanie

Aplikácia používa Serilog na zaznamenávanie udalostí a chýb. Logovacie súbory sa ukladajú do priečinka `SimpleRecords.Server/logs`.

## Bezpečnostné upozornenie

Heslá a iné citlivé údaje neukladaj do zdrojového kódu ani do verejného Git repozitára. Na lokálny vývoj používaj .NET User Secrets.