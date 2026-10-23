# Publish a game to Google Play production

Trigger: "выложи в стор", "опубликуй в Google Play", or ad setup blocked because the game has no public
listing (`META_SETUP_STORE_LISTING_NOT_FOUND`, AdMob/Pangle store link failures). Production publishing is
public and effectively irreversible: show the full plan and get explicit approval; the final release needs
a separate confirmation string.

Prerequisite: the app exists in Play Console and a signed AAB is released to **internal testing** — see
[play-onboarding.md](play-onboarding.md) (`play_create_app`, `play_upload_internal_draft`,
`play_release_internal`). Only that tested build can go to production.

## Operations (Android projects; package comes from the Superleo project)

| Operation | Body | Effect |
| --- | --- | --- |
| `play_publishing_status` | — | Read-only: listings (title/short/full per language), graphics (`icon`, `featureGraphic`, `phoneScreenshots` sha256), tracks/releases, contact website/email, `readiness`, `consoleOnly` forms |
| `play_set_listing` | `{language, title (≤30), shortDescription (≤80), fullDescription (≤4000), expected:{title, shortDescription, fullDescription}}` | Writes one language's texts; `expected` = values from status (`""` when absent) |
| `play_set_images` | `{language, imageType: icon|featureGraphic|phoneScreenshots, sources:[...], expectedSha256:[...]}` | Replaces that image set. `sources`: `data:image/png;base64,...` / jpeg (≤8 MB each) or your own `/api/gallery/images/<your email>/<file>.png`. Icon/feature graphic: 1 image; screenshots: 2–8 |
| `play_set_contact_website` | `{website, expectedWebsite}` | Developer website (Meta verifies app-ads.txt there) |
| `play_set_data_safety` | `{csv}` | Submits the Data safety form (Play Console CSV format) |
| `play_set_store_listing` | `{projectId, app, devId, consoleAppId, requestId, listing:{title, shortDescription, fullDescription}, images:{icon:[1], featureGraphic:[1], phoneScreenshots:[2-8]} (PNG/JPEG data URIs), confirmation: "SET STORE LISTING <package>"}` | New (draft) app only: default listing through Play Console; fills empty image slots |
| `play_prepare_production` | `{…ids, requestId, versionCode, releaseName, releaseNotes (en-US), countries: "all", confirmation: "PREPARE PRODUCTION <package> <versionCode>"}` | New app: all countries + production release from the internally released bundle, saved in Publishing overview (not public) |
| `play_send_for_review` | `{…ids, requestId, versionCode, confirmation: "PUBLISH PRODUCTION <package> <versionCode>"}` | New app: sends all pending changes for Google review (public after approval) |
| `play_inspect_app_content` / `play_set_app_content` | see step 6 | App content declarations, content rating, store category and contact details (browser worker) |
| `play_release_production` | `{versionCode, expectedProduction:[...from status], userFraction?, releaseNotes?, confirmation: "PUBLISH PRODUCTION <package> <versionCode>"}` | Puts the internally released build into production and sends it for Google review |

Every write re-checks its `expected*` value and refuses with `*_CHANGED` if the saved state moved. Commits
never cancel changes already in Google review (`PLAY_CHANGES_IN_REVIEW`: wait). `PLAY_MANUAL_SEND_REQUIRED`
means Google wants the changes sent from Play Console → Publishing overview by a person.

## Flow

1. `play_publishing_status`. Report `readiness` and what is missing.
2. **Texts.** Draft title/short/full description from the game itself (repository, store-ready features,
   real IAP); never invent features. Show them, apply after approval with `play_set_listing`.
3. **Graphics.** Icon 512×512 PNG, feature graphic 1024×500, 2–8 phone screenshots (16:9 or 9:16, 320–3840 px).
   Prefer real screenshots from the game; generated art (superleo-orkes, saved to your gallery) is fine for
   icon/feature graphic if it matches the game. Show them before `play_set_images`.
4. **Developer website**: `play_set_contact_website` if empty (team: `https://multicastgames.com`, which
   hosts the studio `app-ads.txt`).
