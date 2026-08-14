from pathlib import Path

path = Path("openspec/specs/ui-ux-hardening/spec.md")
text = path.read_text(encoding="utf-8")

start_marker = "### Requirement: The split pair SHALL persist until an explicit or structural teardown\n"
end_marker = "### Requirement: Native move/size completion SHALL reconcile against final geometry AND local z-order\n"
if text.count(start_marker) != 1 or text.count(end_marker) != 1:
    raise SystemExit(
        f"canonical split contract markers changed: start={text.count(start_marker)} end={text.count(end_marker)}"
    )

replacement = """### Requirement: The split pair SHALL persist until an explicit presentation switch or structural teardown
Once a split pair `{LEFT, RIGHT}` is active it SHALL remain the selected
composite unit while the user interacts with either split member and while a
non-member tab is merely hovered or right-clicked. An ordinary **left-click**
on a non-member tab SHALL instead be treated as an explicit presentation switch:
TabDock SHALL journal-safely hide both split members, clear the split composite,
make the clicked tab the ordinary active tab, and present it full-width without
releasing any captured member.

If either split-member hide cannot complete safely and returns recovery-pending,
TabDock SHALL fail closed: the split remains authoritative, the clicked
non-member SHALL NOT become selected/active, and the existing pair SHALL be
re-presented so a partially completed hide cannot leave one blank pane.

A newly captured window added while split is active SHALL continue to be hidden
journal-safely so the visible set remains the pair. Ctrl+Tab while split is
active SHALL continue to cycle only between the two split members. Split-member
focus, direct guest click, hover, and non-member context-menu open/close SHALL
not by themselves change split membership.

#### Scenario: Clicking a third tab exits split and opens that tab
- **WHEN** a split `{A, B}` is active with a third captured tab C and the user left-clicks C's ordinary tab
- **THEN** a normal split exit occurs, A and B are hidden but remain captured, C becomes the single active full-width guest, the ordinary three-tab strip is restored, and no member is released

#### Scenario: An uncertain split hide fails the third-tab switch closed
- **WHEN** the user left-clicks C while `{A, B}` is split and hiding either A or B becomes recovery-pending
- **THEN** C does not become the active tab, the split remains authoritative, and TabDock re-presents A and B through the existing split layout path while preserving recovery evidence

#### Scenario: Hovering a third tab leaves the pair untouched
- **WHEN** a split `{A, B}` is active with a third tab C and the user hovers C without left-clicking it
- **THEN** the pair remains `{A, B}`, both panes stay visible/glued, C stays hidden, and no split exit or release occurs

#### Scenario: Right-clicking a third tab leaves the pair untouched
- **WHEN** a split `{A, B}` is active with a third tab C and the user opens and dismisses C's context menu
- **THEN** the pair remains active and visible and C remains a captured hidden non-member

### Requirement: Split creation SHALL settle presentation after TabDock chrome closes
A split created from a TabDock context menu SHALL receive one bounded
post-popup presentation settle after TabDock-owned chrome is no longer active.
The settle SHALL re-run the existing split layout against the current content
rect and request real foreground for the current focused split member through
the existing identity-checked foreground API. This settle SHALL not synthesize
`WM_SIZE`, alter guest window styles, reparent guests, use `AttachThreadInput`,
or bypass foreground/identity guards.

#### Scenario: Initial split does not require a corrective pane click
- **WHEN** the user selects Split screen from a tab context menu
- **THEN** after the menu closes both guests are laid out in their current panes and the focused/initiating member receives a real foreground request, so the first correct split presentation does not depend on the user clicking inside that guest

#### Scenario: TabDock chrome is never preempted by the settle
- **WHEN** another TabDock-owned popup/chrome interaction is still active while a split settle is pending
- **THEN** the settle remains pending and does not foreground a guest until the chrome interaction has ended

"""

start = text.index(start_marker)
end = text.index(end_marker, start)
text = text[:start] + replacement + text[end:]
path.write_text(text, encoding="utf-8", newline="\n")
print(f"updated {path}")
