using System.Runtime.CompilerServices;

// Deterministic xUnit coverage (tests/UnitTests) exercises internal seams
// such as WindowIdentityGate and the recovery identity evaluators against
// recording fakes instead of real desktop HWNDs. This is the only friend
// assembly; product surface is unchanged.
[assembly: InternalsVisibleTo("TabDock.UnitTests")]
