# real-app-hardening-final-closure

Corrective closure campaign for the prematurely archived real-app hardening.
This is NOT a new hardening effort. It carries forward exactly four unfulfilled
acceptance obligations from `archive/2026-09-01-real-app-hardening/`:

1. Edge's preserved first valid `FAIL_PRODUCT` (second F11 cycle of the first
   invocation) has no defensible final disposition — it is `FLAKE_UNCLASSIFIED`.
2. Real-app visual tasks (1.7, 4.3) were checked complete without actual
   restricted real-browser visual packets or capable multimodal review.
3. Final-validation task 7.1 was checked complete without actually executing
   `scripts/validate.ps1 -Configuration Release -Ci -Publish`, the explicit
   native ABI gate, and the resource/privacy/recovery qualification.
4. The final report said "26 real-app tasks" while the canonical archived
   `tasks.md` contains 38 checkbox rows.

Historical archive remains evidence; this corrective change becomes the
authority for the outstanding closure requirements. It does not rewrite
history — the premature archive stands as history.

See `proposal.md`, `design.md`, `tasks.md` and `specs/` deltas. Validate with:

```
openspec validate real-app-hardening-final-closure --type change --strict --no-interactive --json
```