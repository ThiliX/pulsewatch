# Contributing to PulseWatch

Thanks for helping! Bug reports, docs fixes and code are all welcome.

## Before you start

- **Bugs:** open an issue with the bug template. Include steps to reproduce and what you expected.
- **Features:** open an issue first so we can agree on the approach before you spend time on code.
- **Security problems:** don't open an issue; follow [SECURITY.md](SECURITY.md).

By contributing you agree that your work is released under the [MIT License](LICENSE), and you follow the
[Code of Conduct](CODE_OF_CONDUCT.md).

## Development setup

Follow [Getting started](README.md#getting-started) in the README: Docker for PostgreSQL and Mailpit, the .NET 10
SDK, Node.js 22+. In short:

```bash
cp .env.example .env                      # set POSTGRES_PASSWORD
docker compose up -d
dotnet user-secrets set "ConnectionStrings:PulseWatch" "<connection string>" --project src/PulseWatch.Api
dotnet user-secrets set "ConnectionStrings:PulseWatch" "<connection string>" --project src/PulseWatch.Worker
dotnet run --project src/PulseWatch.Api -- --migrate
dotnet run --project src/PulseWatch.Api       # terminal 1
dotnet run --project src/PulseWatch.Worker    # terminal 2
cd web && npm install && npm run dev          # terminal 3, http://localhost:5173
dotnet run --project src/PulseWatch.Api -- seed-demo   # optional: demo data
```

## Checks to run before a pull request

CI runs exactly these ([.github/workflows/ci.yml](.github/workflows/ci.yml)); please run them locally first.

```bash
dotnet format PulseWatch.slnx --verify-no-changes
dotnet build PulseWatch.slnx
dotnet test                    # needs Docker running (Testcontainers)

cd web
npm run format:check
npm run lint
npm run typecheck
npm test
npm run build
```

## Coding rules

- **Layers:** Domain references nothing; Application references Domain; Infrastructure, Api and Worker sit on top.
  Architecture tests enforce this.
- **Warnings are errors**, including analyzers. Log with `[LoggerMessage]` source-generated methods.
- **Never log secrets or full URLs.** Use `LogRedaction.UrlOrigin` for monitored URLs. No passwords, tokens,
  connection strings, email bodies or exception messages in logs, API responses or emails.
- **SSRF rules are not configurable.** Don't add settings that relax `PublicAddressPolicy`, `DestinationValidator`
  or `SafeConnect`. Tests that need a local target replace `IAddressPolicy`/`IDnsResolver` through dependency
  injection inside the test project.
- **Every query is scoped to the current user.** Not found and not yours both return the same 404.
- **Parse URLs with `AbsoluteUrl.TryParse`**, never `Uri.TryCreate(..., UriKind.Absolute, ...)` directly: on Linux
  that accepts `/path` as a file URL.
- **Tests are deterministic:** use `FakeTimeProvider`, fake DNS and fake HTTP handlers. Tests never call the public
  internet or a real mailbox. Every bug fix gets a regression test.
- **Database changes** go through EF Core migrations (see [Database migrations](README.md#database-migrations)).
  Keep them backward compatible with the previous release: add a column before code uses it, drop it later.
- **Packages:** versions live in `Directory.Packages.props` (central package management). Prefer packages that
  have been released for at least two weeks.
- **Frontend:** TypeScript strict mode, no inline scripts or styles that the Content Security Policy would block,
  every status shown with text and an icon (not only colour), keyboard accessible.
- **Docs:** update the README or `docs/` when behaviour, configuration or deployment changes.

## Pull requests

- Keep each pull request to one change; small is easier to review.
- Describe what changed and why, and how you tested it. Screenshots help for UI changes.
- Fill in the checklist in the pull-request template.
- A maintainer reviews within about a week. CI must be green before merging.

## Commit messages

Short imperative subject (for example "Add webhook alert channel"), then a body explaining why if it isn't obvious.
