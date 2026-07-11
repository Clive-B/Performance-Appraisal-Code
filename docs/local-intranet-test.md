# Local Intranet Test

The connected intranet build is served by the ASP.NET Core API.

## Prerequisite

Install PostgreSQL locally or point `backend/Appraisal.Api/appsettings.json` to an existing PostgreSQL server.

Winget package options found on this machine include:

- `PostgreSQL.PostgreSQL.17`
- `PostgreSQL.PostgreSQL.18`

## Database

Create the database and app user:

```sql
create database nca_appraisal;
create user nca_appraisal_app with password 'change-me';
grant all privileges on database nca_appraisal to nca_appraisal_app;
```

Run the schema:

```powershell
psql -d nca_appraisal -f database\postgresql-schema.sql
```

## Start API and Connected HTML

```powershell
$env:DOTNET_CLI_HOME="$PWD\.dotnet"
dotnet run --project backend\Appraisal.Api\Appraisal.Api.csproj
```

Open the URL shown by `dotnet run`, usually:

```text
http://localhost:5247
```

The dashboard uses NCA intranet API mode by default and routes Firebase-style calls to `/api/*`.

To force the older Firebase browser path for comparison, append `?firebase=1` to the URL.

## First Admin

Create the first administrator before normal login:

```powershell
Invoke-RestMethod -Method Post -Uri http://localhost:5247/api/bootstrap/admin -ContentType 'application/json' -Body '{
  "bootstrapKey": "change-this-bootstrap-key-before-first-run",
  "email": "admin@nca.gov",
  "displayName": "System Administrator",
  "password": "ChangeThisPassword!",
  "division": "All Divisions",
  "unit": "All Units"
}'
```

Then log in through the browser with:

```text
admin@nca.gov
ChangeThisPassword!
```

## Smoke Test

After the API is running and the first admin exists, run:

```powershell
powershell -ExecutionPolicy Bypass -File tools\local-intranet-smoke.ps1
```

The script checks API health, logs in, saves a small dashboard marker, reads it back from PostgreSQL, and fails if the round trip does not match.
