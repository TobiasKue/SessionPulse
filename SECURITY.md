# Security

Please report security issues privately through
[GitHub's private vulnerability reporting](../../security/advisories/new),
not in a public issue. You will get an answer as soon as possible.

SessionPulse works offline and only reads and writes small local files
([docs/privacy.md](docs/privacy.md)). Relevant reports are, for example, a way
to make the hook print into Claude's context, run commands, or write outside
its status folder, and anything in the optional keypad approvals that could
allow a tool call nobody approved.