5. **Data safety** (`play_set_data_safety` with `{csv}`): the CSV must follow Google's template, which ships
   with the skill: `assets/data-safety-template.csv` (official sample from Play help 10787469). Build the
   game's CSV with `node <skill>/scripts/data-safety.mjs <spec.json> > data-safety.csv` and send its text.
   `assets/data-safety-multicast-default.json` (and the prebuilt `assets/data-safety-multicast-default.csv`)
   are the studio answers for MAX + Mintegral + Unity Ads + AppMetrica + Adjust, confirmed by the owner on
   2026-10-13: encrypted in transit, deletion on request (https://multicastgames.com/policy), no account
   creation; collected and shared, required: Device IDs, Approximate location, Purchase history, Diagnostics,
   App interactions, Other actions, Other app performance data, Crash logs, User IDs with their purposes.
   Use them only when the build has exactly that SDK set; otherwise copy the spec, adjust `types`
   (`PSL_*` IDs from the template) and show the user what changed. Never declare less than the SDKs collect.
6. **App content and Store settings** (Play Console browser worker; forms the API does not expose).
   `play_inspect_app_content` `{projectId, app, devId, consoleAppId}` (`confirm: true`, read-only) returns
   `pending` (sections still under "Need attention") and `storeSettings` (category, contact email, website).
   `play_set_app_content` adds `requestId`, `content` and `confirmation: "SET APP CONTENT <package>"`; send
   only the sections to change. Supported answers:

   | `content` key | Value | Studio default |
   | --- | --- | --- |
   | `privacyPolicyUrl` | https URL | `https://multicastgames.com/policy` |
   | `ads` | `true` / `false` | `true` for MAX games |
   | `signIn` | `{restricted: false}` | no login in studio games |
   | `adId` | `{uses: false}` or `{uses: true, purposes: [...]}` (`App functionality`, `Analytics`, `Advertising or marketing`, …) | MAX + Adjust: `App functionality`, `Analytics`, `Advertising or marketing` |
   | `governmentApp` / `financialFeatures` / `healthFeatures` | `false` / `"none"` / `"none"` | |
   | `targetAudience` | `{ageGroups: [...]}` from `13-15`, `16-17`, `18 and over` | `["18 and over"]` |
   | `contentRating` | `{email, category: "Game", answers: "all-no", acceptIarcTerms: true}` | `gdev@multicastgames.com` |
   | `storeSettings` | `{category, contactEmail, contactWebsite?}`; game categories (`Arcade`, `Casual`, `Puzzle`, …) | |

   Show the user every answer first: these are declarations of the studio, and `acceptIarcTerms` accepts IARC's
   Terms of Use. `answers: "all-no"` is only for games without violence, fear, sexuality, gambling, offensive
   language, drugs, crude humor, **digital purchases** (IAP = "Yes"), user chat or location sharing; any
   "Yes", a login, a child audience (under 13), government/financial/health features stay manual in Console.
   Each section is read first and skipped when it already matches; after Save the worker reloads and reads
   it back, so a repeated call only fills what is missing. A run stops at about 8 minutes and returns
   `remaining`; call again with those sections. `PLAY_APP_CONTENT_UNKNOWN_QUESTION` / `_UNEXPECTED_FORM`: Console
   changed, nothing was saved for that section; report the code and the question text. Data safety stays on
   `play_set_data_safety` (step 5). The rating is saved into Publishing overview and goes to review with the
   release.
**New app (never published).** The Publishing API only reads a draft app: listing/image writes fail with
`403 at POST :commit` and releases can only be drafts. Use the Console operations instead: steps 2–3 with
`play_set_store_listing` (the review step's "AI asset declaration" stays "Don't label assets"; if any image
was generated with AI, stop and let the user label it in Console), then `play_prepare_production`, show the
returned `pendingChanges` and get the user's explicit "publish", then `play_send_for_review`. Google first runs
quick checks (up to ~15 min): `canSend:false` with no blockers means wait and call prepare again (it is
idempotent). After the first approval the API operations below work as for existing games. The worker retries browser timeouts
itself (up to 3 attempts within 5 minutes) only for inspections, `play_set_app_content`, `play_set_store_listing`
and `play_prepare_production`; a `PLAY_*` code is final, and create/upload/release/send are never repeated
automatically: read the state (`play_inspect_*`, Publishing overview via `play_prepare_production`) first.
`play_prepare_production` returns `inReview: true` without touching anything while changes are in review.

7. **Release.** Show the plan (versionCode from the internal track, rollout share, release notes), get
   approval, then `play_release_production` with the confirmation string. Read status again: production
   shows the release; Google review takes hours to days.
8. After Google approves (the game is public), run ad setup ([ad-setup.md](ad-setup.md)).

Never publish a smoke/test app, another game's build, or a build that was not released internally.
