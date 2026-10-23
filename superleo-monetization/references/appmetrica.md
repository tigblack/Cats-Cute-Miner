# AppMetrica analytics provisioning

AppMetrica is analytics, not a sixth mediation network. Run only when requested.
Use the deployed Superleo catalog; OAuth stays on the backend in
`APPMETRICA_OAUTH_TOKEN`. Creating an OAuth client is not proof that this token
exists or has write permission. Never request the OAuth token in chat.

1. Read `appmetrica_apps` and `appmetrica_categories`. Errors mean unknown state,
   not an empty account. Match the intended app by verified identity/operator
   selection, not name alone. `appmetrica_app` takes params `{id}` (numeric string).
2. If absent, obtain the name, category from the returned list, IANA time zone and
   explicit `hide_address` / `gdpr_agreement_accepted` choices. Do not infer GDPR
   acceptance from permission to create an app. Consult the provider's current
   privacy-field descriptions with the operator; do not guess from field names.
3. `appmetrica_create_app` body is flat:
   `{name, category, time_zone_name, hide_address, gdpr_agreement_accepted}`.
   Plan first; send `confirm: true` only for the approved choices. The backend
   wraps it in `application` for the provider. No package-name field is sent by
   this create endpoint; creation does not prove SDK/package integration.
4. `created: false, needsSelection: true, existingIds` means a name conflict, not
   a successful creation. Inspect candidate IDs and get operator selection. Do not
   rename just to force creation. `created: true, verified: false` is partial:
   retain the returned ID and read it back, never create again.
5. Creation has no distributed exactly-once guarantee. Never run parallel creates
   for the same app or automatically retry after timeouts/errors. Reconcile list
   and detail reads; if outcome remains ambiguous, stop for operator review.
6. Bind the verified ID using `project_network_ids`, params `{id: projectId}`,
   body `{appMetricaAppId: "123"}`. Read the current project first and confirm a
   change to any existing different mapping. Read back after saving.
7. For authorized local SDK integration, `appmetrica_sdk_config` with params
   `{id}` returns only the client `sdkKey` and ID. Capture programmatically into
   ignored local build config, not console/model output. The ordinary list/detail
   and create outputs omit key values. Never embed OAuth or `import_token` in a
   game. Use the intended engine's SDK; provider creation alone does not install
   it or verify analytics events. Report those phases separately.

Sources: [Create app](https://appmetrica.yandex.com/docs/en/mobile-api/management/applications/createapplication),
[Categories](https://appmetrica.yandex.com/docs/en/mobile-api/management/applications/get-categories).
