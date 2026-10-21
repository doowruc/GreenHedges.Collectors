# GreenHedges.Collectors

Always-on services that keep **your own complete, permanent archive** of data that otherwise only lives in someone else's cloud, each with dashboards built on top. One repository and one .NET solution; each collector deploys and runs as its own service.

| Collector | Archives | Status |
|---|---|---|
| **Libre** ([docs](docs/libre/README.md)) | FreeStyle Libre glucose readings, fetched once a minute through LibreLinkUp, with Nightscout dashboards and Apple Health sync | Running |
| **Weather** ([docs](docs/weather/README.md)) | Every radio message from a home weather station's outdoor sensor, received with an RTL-SDR dongle and [rtl_433](https://github.com/merbanan/rtl_433), with Grafana dashboards | Archiving, with alerts, heartbeat and backups (Phase 1); dashboards next |

Shared by both: a .NET 10 Worker Service under systemd, SQLite archives that keep the data as received, nightly backups pulled off the host, encrypted off-site copies, email alerts and an external heartbeat, and remote access over Tailscale only.

## Layout

| Path | What |
|---|---|
| `GreenHedges.Collectors.slnx` | The one solution |
| `src/Common/`, `tests/Common/` | Shared by every collector (`GreenHedges.Collectors.Common`): host setup, email alerts, heartbeat, backups and off-site copies |
| `src/Libre/`, `tests/Libre/` | The Libre collector (`GreenHedges.Collectors.Libre.*`) |
| `src/Weather/`, `tests/Weather/` | The Weather collector (`GreenHedges.Collectors.Weather.*`) |
| `deploy/deploy.sh` | Publishes a collector and installs it on the host (`deploy/<collector>/deploy.sh` for short) |
| `deploy/libre/` | The Libre collector's systemd unit, deploy script, host setup and Nightscout stack |
| `docs/libre/` | Its README (setup and use) and design notes |
| `deploy/weather/` | The Weather collector's systemd unit, deploy script and host setup |
| `docs/weather/` | Its README (day to day) and design notes |
| `deploy/mac/` | The Mac's daily backup pull, for either collector |
| `deploy/aws/` | CloudFormation for the shared off-site backup bucket and its write-only users |
| `deploy/local.env.example` | Personal defaults for the deploy scripts (copy to the git-ignored `deploy/local.env`) |

Building, testing and the pull-request workflow: [CONTRIBUTING.md](CONTRIBUTING.md).

## Licence

[MIT](LICENSE)

---

Designed and built with [Claude Code](https://claude.com/claude-code).
