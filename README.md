# NCA Performance Appraisal Progress Dashboard

Performance appraisal dashboard for tracking objectives, action points, KPIs, notes, attachments, reminders, and division/unit reporting.

The original build is a single-page Firebase-backed dashboard. The repo now also contains the start of an NCA intranet backend that replaces Firebase with ASP.NET Core and PostgreSQL.

## Current Deployment State

This repo contains a deployable static HTML build plus Firebase configuration scaffolding. The first hardening pass has:

- removed a browser-injected Kaspersky script from the shared artifact
- removed plaintext security-question collection
- added escaping for core user-rendered objective, task, note, owner, and attachment fields
- removed one duplicate organization-profile patch block
- added Firebase Hosting configuration
- added Firestore and Storage security rules starter policies
- moved new attachment uploads to Firebase Storage while keeping old embedded attachments viewable
- ignored local backup artifacts

## Files

- `Performance Appraisal Tracker.html` - primary browser entry point
- `Performance Appraisal Tracker.txt` - mirrored source copy of the HTML artifact
- `firebase.json` - Firebase Hosting, Firestore, and Storage rules configuration
- `firestore.rules` - security rules for users, dashboards, units, and audit logs
- `storage.rules` - security rules for user-owned attachment uploads
- `backend/Appraisal.Api` - ASP.NET Core API for NCA intranet/PostgreSQL hosting
- `database/postgresql-schema.sql` - PostgreSQL schema for the intranet backend
- `docs/nca-intranet-postgresql-migration.md` - server migration notes and checklist

## NCA Intranet / PostgreSQL Track

The intranet backend has been added and builds successfully with .NET 8. It provides:

- local PostgreSQL-backed login sessions
- user/profile/role storage
- personal dashboard JSON storage in PostgreSQL
- unit management endpoints
- attachment upload/download/delete using server storage
- audit logging for key actions

The existing HTML frontend still needs to be rewired from Firebase SDK calls to `/api/*` calls before the Firebase dependency can be fully removed from the browser experience.

Build the intranet API:

```powershell
$env:DOTNET_CLI_HOME="$PWD\.dotnet"
dotnet build backend\Appraisal.Api\Appraisal.Api.csproj
```

## Firebase Setup

1. Confirm the Firebase project is `nca-performance-dashboard`.
2. In Firebase Authentication, enable email/password sign-in.
3. Add the deployment domain to Firebase Authentication authorized domains.
4. Review `firestore.rules` and `storage.rules` against the final organizational policy before deploying.
5. Install the Firebase CLI if it is not already available:

```powershell
npm install -g firebase-tools
firebase login
```

6. Deploy rules:

```powershell
firebase deploy --only firestore:rules,storage
```

7. Deploy hosting:

```powershell
firebase deploy --only hosting
```

## Security Notes

Client-side role checks are only for UI convenience. Firestore rules must be the source of truth. Before institutional rollout, test the rules with Firebase Emulator Suite and confirm:

- employees can write only their own dashboard
- unit members can read only their unit's visible profiles/dashboards
- director-level roles can read/manage their division: Divisional Head, Director, Secretariat, and Deputy Director General
- system administrators can perform administrative actions
- destructive global profile/dashboard deletion is limited to the primary administrator account
- attachment uploads are stored under the signed-in user's Storage path and are limited to approved document/image types under 10 MB

## Recommended Next Hardening Pass

- split the single HTML file into modules
- add audit logging calls for admin and appraisal actions
- replace destructive admin UI actions with Cloud Functions
- add Firebase Emulator tests for Firestore rules
- migrate any legacy Base64 attachments already saved in Firestore into Firebase Storage

## Current Deployment Decision

This build is suitable for a stronger controlled pilot after Firestore and Storage rules are deployed and tested. It is still not ideal for full institutional rollout because legacy embedded attachments may exist in older dashboard documents, destructive admin actions should ultimately move behind Cloud Functions, and emulator tests should be added before broad deployment.
