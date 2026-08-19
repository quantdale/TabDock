# Final Manual Windows Release Smoke

**Status: NOT PERFORMED — BLOCKED_EXTERNAL until executed by a human on real
Windows against the exact Stage A candidate bytes.**

This is the final human gate for the **exact release candidate artifact**
(the FINAL distributed `TabDock.exe` from Stage A). It is a smoke test, not an
exhaustive suite: every item is a quick manual check on a real desktop. It may
only be marked `PASS` after the operator performs every applicable item against
those exact bytes; there is no tooling flag or boolean shortcut — only the
auditable evidence record described below satisfies the publication gate.

**Which artifact:** the FINAL distributed executable — the signed `TabDock.exe`
when production signing is in effect. Signing changes the bytes, so smoke
testing an unsigned artifact and then publishing the signed one is NOT
acceptable: the evidence must describe the exact bytes that will be
distributed. See `docs/release/publication-gates.md` for the trust model and
the evidence schema, and `docs/release/code-signing.md` for the signing
provider architecture (the candidate is signed once by the approved
HSM/cloud signer in Stage A; the manifest records `signingProvider`,
`signingKeyProtection`, and the signing certificate identity).

Result vocabulary per item: `PASS`, `FAIL`, `SKIP_NOT_APPLICABLE`,
`BLOCKED_ENVIRONMENT`. The overall smoke is PASS only when all applicable
items PASS with evidence (a checklist signed by the operator). The externally
visible gate statuses are exactly `PASS`, `FAIL`, `BLOCKED_EXTERNAL`, and
`BLOCKED_ENVIRONMENT`; see "External gate lifecycle" in
`docs/release/publication-gates.md` — nothing else may be recorded.

## Operator procedure (exact, unfakeable)

Every check must run against the **exact bytes that will be published**,
cryptographically bound to the Stage A run that produced them:

1. **Download the exact candidate** from the Stage A
   `prepare-release-candidate` run that produced it: artifact
   `tabdock-candidate-<sha>-<run-id>` from run `candidateWorkflowRunId`.
   Do NOT substitute a local build, a debug build, or an unsigned copy.
2. **Verify byte identity** before touching any checklist item:
   ```powershell
   # Must equal manifest finalSignedSha256 (the FINAL distributed hash).
   (Get-FileHash -Algorithm SHA256 .\TabDock.exe).Hash.ToLowerInvariant() -eq
     ((Get-Content release-manifest.json | ConvertFrom-Json).finalSignedSha256.ToLowerInvariant())
   Get-Content SHA256SUMS.txt   # same hash
   .\TabDock.exe --version      # reports the release commit + matching sha256
   .\TabDock.exe --selftest-native-abi  # capture the environment report for the compatibility gate
   ```
   Proceed only when the three hashes agree and `--version` reports the
   candidate commit. A local smoke that does not re-prove this hash is not
   bound to the published artifact and will be rejected.
3. Execute items 1–38 below on that verified binary.

Expected evidence format (see "Recording"): exactly one
`release-external-evidence.json` authored once with `schemaVersion: 2`, the
exact `sourceCommitSha` / `artifactSha256` / `candidateWorkflowRunId` /
`candidateArtifactName` above, and every gate carrying an ISO-8601
`completedAt` (not in the future beyond 5 minutes), `operator`, and `evidence`.

## Setup

- Fresh Windows 10/11 x64 session; at least 2-3 real applications available
  (e.g. Notepad, Windows Terminal, a browser).
- Use ONLY the verified `TabDock.exe` from the step above (never a debug build).
- Record the candidate SHA-256 and the machine OS build before starting.
- Confirm again that `TabDock.exe --version` reports the release commit and a
  SHA-256 equal to `release-manifest.json` `artifactSha256` AND `SHA256SUMS.txt`.
  Also retain the `--selftest-native-abi` environment report for the
  compatibility matrix (`docs/release/compatibility-matrix.md`).

## Application

| # | Check | Pass criteria |
|---|-------|---------------|
| 1 | Launch | Launcher appears; no crash; `--version`, `--doctor`, `--pending-recovery` exit 0 |
| 2 | Normal exit | Close from launcher; process exits cleanly |

## Groups

| # | Check | Pass criteria |
|---|-------|---------------|
| 3 | New Group | Creates a group |
| 4 | Rename group | Inline rename commits on Enter; blank rejected |
| 5 | Color picker | Accent color changes and persists |
| 6 | Group dropdown | Switch between groups via Group ▾ |
| 7 | Delete group (empty + populated) | Confirmation; guests released and running; group does not return after restart |

## Capture

| # | Check | Pass criteria |
|---|-------|---------------|
| 8 | Inline Add App | Captures a window into the open group |
| 9 | Global capture hotkey | Ctrl+Alt+G opens the standalone picker |
| 10 | Capture 2-3 applications | All present as tabs; active fills content area |
| 11 | Reject unsafe/unverified target | TabDock container itself or an unverifiable target is refused with a clear message |

## Tab operations

| # | Check | Pass criteria |
|---|-------|---------------|
| 12 | Switch tabs | Active guest swaps; others hidden |
| 13 | Reorder tabs | Order updates |
| 14 | Pop-out via X | Guest returns to standalone at original placement; process alive |
| 15 | Middle-click pop-out | Same as 14 |

## Guest lifecycle

