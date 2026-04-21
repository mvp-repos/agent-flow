# Contributing

## Development

- .NET 10 SDK required
- Build: `dotnet build` (from repository root)
- Documentation index: [docs/index.md](docs/index.md)
- Architecture and roadmap: [docs/project-overview-and-future-developments.md](docs/project-overview-and-future-developments.md)

## Pull requests

- Keep PRs small and focused
- Add/adjust tests when relevant
- Explain the guardrails/safety impact of changes
- CLI exit code **1** indicates workflow **Failed** or argument errors; **0** for Done, Skipped, or StoppedByUser (see [README.md](README.md#cli-reference))