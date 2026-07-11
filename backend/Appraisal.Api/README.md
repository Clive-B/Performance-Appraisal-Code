# NCA Appraisal API

ASP.NET Core backend for hosting the Performance Appraisal Tracker on the NCA intranet with PostgreSQL instead of Firebase.

## What It Replaces

- Firebase Authentication -> API cookie sessions with PostgreSQL-backed users
- Firestore dashboards -> PostgreSQL `dashboards.data` JSONB
- Firebase Storage -> server disk or NCA file-share attachment storage
- Firebase rules -> backend authorization checks

## Required Server Configuration

Update `appsettings.json` or environment variables on the NCA server:

```json
{
  "ConnectionStrings": {
    "Postgres": "Host=localhost;Port=5432;Database=nca_appraisal;Username=nca_appraisal_app;Password=change-me"
  },
  "Bootstrap": {
    "Key": "replace-with-one-time-secret"
  },
  "Storage": {
    "RootPath": "D:\\NCA\\Appraisal\\Attachments"
  }
}
```

Use environment variables for production secrets:

```powershell
$env:ConnectionStrings__Postgres="Host=localhost;Port=5432;Database=nca_appraisal;Username=nca_appraisal_app;Password=..."
$env:Bootstrap__Key="..."
$env:Storage__RootPath="D:\NCA\Appraisal\Attachments"
```

## Database Setup

Run:

```powershell
psql -d nca_appraisal -f ..\..\database\postgresql-schema.sql
```

Then create the first administrator:

```powershell
Invoke-RestMethod -Method Post -Uri http://localhost:5000/api/bootstrap/admin -ContentType 'application/json' -Body '{
  "bootstrapKey": "replace-with-one-time-secret",
  "email": "admin@nca.gov",
  "displayName": "System Administrator",
  "password": "ChangeThisPassword!",
  "division": "All Divisions",
  "unit": "All Units"
}'
```

## Local Run

```powershell
dotnet run --project backend\Appraisal.Api\Appraisal.Api.csproj
```

Default local URL:

```text
http://localhost:5247
```

## Key Endpoints

- `POST /api/auth/login`
- `POST /api/auth/logout`
- `GET /api/auth/me`
- `GET /api/dashboard/me`
- `PUT /api/dashboard/me`
- `GET /api/users`
- `PATCH /api/users/{userId}/assignment`
- `GET /api/units`
- `POST /api/units`
- `POST /api/attachments`
- `GET /api/attachments/{attachmentId}/download`
- `DELETE /api/attachments/{attachmentId}`

## Deployment Package

From the repo root:

```powershell
powershell -ExecutionPolicy Bypass -File tools\publish-intranet.ps1
```

See `docs/nca-server-deployment.md` for IIS setup, production configuration, backup, restore, and smoke testing.
