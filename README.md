# GreenHedges.HomeServer

The services that run on a small always-on home server, in one repository and one .NET solution. Each deploys and runs as its own service.

So far they're **collectors**: each keeps **your own complete, permanent archive** of data that otherwise only lives in someone else's cloud, with dashboards built on top.

| Collector | Archives | Status |
|---|---|---|
| **Libre** ([docs](docs/libre/README.md)) | FreeStyle Libre glucose readings, fetched once a minute through LibreLinkUp, with Nightscout dashboards and Apple Health sync | Running |
| **Weather** ([docs](docs/weather/README.md)) | Every radio message from a home weather station's outdoor sensor, received with an RTL-SDR dongle and [rtl_433](https://github.com/merbanan/rtl_433), with Grafana dashboards | Archiving, with alerts, heartbeat and backups (Phase 1); dashboards next |

Shared by the collectors: a .NET 10 Worker Service under systemd, SQLite archives that keep the data as received, nightly backups pulled off the host, encrypted off-site copies, email alerts and an external heartbeat, and remote access over Tailscale only.

## Layout

| Path | What |
|---|---|
| `GreenHedges.HomeServer.slnx` | The one solution |
| `src/Common/`, `tests/Common/` | Shared by every service (`GreenHedges.HomeServer.Common`): host setup, email alerts, heartbeat, backups and off-site copies |
| `src/Collectors/Libre/`, `tests/Collectors/Libre/` | The Libre collector (`GreenHedges.HomeServer.Collectors.Libre.*`) |
| `src/Collectors/Weather/`, `tests/Collectors/Weather/` | The Weather collector (`GreenHedges.HomeServer.Collectors.Weather.*`) |
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
