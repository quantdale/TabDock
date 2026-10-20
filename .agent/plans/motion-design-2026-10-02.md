# Motion design for TabDock — 2026-10-02

Source prompt: `D:\Downloads\motiondesign prompt.txt` (active
requirement set for the native goal "create a motion design of this project").
Prior session shipped only `docs/motion-design/index.html` (an HTML
concept demo). The prompt requires a cohesive motion system in the actual
WPF application.

## Scope (first incomplete requirements)

1. Define a reusable motion language: durations, easing, and a
   reduced-motion policy in one place.
2. Centralize in `Infrastructure/MotionProfile.cs`: duration tokens
   (feedback ~90 ms, state ~150 ms, navigation ~220 ms), a single easing
   curve, reduced-motion detection, attached feedback behaviors, and
   entrance helpers.
3. Apply to the shared control templates in `App.xaml`:
   - Button: hover overlay fade + pressed scale feedback.
   - ListViewItem (TdListItem): hover/selection overlay transitions.
   - CheckBox: check-mark pop-in.
   - ComboBox/MenuItem popups: Fade animation, None under reduced motion.
4. Window entrances (MainWindow, CapturePickerWindow): subtle fade/rise.

## Out of scope / judgment calls

- Guest windows are native HWNDs hosted by `NativeHwndHost`; their
  positioning stays instant and authoritative (Shepherd/no-reparent).
  No WPF fade over native HWND content.
- Tab strip selection is an instantaneous native-content swap; feedback
  is limited to the tab item visuals.

## Validation

- `dotnet build TabDock.sln` (0 warnings/errors).
- Full xUnit suite.
- Visual smoke via agent-browser on captured structure is not applicable
  to WPF; use the existing Win32 validation paths only if behavior
  warrants. No application-behavior change expected beyond motion.
