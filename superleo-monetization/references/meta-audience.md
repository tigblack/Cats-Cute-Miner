# Meta Audience Network monetization

Use for Meta/Facebook **ads shown inside the game** through MAX, including audit and repair.
This is separate from developer-app creation, Facebook SDK events, ad accounts/Pages and buying traffic
in [facebook.md](facebook.md). Do not substitute Facebook App ID for Audience Network Placement ID.

## Connection and current boundary

Use the same configured skill key, `access`, `catalog` and `call`. Provider/browser credentials stay
on Superleo and the shared Facebook provision PC. Never ask each employee for a Meta token.
The backend and updated `scripts/fb-dashboard` worker must both be deployed. The existing worker
must have `FB_CDP_URL` pointing to a running, signed-in browser with access to the correct business
in Monetization Manager. `facebook_browser_health` only establishes service reachability/busy state.

Public placement-create API support was not established; do not invent Graph API endpoints.
These `meta_audience_*` operations use browser automation of the modern Monetization Manager
(`/pub/...`), replayed from the flow verified live on 2026-10-20. A missing or ambiguous control is a
blocker (`META_SETUP_*_CONTROL`, `UNEXPECTED_FORM`), not permission to guess a different button.

For a new Android game prefer the one-approval flow in [ad-setup.md](ad-setup.md). Automated:
property creation (or reuse of an empty one with the exact name), adding the Android app from its Google
Play URL, keeping the payout account when exactly one exists, app-ads.txt review submission, AppLovin
MAX mediation choice, ad space and placements. Not automated: choosing among several payout accounts,
entering payout/tax details, accepting new terms, Meta's own review decision. iOS is not automated.

Meta verifies app-ads.txt on the **developer website of the Google Play listing**. If the listing has
none, review fails with "couldn't find your developer website URL"; set it with
`play_set_contact_website` (`{website, expectedWebsite}` from `play_contact_website`) and resubmit.
Too many browser reads get the business temporarily blocked (`META_RATE_LIMITED`): stop for hours.

## Discover, bind, create

All operations go through `superleo_monetization`. Reads do not require `confirm`.
Binding and creation are plan-only unless `confirm:true` follows approval of that concrete plan.
The backend derives platform/package/store ID from the saved Superleo project; do not pass overrides.
iOS requires both the bundle and numeric App Store ID saved on the project.

| Operation | Params | Body / purpose |
| --- | --- | --- |
| `meta_audience_status` | `{id: projectId}` | Cached binding/evidence only; no browser job |
| `meta_audience_properties` | `{id: projectId}` | Read properties matching this game's verified store identity |
| `meta_audience_inspect` | `{id: projectId, propertyId}` | Fresh read of exact property, store, ad spaces, placements and available status evidence |
| `meta_audience_bind` | `{id: projectId}` | `{propertyId, expectedPropertyId}`; expected ID from status, or literal `null` if unbound |
| `meta_audience_create_placement` | `{id: projectId}` | `{propertyId, adSpaceId, name, format: "REWARD"|"INTER"|"BANNER"}` |
| `meta_audience_setup` | `{id: projectId}` | `{name}` — create/complete the property (Android), bind it if the project has no binding; returns `done[]`, `steps{payout, review, mediation}`. Up to ~6 min |
| `meta_audience_resubmit` | `{id: projectId}` | `{}` — resubmit app-ads.txt review of the bound property; `review: submitted / website-missing / action-needed` |

Placement IDs are the full `<app id>_<placement id>` used by SDKs and MAX. Payout is `ready` when
exactly one payout account lists the game's app. Meta has no placement-level test mode (test ads go only
to registered test devices), so placements report `testMode: off`; check SDK test flags in the build.
New placements show `Ready to publish` (`status: ready`); app approval is separate (`reviewStatus`).

1. Resolve the exact game, platform and store listing. Read status, then discover or inspect the bound
   property. Empty/incomplete/error results are not permission to create anything.
2. Match the saved store identity, not just property name. Multiple matches need a selection; never
   pick the first business/property. If a matching property is not bound, propose binding it with the
   expected previous ID. This saves separate monetization metadata and leaves Facebook App/Page alone.
   If status reports `identityChanged:true`, discard old evidence and explicitly rebind after verifying
   the current game. Use the old `binding.propertyId` as `expectedPropertyId`, even if reusing the same property.
3. Inspect existing placements and choose the correct format/ad space. Reuse a verified suitable
   placement. For a genuinely missing placement, propose creation in an existing, verified ad space.
   Identical name/format/ad space is reused; a conflicting name stops rather than creates a duplicate.
4. Save returned IDs and readback results. If a create times out or reports an uncertain outcome,
   inspect only; do not retry the write blindly. A failed evidence-cache write does not undo a real
   placement: retain its ID and inspect again. Creation alone is not approval or live delivery.

## Attach to MAX

Meta differs from Google's Partner bidding-unit type: AppLovin documents that existing Meta
placements can be used for bidding. There is no invented Meta bidding checkbox in this workflow.
Read [AppLovin's Meta guide](https://support.applovin.com/en/max/mediated-network-guides/sdk-bidder-network-guides)
and its [Management API](https://support.applovin.com/en/max/advanced-features/ad-unit-management-api)
when implementation details need verification.

For `max_create`, include `projectId`, the normal game/format identity, `meta_enabled:true` and
`meta_placement_id` from verified readback. For `max_update`, read the existing unit first; pass its
identity, `projectId`, and only the intended network change:

```json
{
  "FACEBOOK_NETWORK": {
    "disabled": false,
    "ad_network_ad_units": [{"ad_network_ad_unit_id": "<verified Placement ID>"}]
  }
}
```

Put that object inside `ad_network_settings`. Meta requires **no** `ad_network_app_id` or App Key
in this MAX mapping. `FACEBOOK_BIDDING` / `META_BIDDING` are not supported aliases; native formats
are outside this flow. Preserve the MAX unit ID, all unrelated networks, caps and floors.

Before enabling a changed Meta mapping, the backend freshly checks the bound property's store,
placement ownership/format, approval, payout and test status. Missing evidence blocks activation.
Read back MAX to verify the same `FACEBOOK_NETWORK` Placement ID and `disabled:false`; do not
infer success from a create response. For pending review that needs a first SDK request, report
the provider onboarding dependency: the current production-readiness guard does not auto-enable
an unapproved network just to trigger review. An explicitly scoped SDK/test onboarding step is separate.

## Report what must be fixed

Use [ad-readiness.md](ad-readiness.md): property binding, correct store URL, required placements,
MAX mapping, review, payout, test-mode evidence and SDK/device verification are separate checks.
`Ready to publish` on a placement is not proof of app approval or payouts. Unknown fields stay
unknown. The integrations page reads a 15-minute evidence snapshot and does not run a browser job
on every refresh; use explicit inspection to refresh it. `ads_approval_status` (and the panel's
"Одобрение рекламных сетей") shows Meta review together with the other networks. Fresh provider settings alone do not prove
MAX mappings or device serving. Check the MAX Meta adapter and SDK test flags in the game separately.

Always finish with concrete **«Что нужно поправить»** / **«Что ещё нужно проверить»**, naming the
resource, observed/required values, exact action, whether the skill or operator can do it, and the
verification needed. Do not claim an unconfigured Meta network will necessarily show test ads:
it may give no fill. No campaigns, spending, payout changes or destructive cleanup are authorized.
