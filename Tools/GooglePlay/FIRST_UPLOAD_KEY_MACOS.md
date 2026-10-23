# First upload key on macOS

`new-upload-key-macos.py` prepares a **first upload key only** using Python 3.9+
and an explicitly selected installed JDK. It performs no network calls, Console
changes, signing-key resets, Unity builds or AAB uploads. Owner legal/audience
decisions still block the release. A historical Windows upload key is preserved;
its absence on this Mac alone is never sufficient authorization to create a key.

The parent must first save the **actual authenticated Play Console readback** to
`release/google-play/evidence/google-play-signing-current-20261018.json`. The
helper was checked against the parent's actual 2026-10-18 receipt and requires
its following contract (string IDs, literal JSON booleans):

| Field | Required value |
| --- | --- |
| `schema_version` | `1` (integer) |
| `package` | `com.multicastgames.catscuteminer` |
| `developer_id` / `play_app_id` | `8879742639795815518` / `4974963942177850884` |
| `console_url` | HTTPS `play.google.com/console/u/<account>/developers/8879742639795815518/app/4974963942177850884/keymanagement` (query allowed) |
| `evidence_kind` | `native_console_text` |
| `observed_upload_key_message` | `Certificate fingerprints will be shown here after you upload your first app bundle` |
| `upload_key_certificate_registered` / `aab_uploaded` | Both `false` |
| `app_signing_key.manager` / `.status` | `Google Play` / `In use` |
| `read_only` | `true` |
| `app_signing_changes_performed` / `upload_key_changes_performed` | Both `false` |
| `actor` / `checked_at` | Nonempty attribution / observation time |

Additional public provenance fields are welcome. Upload certificate/fingerprint
fields must be absent or empty (the Google-managed **app signing** certificate
is a separate field and is allowed). If the receipt schema changes, review and
align the helper contract with that real readback before running.
**Do not copy invented success evidence or change facts to pass the gate.** This
local JSON is trusted owner evidence, not an independent Console authentication
mechanism. Recheck Console if its state may have changed. A bound upload
certificate or any prior bundle requires a different workflow.

Run once on the owner's Mac, after review. Prepare existing parent directories
`~/MulticastSigning` and `~/MulticastSigning/com.multicastgames.catscuteminer`
owned by the current user with mode `700`. Keep this tree outside the project,
cloud sync and backup sync; the helper restricts output to this tree and rejects
known cloud paths, but cannot detect arbitrary third-party sync configuration.
The final output directory **must not exist**, even empty or as a symlink.

```sh
python3 Tools/GooglePlay/new-upload-key-macos.py \
  --create-first-upload-key \
  --output-dir /Users/arthuri/MulticastSigning/com.multicastgames.catscuteminer/first-upload-20261018 \
  --jdk /Applications/Unity/Hub/Editor/6000.6.4f1/PlaybackEngines/AndroidPlayer/OpenJDK \
  --first-upload-evidence "$PWD/release/google-play/evidence/google-play-signing-current-20261018.json"
```

The helper exclusively creates a mode `700` directory, then a mode `600`
`signing.secret.json` **before** invoking keytool. Passwords are independently
generated in memory and passed using `-storepass:env` / `-keypass:env` in a minimal
child environment. They never enter command arguments, stdout, source, shell
evaluation or logs. The resulting `upload.jks` is private JKS, alias `upload`,
RSA 2048, SHA256withRSA, valid 10,000 days, with public DN
`CN=Multicast Games Limited, O=Multicast Games Limited, C=CY`.

Export/list checks require the expected `PrivateKeyEntry`, identical PEM/DER
certificates, and calculate SHA-256, SHA-1 and the Base64 SHA-1 Meta upload hash.
Only after successful checks does the private JSON receive its final six fields:
`package`, `keystore`, `alias`, `storePassword`, `keyPassword`, `certSha256`.
This is the schema consumed by `google_play_macos.py`; incomplete JSON lacks
`certSha256` and fails that builder's signing-field checks. The build wrapper requires the active verified signing receipt in
`profile.release.signing_evidence_ref`. The parent must save the public receipt
and explicitly select it before the new key can build anything.

Success exits `0` and prints only public JSON with `keystore`, `secretRef`,
`alias`, `certificateSha256`, `certificateSha1`, `metaUploadKeyHash`,
`status: first_upload_key_verified`, and `first_upload_evidence_ref`. The parent
saves curated public evidence. This helper never writes project evidence or
overwrites/deletes historical `upload-signing.json` or existing keys.

Failure exits `1` (interrupt: `130`) with a sanitized stderr diagnostic and no
success JSON. Keytool calls have a 120-second timeout; raw output and exceptions
are suppressed. **Preserve every partial file and its original output path. Do
not delete it, retry blindly, or choose another path to evade the refusal.**
Credentials remain in the private JSON if key generation partly succeeds; a
completed temporary secret may remain if final replacement fails. Inspect and
recover privately before deciding any next action. No automatic retry, cleanup
or resume is implemented. Keep the successful key and credentials in approved
private, non-synced storage; never paste their contents into release evidence.
