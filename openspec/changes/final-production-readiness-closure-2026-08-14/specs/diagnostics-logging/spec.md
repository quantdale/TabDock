## ADDED Requirements

### Requirement: Every externally derived supervised field is terminal-safe
Interactive supervised recovery output SHALL pass window titles, executable
filenames, class names, candidate labels, pending filenames, and other
externally derived display strings through one bounded sanitizer that removes
or replaces C0/C1 controls, ESC/CSI/OSC bytes, DEL, CR/LF/tab, and Unicode
line/paragraph separators. Ordinary Unicode, including emoji and CJK, SHALL
remain readable when it fits the bound.

#### Scenario: Adversarial candidate fields cannot inject terminal output
- **WHEN** executable, class, title, filename, or candidate label values contain ESC, CSI, OSC, C0/C1 controls, DEL, line separators, emoji, CJK, surrogate pairs, and maximum-length input
- **THEN** interactive output contains no terminal control sequence, added line, tab, or unpaired surrogate, remains bounded, and preserves ordinary Unicode

### Requirement: Sanitized support artifacts are the primary sharing guidance
User-facing support guidance SHALL direct users first to `--support-bundle`
and/or `--doctor`. If raw logs are mentioned as an advanced diagnostic, the
guidance SHALL warn that they may contain window titles, executable paths, and
local environment details and SHALL require review/redaction before sharing.

#### Scenario: Public support instructions do not invite raw-log pasting
- **WHEN** a user follows the documented geometry/support workflow
- **THEN** the primary artifact is sanitized and raw log sharing is explicitly marked sensitive and review-required
