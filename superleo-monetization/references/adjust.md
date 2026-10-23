# Adjust attribution apps

Use only when Adjust setup is requested. Connect through the existing
`superleo_monetization` tool/client and skill-local config (see `connection.md`); require the same
internal root/dev/user/art/guest role and `capabilities.monetization`. This workflow creates an
attribution app, not advertisements, revenue products or tracking links.
`ADJUST_API_TOKEN` stays on the Superleo server. Listing apps successfully does
not prove that the token can create apps. Do not ask for the provider token in chat.

## Discover and choose

1. Resolve the exact game/project from the repository and `projects` / `project`.
   Android `storeId` and `bundleId` must both equal its package name. For iOS,
   `storeId` is the numeric App Store ID (an `id` prefix is normalized), while
   `bundleId` is the reverse-DNS bundle identifier. Never substitute one for the
   other. Ask if the store ID or platform is unknown.
2. Read `adjust_settings` and `adjust_apps`. The settings expose
   `useAutomationApi` and `templateAppToken`, not API credentials. If automation
   is disabled in the panel, ask the operator to enable it; do not change Admin
   settings or silently switch accounts. A permission/configuration/read error
   is not an empty account.
3. `adjust_find_app` takes query `{platform, store_id}`; `adjust_app` takes params
   `{token}`. The finder can return `state: "not_found"` for a real provider 404;
   this is not an error, but is not enough to justify creation alone. Also inspect
   the list and the project's existing `adjustTrackerToken`: the finder may not
   find legacy/manual apps. `adjust_app` uses the settings endpoint for those.
4. Match by exact platform/store identity, not just name. Multi-platform apps
   expose `android_package`, `ios_app_id`, `ios_bundle_id`; a default `platform`
   of Android does not mean the app has no iOS configuration. Inspect candidate
   details and resolve conflicts with the user. Reuse an exact match.
5. Propose the app name, platform/IDs, create versus reuse, template choice and
   project binding. Creation without template migration is NOT supported by this
   integration: `skipTemplate: true` is rejected before creation. Do not propose
   it as minimal mode or silently change it to false. `skipTemplate: false` copies events,
   partners, callbacks, attribution settings and S2S from the server's template;
   it does not copy trackers. Get approval for that copying and the specific
   template before creating. Missing/unclear template configuration is a blocker,
   not permission to silently create without it. If copying is not authorized,
   reuse a verified existing app or stop and explain the limitation.

   Adjust's [App endpoint](https://dev.adjust.com/en/api/app-automation-api/app-endpoint/)
   uses the account template when its token is omitted, and migrates ALL sections
   when `sections` is empty. Neither is a way to disable migration.

## Create and observe

Example arguments (or a JSON file for `scripts/superleo.mjs call request.json`):

```json
{
  "operation": "adjust_create_app",
  "body": {
    "name": "Game Name",
    "platform": "android",
    "storeId": "com.example.game",
    "bundleId": "com.example.game",
    "skipTemplate": false
  }
}
```

Without `confirm: true` this validates only the local plan and permissions;
`providerValidated: false` means no provider request was sent. Send confirmation
only for the approved plan. The API rejects extra credentials, `channelSetup`,
template overrides, `force` and arbitrary settings. Partner updates, link
creation, job execution, deletion and Admin settings are not exposed here.
Standard event creation has a separate, project-scoped workflow in
[adjust-events.md](adjust-events.md); it does not re-run template migration.

On execution the API re-reads the app list before creating. `APP_EXISTS` returns
`existingApps`: inspect/reuse those tokens, do not rename or retry to bypass it.
`APP_DISCOVERY_FAILED` means creation was not started. This guard is not a
distributed exactly-once guarantee: never run parallel creates for one game.

Results live under the MCP wrapper's `data`, with sanitized app fields in its
`data` and explicit `state` / `completed`:

- `pending` / `running`: request accepted or still processing, not completed.
  Provider `new` maps to `pending`, `opened` maps to `running`.
- `unverified`: creation returned an app token without a ticket; inspect the app.
- `completed`: the specific ticket completed; still verify the resulting app.
- `failed` / `unknown`: do not claim setup succeeded or automatically recreate.

Preserve both `adjust_app_token` and `ticket_token` if returned. Use
`adjust_ticket_status`, params `{ticket: ticket_token}`, to poll that ticket at
five-second intervals for up to five minutes. Stop on completion, failure, an
unknown status or timeout. Pending after the limit remains pending; report its
ticket so it can be checked later. Read calls may be repeated; creation must not
be retried blindly. In-progress tickets can already contain `hasFailures: true`;
keep polling until a terminal state or the time limit. Completed tickets with
nested failures are marked failed. `data.sections` gives per-section success/failure
counts and safe `failureCodes`, not raw partner config or credentials. A failed
migration can leave an app and copied settings in place; inspect before recovery.
User approval to keep partial settings permits evaluation, not another migration
or reporting the failed ticket as completed. Report remaining failures separately.

After a create timeout, re-read the project and app list/finder/details, and the
known ticket if available. If the outcome remains ambiguous, stop for operator
reconciliation. Do not create another app or delete one as rollback.

## Bind and verify

Read the resulting app with `adjust_app` and verify its exact platform/store and
bundle identifiers. `adjust_app_token` is the SDK app identifier, not the server's
API credential. The project's historical field `adjustTrackerToken` stores that
app token here, not an advertising link/tracker token.

Save only the verified app token using the existing operation:

```json
{
  "operation": "project_network_ids",
  "params": { "id": "project-id" },
  "body": { "adjustTrackerToken": "returned_app_token" }
}
```

This is also plan-only until confirmed. Replacing a different existing binding
needs the user's approval. Creation itself does not save the token to a project.
Read the project back after saving. If no project exists, use the separately
confirmed `project_create` workflow first.

Report app creation/reuse, ticket outcome, verified identity and saved binding
separately. SDK installation and device-event verification are separate local
integration work; app creation alone does not prove either. Do not promise
partner configuration merely because creation returned an app token, and do not dump raw
project data, template settings or credentials into the chat.
