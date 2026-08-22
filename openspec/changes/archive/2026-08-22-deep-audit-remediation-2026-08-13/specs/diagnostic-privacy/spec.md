## ADDED Requirements

### Requirement: Support-bundle text SHALL not contain personal paths or secrets
Every support-bundle entry, including doctor text, JSON, trace, and recent-log
text, SHALL sanitize embedded profile/AppData paths case-insensitively across
slash styles and SHALL redact absolute executable/error paths and
credential/token-like values before writing the ZIP.

#### Scenario: Embedded path variants are removed from every representation
- **WHEN** a diagnostic contains quoted, unquoted, JSON, timestamp-prefixed, mixed-case, or slash-variant user/AppData paths
- **THEN** no bundle entry contains the actual username, profile path, AppData path, or equivalent personal path

#### Scenario: A generated archive is inspected entry by entry
- **WHEN** a real support ZIP is generated on the current machine
- **THEN** every entry passes the privacy fixture scan and the archive remains readable
