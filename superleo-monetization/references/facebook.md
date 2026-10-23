# Meta/Facebook app setup through Superleo

For Audience Network placements and MAX bidding, use [meta-audience.md](meta-audience.md) instead.
The developer-app workflow below does not establish publisher monetization readiness.

Use this only when the user requests a Meta app or its project/ad-account binding.
It does not launch campaigns, create advertising accounts/Pages, or configure MAX
mediation. Do not call `superleo_fb_onboard_game`: that older workflow can create
resources immediately and does not use this skill's plan/confirmation contract.

## Discover and propose

1. Use the same `access`, `catalog`, `call` commands and `assets/.env` as monetization.
   Require `capabilities.monetization`, an active internal identity (root/dev/user/art/guest) and the
   `facebook_*` operations in the deployed catalog. A personal key binds identity;
   omit top-level email. A body `email`, if supplied for creation, is only the
   app's contact address, not an authentication override.
2. Match the game to `projects` / `project` by verified platform/package/store ID.
   If absent, offer the existing separately confirmed `project_create` workflow.
   Read `facebook_apps`, `facebook_ad_accounts` and, for project configuration,
   `facebook_pages`. These GET operations need no confirmation. Errors or a
   `message`, even with HTTP 200 and `data: []`, mean discovery failed. Do not
   create on that basis. Apps may be cached for five minutes; absence from a
   cached list after creation/timeout does not justify another create.
3. Reuse an app already on the exact project or matching its exact store identity.
   A name alone is a candidate, not proof. Multiple candidates or conflicting IDs
   require user selection. Use the ad account already selected on the project if
   still active/visible; otherwise propose an active returned account (`status: 1`)
   and confirm it. Never substitute another account after an access error. Select
   a Page from returned IDs only when saving complete Facebook project config.
   Do not assume a particular Multicast Page or Instagram identity.
4. Before browser operations call `facebook_browser_health`. Require
   `configured: true`, `ok: true`, `busy: false`. This only checks the worker
   process/network, not Dolphin login, permissions or the FB provision token.
   Superleo holds `FB_PROVISION_URL` / `FB_PROVISION_TOKEN`, Graph credentials and
   business defaults; the remote worker owns its browser/CDP/profile. Do not send
   credentials, CDP addresses or CLI commands from the game. Login/checkpoint,
   unavailable tunnel, busy worker or missing permissions are blockers for the
   operator; do not switch to another browser/account or repeatedly retry.
5. Show the plan in the user's language: game/project, platform, package or bundle
   and store ID, existing/new app, chosen ad account and optional Page, plus which
   project mappings will change. Ask only for missing choices. For iOS,
   `iosBundleId` comes from the game; `storeId` is a numeric App Store ID, often
   the project's `iosAppId` without `id`. Never infer one from the other.

## Operations and order

All examples are arguments to `superleo_monetization` (or JSON files for
`node "<skill>/scripts/superleo.mjs" call request.json`). They contain no keys.
Omitted/false `confirm` is plan-only with `providerValidated: false`, not a remote
dry run. Send `confirm: true` only after approval of the concrete plan.

New Android app:

```json
{
  "operation": "facebook_create_app",
  "body": {
    "projectId": "project-id",
    "name": "Game Name",
    "platform": "android",
    "androidPackageName": "com.example.game",
    "adAccountId": "123456789"
  }
}
```

For iOS replace `platform` with `ios`, omit `androidPackageName`, and include
`iosBundleId: "com.example.game"` and `storeId: "1234567890"`. Both are required.
An optional body `email` sets the app contact. Required `adAccountId` is the
approved account, with or without `act_`; the API normalizes it. `force` and worker
overrides are intentionally rejected. Creation sets the project's App ID and
stores a returned client token; it does not save the selected Page/account mapping.

Save complete project config with the **same** account used during provisioning:

```json
{
  "operation": "facebook_project_config",
  "params": { "id": "project-id" },
  "body": {
    "facebookAppId": "APP_ID",
    "facebookAdAccountId": "123456789",
    "facebookPageId": "PAGE_ID"
  }
}
```

Replace placeholders with returned numeric IDs. This saves project settings; it
does not authorize an account at Meta. If selecting an **existing** app, save its
approved config first (the refresh handler rejects a different saved App ID), then
refresh/authorize it without creating a replacement:

```json
{
  "operation": "facebook_refresh_app",
  "body": {
    "projectId": "project-id",
    "appId": "APP_ID",
    "adAccountId": "123456789"
  }
}
```

Refresh reads the existing app/client token and authorizes the explicit account;
it is a mutation and also requires confirmation. An already-authorized account is
reused. Creation/refresh responses expose `hasClientToken`, not its value; the
token stays saved on the project. Do not dump full project configs into reports.
This workflow does not fetch SDK secrets into the game or install a Facebook SDK.
Local SDK/event integration, if requested, is a separate verified step.

## Verify and recover

- Check MCP `isError`, the wrapper's `success`, native backend `success`, and data.
  HTTP 200 alone is insufficient. Check returned `appId`, expected platform,
  `hasClientToken`, `clientTokenError`, `verificationWarning`, and that
  `adAccountsOnPage` contains the selected bare ID. Missing confirmation of a
  binding/client token is partial/unknown, not a reason to create again.
- `APP_EXISTS` / `existingAppId` means reuse or inspect the existing app.
  `partial: true` / `appId` after a failure means a real app may already exist.
  Preserve that ID, re-read the project and reconcile provider state. Do not use
  `force`, invent a new name, clear the project's App ID, or repeat creation.
- After a timeout, check worker health/lastRun, project App ID and app listing.
  Never send parallel creates or assume absence while an operation is still busy.
  An unknown outcome stops the workflow for operator reconciliation.
- Refresh does not repair a missing platform. If creation stopped during platform
  setup, retain the existing ID and report that platform recovery is not exposed
  by this catalog; do not claim refresh completed it.
- Read back `project` after saving, verifying App ID, account and Page match the
  approved plan. Report created/reused IDs, binding verification, project settings
  and remaining blockers separately. No campaign launch, spending, app publication,
  SDK installation or device-event verification is implied by app creation.
