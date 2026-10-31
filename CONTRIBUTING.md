# Contributing

Thanks for helping! Issues and pull requests are welcome.

## Before you start

* Read [docs/architecture.md](docs/architecture.md) and
  [docs/LESSONS.md](docs/LESSONS.md). Most of the odd-looking decisions have a
  reason recorded there.
* For larger changes, open an issue first so we can agree on the approach.

## Development setup

You need the .NET 10 SDK and bash. Everything except the plugin's Windows
integration builds and runs on any OS; you don't need a keypad or Options+
for most changes.

```bash
bash tests/hook/run.sh
```

```bash
dotnet test
```

```bash
dotnet run --project tools/KeyPreview
```

`tools/KeyPreview` writes PNGs of all key states to `artifacts/key-preview/`;
attach before/after images to pull requests that change how keys look.

To try the plugin on a device (Windows, Logi Options+ with Logi Plugin Service
6.4+), `dotnet build src/SessionPulsePlugin` registers the build output with the
service via a `.link` file. Remove that file before installing a `.lplug4`. The
plugin is not part of the solution, so a build in the repository root skips it.

## Guidelines

* **Code, comments, logs and docs are in English.** Key texts exist in English
  and German (`src/SessionPulse.Core/Presentation/Texts.cs`).
* **Logic goes into `SessionPulse.Core`** with tests; the plugin project stays a
  thin adapter. The build treats warnings as errors.
* **The hook must stay fast and silent:** bash builtins on the hot path, nothing
  on stdout, always exit 0. Add a case under `tests/hook/cases/` for every
  behavior change (`tests/hook/run.sh --update` regenerates expected files;
  review the diff).
* **Status file format changes** follow the compatibility rules in
  [docs/status-format.md](docs/status-format.md).
* **Privacy:** never log or store conversation content, titles or labels beyond
  what [docs/privacy.md](docs/privacy.md) describes.
* **SDK facts** come from the official Logi Actions SDK documentation, the
  `LogiPluginApi` package or the `Logitech/actions-sdk` samples. Cite the page
  in comments or LESSONS.md when you rely on something non-obvious.
* **No trademarked artwork.** Icons must be original.
* Add a line to `CHANGELOG.md` under `[Unreleased]`.

## License

By contributing you agree that your contributions are licensed under the
[MIT License with Commons Clause](LICENSE).
