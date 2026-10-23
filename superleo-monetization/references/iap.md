# In-app purchases

Current import creates/updates **Google Play one-time products and Multicast backend products**. It does not create App Store/RuStore products or subscriptions. App Store product reading and backend mapping are not store-side product creation.

## Google Play prerequisite: billing-enabled build

Before creating Google Play products through the panel/MCP, establish that the
correct app has a build with Google Play Billing uploaded and released in Play
Console. For a new game, prepare an **AAB with billing support**. Google documents
that billing-related product configuration is enabled after publishing a build
containing the Billing Library; any track, including **internal testing**, works.
A production rollout is not required. An AAB sitting on disk or only uploaded as
an unfinished draft is not proof that this prerequisite is complete.
Source: [Google Play Billing — Getting ready](https://developer.android.com/google/play/billing/getting-ready#upload).

- Check the actual Android IAP integration (e.g. Unity IAP and its Android billing
  dependency), target package name, build configuration and, when available, the
  merged/built manifest's `com.android.vending.BILLING` permission. A permission
  alone is not an implementation; do not add it as a substitute for the SDK.
- Check available release evidence or ask the user whether the matching billing
  build has been released to a track. Local SDK presence, successful MCP login,
  or an empty product list does not establish this. Reuse an existing qualifying
  release; do not demand a new upload for every SKU.
- If missing or unknown, proactively offer: "Для создания покупок через Superleo
  сначала нужна AAB-сборка с поддержкой Google Play Billing в Play Console.
  Достаточно внутреннего тестирования, выпускать игру в прод не нужно. Давай
  проверим биллинг в проекте, подготовим AAB и поможем загрузить её?"
- A user-approved preparation plan may include integrating the compatible SDK
  and building the AAB using the project's tooling. Before uploading or releasing,
  separately confirm package, artifact/version and target track. Preserve signing
  configuration; never request private signing keys in chat. For creation and a
  first internal draft upload, read [play-onboarding.md](play-onboarding.md) and
  check the deployed `play_*` catalog. File staging remains an operator dependency;
  `play_release_internal` can release an exactly verified draft with separate
  approval. It cannot publish to production or configure testers/license testing.
- Until readiness is confirmed, continue discovery and drafting SKUs/prices but
  hold Google Play creation/activation. If an import reports a billing-build
  prerequisite error, inspect per-target results (backend writes may have
  succeeded), offer this setup step and reconcile before retrying. Do not
  misdiagnose it automatically as a bad MCP key.

## Product operations

Read `iap_status`, `iap_games`, then `iap_game_config` with params `{gameId}` and `iap_products` with query `{gameId}`. `iap_store_products` takes query `{platform: "google"|"apple", packageName}`. A credentials failure is an operator dependency; do not retrieve private signing keys or change admin integrations.

`iap_save_game` body: `gameId`, optional `displayName`, **both** `googlePlayPackageName` and `appStoreBundleId` are required by the existing backend. Optional `ruStorePackageName`, `ruStoreAppId`, `adjustAppToken`, `adjustPurchaseEventToken`. Read current config before an update and preserve fields; do not invent an iOS bundle ID to satisfy validation. If the returned config masks existing values, ask the operator instead of overwriting them with empty strings.

`iap_import` creates/updates and activates products; first show prices in USD, rewards, product types and package/game target for approval. Example body (illustrative, not a default price):

```json
{
  "gameId": "gm_example",
  "packageName": "com.example.game",
  "products": [
    {
      "sku": "coins_1000",
      "type": "consumable",
      "title": "1,000 Coins",
      "description": "Adds 1,000 coins",
      "priceUsd": 1.99,
      "rewards": [{"sku": "coins", "quantity": 1000}]
    }
  ]
}
```

Use only `consumable`/`non_consumable`, unique nonempty SKUs and positive prices/quantities. Regional prices are converted by the existing Google Play backend integration; don't manually calculate exchange rates. An import may affect existing SKU prices. The backend also writes product mappings; read existing products and avoid silently removing existing App Store/RuStore mappings on update (the importer supplies empty mappings for these stores). If preservation is required, stop or perform an explicitly approved backend-only mapping update after reading its full shape.

`iap_save_product` is **backend-only**, with body `{gameId, product}`. Product uses `productSku`, `productType` (0 consumable, 1 non-consumable), `googlePlaySku`, `appStoreSku`, `ruStoreSku`, `rewards`; preserve existing mappings. It does not activate a store product.

Import returns `{success: true, data: {results: [...]}}` even when individual targets fail. Inspect **each** `results[].googlePlay.ok/error` and `results[].backend.ok/error`. A `created but not activated` error is partial success, not absence. Report per-SKU/per-target state. Do not retry the entire import after a partial result; read both targets and ask for a bounded repair if the available operation would overwrite successful state.

Read products back, return verified IDs and remaining SDK/catalog integration work. This workflow creates purchasable products; it does not make a purchase, confirm a payment or validate an actual device transaction.