| # | Check | Pass criteria |
|---|-------|---------------|
| 16 | Guest self-close | Tab disappears; container closes on last tab |
| 17 | Guest self-hide/tray | Tab disappears; guest stays hidden; not resurrected after restart |
| 18 | Release and restore window state | Normal/maximized guests return to exact prior state |

## Container

| # | Check | Pass criteria |
|---|-------|---------------|
| 19 | Minimize/restore | Guests re-glue; split panes stay partitioned |
| 20 | Maximize/restore | Content area fills; guests resize with it |
| 21 | Move/resize | Guests track the content area without gaps |

## Split screen

| # | Check | Pass criteria |
|---|-------|---------------|
| 22 | Create split pair | [ A \| B ] composite tab appears |
| 23 | Interact with LEFT | Left pane receives input; partner stays visible |
| 24 | Interact with RIGHT | Same for right |
| 25 | Display unrelated third tab | C full-width while A/B dormant; pair restores from either half |
| 26 | Split relationship persists | Through tab switches and restarts of the container |
| 27 | Restore split | Composite half restores exact A/B pair |
| 28 | Explicit exit split | Split ends only via Exit/selection/pop-out/close |
| 29 | Remove split member | Survivor takes full width immediately |

## Native guest interaction

| # | Check | Pass criteria |
|---|-------|---------------|
| 30 | Guest title-bar move | Dragging guest by its own title bar re-glues it to its pane |
| 31 | Re-glue after native move/resize | Same for native resize |

## Recovery

| # | Check | Pass criteria |
|---|-------|---------------|
| 32 | Force-kill TabDock (Task Manager) | Guests survive; none destroyed (never reparented) |
| 33 | Verify guest processes survive | All captured processes still running |
| 34 | Relaunch | Journal recovery restores identity-valid guests to intended state; intentionally hidden guests stay hidden |
| 35 | Pending legacy evidence | If `--doctor` reports pending v1/v2 evidence, `--pending-recovery` lists it read-only; no automatic rescue occurs |

## Support

| # | Check | Pass criteria |
|---|-------|---------------|
| 36 | Support bundle hotkey | Ctrl+Alt+Shift+D writes a sanitized ZIP to the desktop |
| 37 | Support bundle privacy check | ZIP contains no username, profile paths, or credentials (inspect before sharing) |

## Final exit

| # | Check | Pass criteria |
|---|-------|---------------|
| 38 | Normal shutdown | Guests released; independent state retained |

## Browser note

If Chrome/Edge/Brave/Firefox is unavailable, report the exact unavailable
browser. Never substitute unavailable-browser coverage with PASS.

## Recording (exact, once)

The operator signs the 38-item checklist with: the exact `candidateWorkflowRunId`
+ `candidateArtifactName`, the verified `artifactSha256` (`finalSignedSha256`),
the machine OS build, an ISO-8601 `completedAt` at signing time (not in the
future beyond the 5-minute clock-skew tolerance), and every item's result. The
smoke is `PASS` only with every applicable item `PASS`; anything unexecuted is
`BLOCKED_EXTERNAL` and is recorded as such in `release-manifest.json`
(`externalGates.finalWindowsHumanSmoke`). The gate's externally visible status
is exactly `PASS` / `FAIL` / `BLOCKED_EXTERNAL` / `BLOCKED_ENVIRONMENT` — see
"External gate lifecycle" in `docs/release/publication-gates.md`.

The evidence file `release-external-evidence.json` must be authored **exactly
once** with `schemaVersion: 2` and the bindings proven above (any reuse with a
different SHA, hash, run id, or artifact name fails; any `completedAt` in the
future fails; any wrong schema version fails — the publication gate fails
closed). A PASS smoke is recorded there as:

```json
{
  "schemaVersion": 2,
  "sourceCommitSha": "<exact 40-char candidate SHA>",
  "artifactSha256": "<exact FINAL artifact SHA-256>",
  "candidateWorkflowRunId": "<Stage A prepare-release-candidate run ID>",
  "candidateArtifactName": "tabdock-candidate-<sha>-<run-id>",
  "finalWindowsHumanSmoke": {
    "status": "PASS",
    "completedAt": "<ISO-8601 timestamp>",
    "operator": "<human identity>",
    "evidence": "38-item checklist signed by the operator; machine OS build <build>; SELFTEST[native-abi] report attached"
  },
  "physicalMixedDpi": {
    "status": "<PASS only after docs/release/mixed-dpi-qualification.md>",
    "completedAt": "...",
    "operator": "...",
    "evidence": "..."
  },
  "windowsCompatibility": {
    "status": "PASS",
    "windows10": {
      "status": "PASS",
      "build": "<Windows 10 x64 build, e.g. 10.0.19045.x>",
      "operator": "...",
      "completedAt": "...",
      "nativeAbiEvidence": "<--selftest-native-abi PASS ... environment report>",
      "evidence": "..."
    },
    "windows11": {
      "status": "PASS",
      "build": "<Windows 11 build, e.g. 10.0.26200>",
      "operator": "...",
      "completedAt": "...",
      "nativeAbiEvidence": "<--selftest-native-abi PASS ... environment report>",
      "evidence": "..."
    }
  }
}
```

The smoke gate can never be marked PASS by a boolean or by an assertion in
tooling: only this schema-validated record, bound to the exact source SHA,
the exact final artifact hash, and the exact Stage A run and artifact, is
accepted by the publication gate.
