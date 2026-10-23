# Contributing

## Build and test

```bash
dotnet build
dotnet test
```

.NET 10 SDK (pinned in `global.json`). One solution (`GreenHedges.HomeServer.slnx`) holds every project. Package versions are in `Directory.Packages.props` (central package management). Warnings are errors, and a NuGet vulnerability advisory fails the restore.

Vehicle Manager's database and page tests need SQL Server: run the local one in Docker from the repo root (`docker compose up -d --wait`, after copying `.env.example` to `.env`), and point `ConnectionStrings:VehicleManagerTests` at it. See *Local database* in [docs/vehicle-manager/README.md](docs/vehicle-manager/README.md). Without it they're skipped. CI runs them against its own SQL Server.

## Workflow

- Never commit to `main` directly. Create a branch, push it, and open a pull request.
- **CI** (`.github/workflows/ci.yml`) builds and runs all tests on every pull request and on `main`. Merge only when it's green.
- PRs are merged with a merge commit, then the branch is deleted (GitHub does it on merge).

A **local guard** blocks pushes to `main` and force pushes. Enable it once per clone:

```bash
git config core.hooksPath .githooks
```

Emergency override for a single push: `ALLOW_PROTECTED_PUSH=1 git push …`

## Secrets and personal data

Never commit credentials, API keys, ping URLs, glucose or weather data, databases, exports, captures or logs, and keep personal details (names, hosts, IPs, locations, sensor serials and ids, station ids, health information) out of files, commit messages, PRs and issues: this repository is public. `.gitignore` excludes databases, captures, logs, backups, alert state, your script defaults (`deploy/local.env`) and a private `notes/` folder. Keep it that way.
