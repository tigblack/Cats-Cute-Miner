# Local file connection

For clients that can execute commands, Node.js 18+ and the bundled script are enough;
registering an MCP server in the AI client is optional. The script calls the same
Superleo MCP tools and preserves server permissions and confirmation gates.

## One-time setup in the game repository

1. Look inside the installed skill first: `superleo-monetization/assets/.env.superleo`
   holds the server URL and personal or scoped team key; `assets/.env` is also supported and takes
   precedence for compatibility. Use an existing file without asking for the key
   again or printing it. The bundled `assets/.gitignore` excludes both names; preserve it when installing.
   Verify the secret path is ignored in the game repository before filling it.
   If it is already tracked, stop and ask the user to remove it from tracking and
   revoke the exposed key; ignoring a tracked file is insufficient.
2. Only when no supported config exists, copy `assets/.env.superleo.example` to
   `assets/.env.superleo` inside the installed skill. Never overwrite a key or
   create a second file that shadows a configured identity. The legacy
   game-root `.env.superleo` remains supported if both skill-local files are absent; add
   `.env.superleo` to the game's `.gitignore` when using that fallback.
3. For individual setup, have the user enter their personal key locally in their editor. Do not read,
   print, source, upload, or include the secret file in a prompt. Exclude it from
   repository discovery searches. This is plaintext storage: restrict file access
   to the user and do not share or sync it with others. Exclude both skill-local config files when
   packaging or copying the skill for another person; Git ignore does not protect
   against direct folder copies or ZIP archives.
4. Default URL: `https://dostupai.aimulticast.org/mcp`. For local Superleo use
   `http://localhost:3000/mcp` (Vite proxy) or `http://localhost:8000/mcp`.
   A key must be issued in the target deployment. Production availability and
   monetization support must be verified; the template does not deploy anything.

## One shared setup for the internal team

The administrator does this **once**, not once per employee:

1. After deploying team-key support, open Admin → Users → API / MCP for an active
   internal credential owner. Choose **Монетизация — общий ключ команды** and create it.
2. Download `.env.superleo` from the one-time result and place it in the private
   installed skill's `assets/`. Preserve `assets/.gitignore`. Do not overwrite a
   configured file silently; `assets/.env` has higher priority and must not shadow it.
3. Run `access` and verify `scope: monetization-team` and `capabilities.monetization: true`.
   Do not distribute a config that reports personal access. Share this configured
   folder only through the team's approved private channel, never Git/public archives.
   Exclude personal/provider management secrets and operator-specific settings. For the same company
   AppLovin account, an administrator-approved private team copy may also include `assets/.env.sdk.local`
   containing only its shared client `APPLOVIN_SDK_KEY` (see `game-project.md`). This is never permission
   to share signing keys, provider OAuth/management credentials or a personal MCP key.
4. Each employee runs `access` from the game folder and starts the normal workflow.
   No individual key or email is required. The machine hostname is sent automatically.
   Optional email attribution can be set locally before starting the AI client:

   ```powershell
   $env:SUPERLEO_OPERATOR_EMAIL = 'your.name@multicastgames.com'
   ```

   For future Windows processes, set the same non-secret variable at User scope:

   ```powershell
   [Environment]::SetEnvironmentVariable('SUPERLEO_OPERATOR_EMAIL', 'your.name@multicastgames.com', 'User')
   ```

   Restart the AI client to inherit it. Never put one person's email in the shared package.

The server logs `monetization-team-call` with key ID, credential owner, claimed
device/email, tool, operation, confirmation flag, outcome and duration. No payloads
or credentials are logged by this audit. Claimed identity is **not verified** and
cannot change the bound account. Omit tool `email` even when operator email differs.
Logs are container logs, not a new panel history page. Shared keys expose only the
skill's access/catalog/monetization tools; REST, campaigns, admin and OAuth exchange
are denied. Existing provider prerequisites and write confirmations still apply.
Any holder can use the shared key, so do not distribute to partners. Revoke it if
leaked or a holder leaves; rotate and redistribute to the remaining team.

Run from the game root, substituting the actual installed skill path:

```sh
node "<skill>/scripts/superleo.mjs" access
node "<skill>/scripts/superleo.mjs" catalog
node "<skill>/scripts/superleo.mjs" call superleo-request.json
```

The request file contains only tool arguments, never credentials. For example:

```json
{"operation":"projects"}
```

For mutations pass the documented `operation`, `params`, `query`, `body` and
`confirm` arguments. Do not set `confirm: true` until the user approves the concrete
plan. Inspect `isError` and content/per-item failures, not just the process exit.
Check `capabilities.monetization` before making monetization calls.
Active internal `root`, `dev`, `user`, `art` and `guest` accounts are eligible;
`partner` and `member` are not. If an Art/Guest account still gets a role denial,
check that the target backend has the updated policy and that `access` reports
the intended identity. Do not replace its key with an administrator's key.

Meta app setup uses the same commands/key and catalog, with `facebook_apps`,
`facebook_pages`, `facebook_ad_accounts`, `facebook_browser_health`,
`facebook_create_app`, `facebook_refresh_app` and `facebook_project_config`.
Read [facebook.md](facebook.md) before using them. If the deployed catalog lacks
these names, updating the skill alone is insufficient: deploy the backend change.
No local Facebook browser, provider credentials or extra MCP connection is needed;
the Superleo backend calls its configured remote provision worker.

Adjust uses that same connection with `adjust_settings`, `adjust_apps`,
`adjust_find_app`, `adjust_app`, `adjust_create_app` and `adjust_ticket_status`.
Read [adjust.md](adjust.md). No browser or tunnel is needed for Adjust: the backend
uses its Adjust API credential. A missing operation requires a backend update,
not a different key or direct provider access.

The helper reads, in order, `assets/.env`, `assets/.env.superleo` (both relative to
its own installed script), then `.env.superleo` in the current working directory.
It advances only when the previous file is absent. It never merges files or falls back after invalid settings or access
errors, and never reads parent directories or connection credentials from environment variables.
Only optional audit email is read from `SUPERLEO_OPERATOR_EMAIL`. Keep one
configured file to avoid ambiguity. It does not evaluate shell expressions,
follow HTTP redirects, or retry requests. Do not change the URL to an unfamiliar
host without the user's approval: that host receives the credential. For a timeout
after a mutation, read back state before retrying. It targets Superleo's stateless
JSON-response MCP transport, not an arbitrary third-party MCP server.

Clients without command execution need a separately configured HTTP MCP connection;
the skill cannot load a file or register tools on their behalf.

## Handoff checklist: no per-employee credential setup

Transfer the **whole private configured folder**, not just `SKILL.md` and not a Git checkout that omits
ignored files. It must contain `scripts/`, `references/`, `assets/.gitignore` and exactly one configured
`assets/.env.superleo` or `assets/.env`. Keep that secret out of Git, public ZIPs and chat attachments.
For local MAX SDK integration, also include the explicitly approved same-account client SDK config
or use the game's existing key; backend management credentials remain on Superleo.

On the colleague's machine the agent should automatically run `access` and `catalog` with Node.js 18+,
confirm team scope, match the game and then audit/plan. No `npm install`, personal key, provider login,
local tunnel or manually constructed JSON is needed from the colleague for the bundled client itself.
The backend and shared browser host still need to be up, signed in and updated. Store review, human
business/privacy choices, game toolchains, signing and device checks are not removed by a shared key.
If a step is unavailable, report the exact dependency; do not request unrelated replacement credentials.
