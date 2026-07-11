# NCA Server Deployment Guide

This guide packages the PostgreSQL-backed intranet version of the Performance Appraisal Tracker and deploys it to an NCA-hosted Windows server.

## 1. Server Prerequisites

- Windows Server with IIS enabled.
- ASP.NET Core Hosting Bundle for .NET 8 installed.
- PostgreSQL 16 or later installed on the server or reachable from it.
- A dedicated folder for the app, for example `C:\inetpub\nca-appraisal`.
- A dedicated folder for attachments, for example `D:\NCA\Appraisal\Attachments`.
- HTTPS certificate bound in IIS before institutional use.

## 2. Database Setup

Create the database and application user using a strong server-side password:

```sql
create database nca_appraisal;
create user nca_appraisal_app with password 'REPLACE_WITH_STRONG_PASSWORD';
grant all privileges on database nca_appraisal to nca_appraisal_app;
```

Apply the schema from the repo:

```powershell
psql -d nca_appraisal -f database\postgresql-schema.sql
```

## 3. Build The Deployment Package

From the repo root:

```powershell
powershell -ExecutionPolicy Bypass -File tools\publish-intranet.ps1
```

The script creates:

- `artifacts\publish\nca-appraisal-api-YYYYMMDD-HHMMSS`
- `artifacts\publish\nca-appraisal-api-YYYYMMDD-HHMMSS.zip`

Copy the zip to the NCA server and extract it into the IIS application folder.

## 4. Production Configuration

Copy this template:

```text
backend\Appraisal.Api\appsettings.Production.example.json
```

On the server, place the filled version beside the deployed `Appraisal.Api.dll` as:

```text
appsettings.Production.json
```

Set these values for NCA:

- `ConnectionStrings:Postgres`: server, database, username, and strong password.
- `Bootstrap:Key`: one-time secret used only to create the first administrator.
- `Storage:RootPath`: server folder for uploaded attachments.
- `Cors:AllowedOrigins`: the final HTTPS origin of the app.
- `AllowedHosts`: the final host name.

For stronger secret handling, configure these as IIS environment variables instead:

```powershell
ConnectionStrings__Postgres=Host=POSTGRES-SERVER;Port=5432;Database=nca_appraisal;Username=nca_appraisal_app;Password=REPLACE_WITH_STRONG_PASSWORD;Include Error Detail=false
Bootstrap__Key=REPLACE_WITH_ONE_TIME_BOOTSTRAP_SECRET
Storage__RootPath=D:\NCA\Appraisal\Attachments
ASPNETCORE_ENVIRONMENT=Production
```

## 5. IIS Setup

1. Create a new IIS site or application pointing to the extracted publish folder.
2. Set the application pool to `No Managed Code`.
3. Set the application pool identity to an account that can read the app folder and read/write the attachment folder.
4. Bind HTTPS to the NCA hostname.
5. Restart the IIS site.

## 6. First Administrator

After the site is live, create the first administrator once:

```powershell
Invoke-RestMethod -Method Post -Uri https://APPRAISAL-HOST/api/bootstrap/admin -ContentType 'application/json' -Body '{
  "bootstrapKey": "REPLACE_WITH_ONE_TIME_BOOTSTRAP_SECRET",
  "email": "admin@nca.gov",
  "displayName": "System Administrator",
  "password": "REPLACE_WITH_TEMPORARY_PASSWORD",
  "division": "All Divisions",
  "unit": "All Units"
}'
```

Then immediately sign in and change the temporary password from the user menu.

## 7. Smoke Testing

Health check:

```powershell
Invoke-RestMethod https://APPRAISAL-HOST/api/health
```

For staging or a clean test database, run:

```powershell
powershell -ExecutionPolicy Bypass -File tools\local-intranet-smoke.ps1 -ApiBase https://APPRAISAL-HOST/api
powershell -ExecutionPolicy Bypass -File tools\role-access-smoke.ps1 -ApiBase https://APPRAISAL-HOST/api
```

The role-access smoke test creates deterministic test users, so run it on staging first.

## 8. Backup

Set `PGPASSWORD` for the database user in the current server session, then run:

```powershell
$env:PGPASSWORD="REPLACE_WITH_DATABASE_PASSWORD"
powershell -ExecutionPolicy Bypass -File tools\backup-intranet.ps1
```

The backup contains a PostgreSQL custom dump, attachment copy, and manifest under `backups\`.

## 9. Restore

Restore a database backup:

```powershell
$env:PGPASSWORD="REPLACE_WITH_DATABASE_PASSWORD"
powershell -ExecutionPolicy Bypass -File tools\restore-intranet.ps1 -BackupPath backups\nca-appraisal-YYYYMMDD-HHMMSS
```

Restore attachments as well:

```powershell
powershell -ExecutionPolicy Bypass -File tools\restore-intranet.ps1 -BackupPath backups\nca-appraisal-YYYYMMDD-HHMMSS -RestoreAttachments
```

## 10. Deployment Checklist

- Production database password is not committed to Git.
- `Bootstrap:Key` is changed from the sample value.
- First admin temporary password is changed after bootstrap.
- IIS uses HTTPS.
- Attachment folder permissions are limited to the app identity and server administrators.
- Backups are scheduled and restore-tested.
- Smoke tests pass on staging before production rollout.
