# NCA Performance Appraisal Progress Dashboard

Single-page Firebase-backed dashboard for tracking performance objectives, action points, KPIs, notes, attachments, reminders, and division/unit reporting.

## Current Deployment State

This repo contains a deployable static HTML build plus Firebase configuration scaffolding. The first hardening pass has:

- removed a browser-injected Kaspersky script from the shared artifact
- removed plaintext security-question collection
- added escaping for core user-rendered objective, task, note, owner, and attachment fields
- removed one duplicate organization-profile patch block
- added Firebase Hosting configuration
- added Firestore security rules starter policy
- ignored local backup artifacts

## Files

- `Performance Appraisal Tracker.html` - primary browser entry point
- `Performance Appraisal Tracker.txt` - mirrored source copy of the HTML artifact
- `firebase.json` - Firebase Hosting and Firestore rules configuration
- `firestore.rules` - security rules for users, dashboards, units, and audit logs

## Firebase Setup

1. Confirm the Firebase project is `nca-performance-dashboard`.
2. In Firebase Authentication, enable email/password sign-in.
3. Add the deployment domain to Firebase Authentication authorized domains.
4. Review `firestore.rules` against the final organizational policy before deploying.
5. Install the Firebase CLI if it is not already available:

```powershell
npm install -g firebase-tools
firebase login
```

6. Deploy rules:

```powershell
firebase deploy --only firestore:rules
```

7. Deploy hosting:

```powershell
firebase deploy --only hosting
```

## Security Notes

Client-side role checks are only for UI convenience. Firestore rules must be the source of truth. Before institutional rollout, test the rules with Firebase Emulator Suite and confirm:

- employees can write only their own dashboard
- unit members can read only their unit's visible profiles/dashboards
- divisional heads/directors can read their division
- system administrators can perform administrative actions
- destructive operations are denied to ordinary users

## Recommended Next Hardening Pass

- move attachment binary data to Firebase Storage; the current static build blocks files over 350 KB and upload batches over 700 KB to reduce Firestore document-size failures
- split the single HTML file into modules
- add audit logging calls for admin and appraisal actions
- replace destructive admin UI actions with Cloud Functions
- add Firebase Emulator tests for Firestore rules

## Current Deployment Decision

This build is suitable for a controlled pilot after Firestore rules are deployed and tested. It is not yet ideal for full institutional rollout because attachments are still embedded in dashboard documents rather than stored in Firebase Storage, and destructive admin actions still run from the client UI.
