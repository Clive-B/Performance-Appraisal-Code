# NCA Intranet PostgreSQL Migration

## Target Architecture

```text
NCA intranet browser
  -> ASP.NET Core API hosted on IIS or internal server
  -> PostgreSQL database
  -> NCA server disk or secured file share for attachments
```

## Current Migration State

Implemented in this repo:

- `backend/Appraisal.Api` ASP.NET Core API scaffold
- PostgreSQL connection through `Npgsql`
- cookie-based login sessions
- PBKDF2 password hashing using .NET built-ins
- PostgreSQL schema in `database/postgresql-schema.sql`
- personal dashboard save/load endpoints
- user listing and assignment endpoint
- division/unit endpoint
- attachment upload/download/delete endpoint
- audit log table and audit writes for major actions

Still to do:

- rewire the current large HTML file away from Firebase and onto `/api/*`
- optionally replace local password login with Active Directory/LDAP
- add automated API tests once the target PostgreSQL service details are known
- configure IIS, reverse proxy, HTTPS certificate, and Windows service/app pool identity
- migrate any existing Firebase data if production data already exists there

## PostgreSQL Tables

- `users`
- `dashboards`
- `units`
- `attachments`
- `audit_logs`

## Role Model

The backend keeps the current application role names:

- `employee`
- `unitLead`
- `divisionalHead`
- `director`
- `secretariat`
- `deputyDirectorGeneral`
- `systemAdmin`

Director-level access currently includes:

- Divisional Head
- Director
- Secretariat
- Deputy Director General

## Authentication Options

### Option A: Local PostgreSQL Login

This is implemented now. It is easiest to deploy quickly and uses password hashes stored in PostgreSQL.

### Option B: Active Directory / LDAP

Recommended for full NCA intranet rollout if NCA has domain accounts. This should be the second security pass after the API and PostgreSQL data model are accepted.

## Server Setup Checklist

1. Install .NET 8 Hosting Bundle on the NCA server.
2. Install or provision PostgreSQL.
3. Create database `nca_appraisal`.
4. Run `database/postgresql-schema.sql`.
5. Configure `ConnectionStrings__Postgres`.
6. Configure `Storage__RootPath` to a secured server path or file share.
7. Configure `Bootstrap__Key` for first admin creation, then remove/rotate it.
8. Publish `backend/Appraisal.Api`.
9. Host through IIS or the chosen internal server.
10. Rewire and serve the frontend from the API `wwwroot` or a separate intranet web directory.

## Important Security Notes

- Do not leave the sample database password in production.
- Restrict attachment folder permissions to the API app pool/service account.
- Use HTTPS inside the intranet if credentials are entered in the browser.
- Prefer Active Directory before full institutional rollout.
- Keep audit logging enabled for admin actions and role changes.
