## MODIFIED Requirements

### Requirement: The close-confirm modal is re-entrancy-safe
While the close-confirm `MessageBox` is open (a nested dispatcher loop),
WinEvent-driven member removals SHALL NOT re-enter `Close()` on the same
window, and capture-picker requests SHALL be deferred until the prompt
returns. The Yes path SHALL re-validate the tab count, snapshot independent
released-target identities while members are still captured, complete the
safe release transaction, and post graceful close requests only after each
released target is revalidated. The No path SHALL release without posting
close requests, and Cancel SHALL leave the current members untouched.

#### Scenario: A guest destroying itself mid-prompt cannot re-enter Close
- **WHEN** the close-confirm prompt is open and the active guest destroys itself, emptying the tab list via the WinEvent handler
- **THEN** no second `Close()` is initiated on the window already inside `Closing`, and after the prompt returns the chosen action operates on the current re-validated tab list

#### Scenario: Yes closes exact released targets
- **WHEN** the user chooses Yes and all member releases complete safely
- **THEN** the released applications receive `WM_CLOSE` only when their independent HWND/PID/thread/executable/class/process-start identities still match

#### Scenario: Yes fails closed on uncertain release or identity
- **WHEN** a release remains pending or a released target cannot be proven exact
- **THEN** no unsafe close request is posted and the close-confirm transaction preserves the existing recovery evidence
