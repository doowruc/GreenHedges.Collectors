# GreenHedges.Collectors

Always-on services that keep **your own complete, permanent archive** of data that otherwise only lives in someone else's cloud, each with dashboards built on top. One repository and one .NET solution; each collector deploys and runs as its own service.

| Collector | Archives | Status |
|---|---|---|
| **Libre** | FreeStyle Libre glucose readings, fetched once a minute through LibreLinkUp, with Nightscout dashboards and Apple Health sync | Running; moving here from [LibreCollector](https://github.com/doowruc/LibreCollector) |
| **Weather** | Every radio message from a home weather station's outdoor sensor, received with an RTL-SDR dongle and [rtl_433](https://github.com/merbanan/rtl_433), with Grafana dashboards | Design agreed in [WeatherCollector](https://github.com/doowruc/WeatherCollector); being built here |

> **Status: setting up.** The code arrives in the next pull requests. Until then, see the two repositories above.

Shared by both (as they move in): a .NET 10 Worker Service under systemd, SQLite archives that keep the data as received, nightly backups pulled off the host, encrypted off-site copies, email alerts and an external heartbeat, and remote access over Tailscale only.

## Licence

[MIT](LICENSE)

---

Designed and built with [Claude Code](https://claude.com/claude-code).
