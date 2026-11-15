## What and why

<!-- What does this change, and why is it needed? Link the issue: "Closes #123". -->

## How it was tested

<!-- Commands run, tests added, screenshots for UI changes. -->

## Checklist

- [ ] `dotnet format --verify-no-changes`, `dotnet build` and `dotnet test` pass
- [ ] `npm run format:check`, `lint`, `typecheck`, `test` and `build` pass (if `web/` changed)
- [ ] Tests added or updated (every bug fix has a regression test)
- [ ] No secrets, full URLs or exception messages in logs, API responses or emails
- [ ] SSRF protections are unchanged, or the change is explained above
- [ ] Database changes are migrations that stay compatible with the previous release
- [ ] README / `docs/` updated if behaviour, configuration or deployment changed
