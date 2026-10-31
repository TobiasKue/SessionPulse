# Releasing

Versions follow [Semantic Versioning](https://semver.org/). The Logi package
format only allows `major.minor[.build]` with plain numbers, so releases use
`X.Y.Z` without pre-release suffixes (docs/LESSONS.md).

1. Set the version in **both** places:
   * `Directory.Build.props`: `<VersionPrefix>X.Y.Z</VersionPrefix>`
   * `claude-plugin/.claude-plugin/plugin.json`: `"version": "X.Y.Z"`
2. In `CHANGELOG.md`, rename `## [Unreleased]` to `## [X.Y.Z] - YYYY-MM-DD`
   and add a new empty `## [Unreleased]` above it.
3. Run the checks locally: `bash tests/hook/run.sh` and `dotnet test`.
4. Commit, tag `vX.Y.Z` and push the tag.

The `Release` workflow then checks that tag, versions and changelog agree, runs
all tests, and publishes a GitHub release with:

* `SessionPulse_X_Y_Z.lplug4`: the keypad plugin (double-click to install),
  packed and verified with `logiplugintool`.
* `sessionpulse-claude-plugin-X.Y.Z.zip`: the Claude plugin.

Before a public release, check that there is no personal data, host names or
session ids in the repository or its history.

## Marketplace (optional)

The Logitech Marketplace accepts the `.lplug4` through its submission form. It
additionally requires an end-user license agreement of the developer and, if
personal data is collected, a privacy policy (Logi Actions SDK,
`marketplace-approval-guidelines.md`). SessionPulse collects none, see
[privacy.md](privacy.md).
