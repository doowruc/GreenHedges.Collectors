# GreenHedges.Collectors

Always-on services that keep **your own complete, permanent archive** of data that otherwise only lives in someone else's cloud, each with dashboards built on top. One repository and one .NET solution; each collector deploys and runs as its own service.

| Collector | Archives | Status |
|---|---|---|
| **Libre** ([docs](docs/libre/README.md)) | FreeStyle Libre glucose readings, fetched once a minute through LibreLinkUp, with Nightscout dashboards and Apple Health sync | Running. Moved here from [LibreCollector](https://github.com/doowruc/LibreCollector) |
| **Weather** | Every radio message from a home weather station's outdoor sensor, received with an RTL-SDR dongle and [rtl_433](https://github.com/merbanan/rtl_433), with Grafana dashboards | Design agreed in [WeatherCollector](https://github.com/doowruc/WeatherCollector); being built here |

Shared by both: a .NET 10 Worker Service under systemd, SQLite archives that keep the data as received, nightly backups pulled off the host, encrypted off-site copies, email alerts and an external heartbeat, and remote access over Tailscale only.

## Layout

| Path | What |
|---|---|
| `GreenHedges.Collectors.slnx` | The one solution |
| `src/Common/`, `tests/Common/` | Shared by every collector (`GreenHedges.Collectors.Common`): host setup, email alerts, heartbeat, backups and off-site copies |
| `src/Libre/`, `tests/Libre/` | The Libre collector (`GreenHedges.Collectors.Libre.*`) |
| `deploy/libre/` | Its systemd unit, deploy script, host setup, Nightscout stack and backup pull |
| `docs/libre/` | Its README (setup and use) and design notes |
| `deploy/aws/` | CloudFormation for the shared off-site backup bucket and its write-only users |
| `deploy/local.env.example` | Personal defaults for the deploy scripts (copy to the git-ignored `deploy/local.env`) |

Building, testing and the pull-request workflow: [CONTRIBUTING.md](CONTRIBUTING.md).

## Licence

[MIT](LICENSE)

---

Designed and built with [Claude Code](https://claude.com/claude-code).
