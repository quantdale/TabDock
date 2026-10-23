## 1. Establish deterministic color round trips

- [x] 1.1 Confirm A03 attachment ownership and the current motion baseline; add the audited normal→selected→normal background/border fixture as a failing real-control test.
- [x] 1.2 Add hover, focus, selected+focus precedence, interrupted reversal and reload sequences under both motion policies with controlled/settled clocks rather than fixed sleeps.

## 2. Separate target tokens from animated values

- [x] 2.1 Store independent base fill/border tokens in the existing feedback owner and compute destinations solely from semantic state plus those tokens.
- [x] 2.2 Replace owned animation clocks without letting filled/intermediate effective colors redefine normal state; preserve the documented selection/hover/focus precedence and reduced-motion endpoints.
- [x] 2.3 Provide a single base-token update boundary for A07's later live-authority integration without implementing picker mapping or theme changes here.

## 3. Verify feedback and lifecycle

- [x] 3.1 Run `dotnet build TabDock.sln`, targeted color-state tests and A03 weak-reference tests; assert visible endpoints and release of clocks/controls in both motion modes.
- [x] 3.2 Record palette/duration/easing and native-guest invariants unchanged; defer the complete unit/Release gate until A02/A10 are safe.
