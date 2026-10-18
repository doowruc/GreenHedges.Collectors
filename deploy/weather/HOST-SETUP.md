# Weather collector: host setup

Adds the Weather collector to a host already prepared with [the Libre collector's HOST-SETUP](../libre/HOST-SETUP.md) sections 0–7 (updates, SSH keys, firewall, unattended upgrades, Tailscale). Run everything on the server unless a step says **on the Mac**. `<your-user>` is your login on the server.

Design and reasoning: [docs/weather/DESIGN.md](../../docs/weather/DESIGN.md).

## 1. rtl_433 and the RTL-SDR tools

```bash
sudo apt install -y rtl-433 rtl-sdr sqlite3
```

Both are in Ubuntu's universe repository; no build needed. The `rtl-sdr` package also installs a udev rule that gives group **`plugdev`** access to the dongle.

## 2. Keep the DVB-T driver off the dongle

Linux treats an RTL-SDR dongle as a DVB-T TV tuner and loads `dvb_usb_rtl28xxu` for it. librtlsdr can usually detach it, but it's more reliable never to load it:

```bash
printf 'blacklist dvb_usb_rtl28xxu\nblacklist rtl2832_sdr\nblacklist rtl2832\n' | sudo tee /etc/modprobe.d/blacklist-rtl-sdr.conf
```

If the dongle is plugged in, the driver is attached to it and can't be unloaded ("Module dvb_usb_rtl28xxu is in use"). Detach it first (or unplug the dongle), then unload it:

```bash
for i in /sys/bus/usb/drivers/dvb_usb_rtl28xxu/*:*; do [ -e "$i" ] && echo -n "${i##*/}" | sudo tee /sys/bus/usb/drivers/dvb_usb_rtl28xxu/unbind; done
```

```bash
sudo modprobe -r dvb_usb_rtl28xxu rtl2832_sdr rtl2832; lsmod | grep rtl28 || echo "DVB-T driver not loaded"
```

The blacklist keeps it off after a replug or reboot.

## 3. Check reception (optional)

As yourself (you're in `plugdev`), for one minute:

```bash
timeout 60 rtl_433 -f 868.3M -s 1024k -R 172 -M level -M protocol -F json
```

Each line is a message. Your station's `id` is the one you'll configure in §5; note its `snr` (above about 10 dB is comfortable). Nothing? Check the dongle is plugged in (`lsusb | grep 2838`) and move it or its antenna; a USB extension lead helps.

## 4. Service account and directories

```bash
sudo useradd --system --home-dir /var/lib/weathercollector --create-home --shell /usr/sbin/nologin weathercollector && sudo chmod 750 /var/lib/weathercollector
```

```bash
sudo install -d -m 0755 /opt/weathercollector && sudo install -d -m 0750 /etc/weathercollector
```

So you can read its logs and database without sudo (takes effect at your next login):

```bash
sudo usermod -aG weathercollector <your-user>
```

The service gets the dongle through `SupplementaryGroups=plugdev` in its unit, so the account itself needs no extra groups.

## 5. Settings that stay off the repository

```bash
sudo install -m 0600 /dev/null /etc/weathercollector/env && sudo nano /etc/weathercollector/env
```

Contents (environment variable form of the config keys; `__` separates sections, `__0` is the first list item):

```
Station__DeviceIds__0=<your station's id>
```

Without it the collector still archives every message, but none counts as the station's; its log names each new device it hears (`New device heard: Bresser-6in1 id …`), so the id can be found there too. After a battery change, if the sensor picks a new id, **add** it as `Station__DeviceIds__1=…` and keep the old one.

Defaults for everything else (frequency, rtl_433 arguments, database path, logging) are in [`appsettings.json`](../../src/Weather/GreenHedges.Collectors.Weather.Worker/appsettings.json); any can be overridden here the same way (e.g. `Rtl433__Arguments=…`).

## 6. Deploy (on the Mac)

```bash
deploy/weather/deploy.sh
```

The host defaults to `COLLECTORS_SSH` (or `LIBRECOLLECTOR_SSH`) in `deploy/local.env`; otherwise pass `<your-user>@<host>`. It publishes a self-contained build, installs it in `/opt/weathercollector` with the systemd unit, starts the service and shows its status and recent log.

## 7. Check it's archiving

```bash
journalctl -u weathercollector -f
```

Expect `Archive ready (schema v1)`, `rtl_433 started`, `New device heard: … (the station)` and `Station heard`, then a summary every 15 minutes. Then:

```bash
sqlite3 -readonly /var/lib/weathercollector/weathercollector.db "SELECT COUNT(*), MAX(received_at_utc) FROM messages; SELECT * FROM devices;"
```

## 8. Alerts and heartbeat

Email alerts go through Resend, as for the Libre collector; the subject starts `[WeatherCollector]`. One PROBLEM email per incident and one RESOLVED email, kept across restarts in `/var/lib/weathercollector/alert-state.json`:

| Alert | Raised when |
|---|---|
| No station messages | Nothing from the station's ids for 5 minutes |
| Receiver problem | No usable RTL-SDR dongle, or 3 rtl_433 restarts in the last hour |
| Station id changed | The station's ids are silent and a new id of the same model is heard (a battery change): the email names it |
| Sensor battery low | `battery_ok` = 0 for an hour |
| Backups failing / Off-site backup failing | No good copy for 36 hours (off-site: straight away if its settings are wrong) |

Thresholds and their defaults are in the `Alerts` section of [`appsettings.json`](../../src/Weather/GreenHedges.Collectors.Weather.Worker/appsettings.json). Details: *Backups and monitoring* in [docs/weather/DESIGN.md](../../docs/weather/DESIGN.md).

**Heartbeat (off-machine alert):** at <https://healthchecks.io>, create a **new** check for this collector (period **5 minutes**, grace **15 minutes**, with your email integration) and keep its ping URL for the env file below. The collector pings it every 5 minutes while the station is being heard, so it alerts if the machine, the collector or the reception stops.

## 9. Backups and off-site copies

The collector takes a nightly backup at 03:30 into `/var/lib/weathercollector/backups/` (gzipped, integrity-checked, 7 kept), and one at startup if none exists from the last day. Each new backup is then encrypted with [`age`](https://github.com/FiloSottile/age) to a public key and uploaded to the shared off-site bucket (from [`deploy/aws/offsite.yaml`](../aws/README.md)) under `weathercollector/`, with the write-only IAM user `<stack>-weather`.

**Access key (AWS console, by you):** **IAM → Users → `<stack>-weather` → Security credentials → Create access key** ("Application running outside AWS"). Save both values in your password manager; they go only into the env file below.

**age key:** the collectors may share one key pair (one private key to keep safe) or have one each. To share the Libre collector's, reuse its public key (`Offsite__AgeRecipient`). For a separate pair, follow step 1 of §8b in [Libre's HOST-SETUP](../libre/HOST-SETUP.md), with your own name for the file and note.

On the server, install `age` (already there if the Libre collector uses off-site copies):

```bash
sudo apt install -y age
```

## 10. Settings for alerts, heartbeat and off-site copies

If the Libre collector runs on the same host, copy its Resend settings and its bucket, region and age public key, so none of them needs retyping:

```bash
sudo grep -E '^(Alerts__|Offsite__(Bucket|Region|AgeRecipient)=)' /etc/librecollector/env | sudo tee -a /etc/weathercollector/env > /dev/null
```

Then edit the file:

```bash
sudo nano /etc/weathercollector/env
```

It should end up with (in addition to `Station__DeviceIds__0`):

```
Alerts__ResendApiKey=re_...
Alerts__From=WeatherCollector <alerts@example.com>
Alerts__To__0=you@example.com
Heartbeat__PingUrl=https://hc-ping.com/<uuid of the new check>
Offsite__Bucket=<bucket>
Offsite__Region=eu-west-2
Offsite__AccessKeyId=AKIA…
Offsite__SecretAccessKey=…
Offsite__AgeRecipient=age1…
```

Change the copied `Alerts__From` name to `WeatherCollector` (same address), and add the heartbeat URL and the `<stack>-weather` access key. Then deploy (§6), or if this version is already installed:

```bash
sudo systemctl restart weathercollector
```

Check the email works (sends one test email and exits; safe while the service runs):

```bash
sudo systemd-run --quiet --wait --pipe --uid=weathercollector -p EnvironmentFile=/etc/weathercollector/env -p WorkingDirectory=/var/lib/weathercollector /opt/weathercollector/GreenHedges.Collectors.Weather.Worker --test-alert
```

Then the log should show `Backup written: /var/lib/weathercollector/backups/weathercollector-….db.gz`, `Off-site backup to s3://<bucket>/weathercollector` and `Off-site copy uploaded: …`, and no `Alerts:ResendApiKey is not set` or `Heartbeat:PingUrl is not set` warnings:

```bash
journalctl -u weathercollector -n 40 --no-pager -o cat
```

The healthchecks.io check turns green within 5 minutes. If an off-site setting is wrong, collection carries on: the log says `Off-site backup is misconfigured and won't run: …` and an "Off-site backup failing" email follows straight away. Fix the env file and restart.

**Restore test** (once now, then occasionally): as in step 5 of §8b in [Libre's HOST-SETUP](../libre/HOST-SETUP.md), with the newest `weathercollector/…db.gz.age` object, then:

```bash
sqlite3 restored.db "PRAGMA integrity_check; SELECT COUNT(*), MAX(received_at_utc) FROM messages;"
```

**Restoring:** `sudo systemctl stop weathercollector`, `gunzip -k` the backup, copy the `.db` over `/var/lib/weathercollector/weathercollector.db` (owner `weathercollector`, mode 0640; remove any `-wal`/`-shm` files next to it), then `sudo systemctl start weathercollector`. Messages received while it was stopped are lost (the sensor doesn't repeat them), so restore only what's broken.

## 11. Pulling backups to the Mac

Same mechanism as the Libre collector (§8a of [its HOST-SETUP](../libre/HOST-SETUP.md)): a daily launchd job on the Mac pulls the backups over SSH with a key the server limits to read-only rsync of this collector's backups folder. Your user must be in the `weathercollector` group (§4), and `/usr/bin/rrsync` present (`rsync` package).

**On the Mac** (Homebrew's `rsync` installed). Your server user and host come from `deploy/local.env` (`COLLECTORS_USER` and `COLLECTORS_HOST`, or `WEATHERCOLLECTOR_…` / `LIBRECOLLECTOR_…`; see [`local.env.example`](../local.env.example)):

```bash
deploy/mac/install-backup-pull.sh weather
```

It creates the key `~/.ssh/weathercollector_backup`, installs `~/.local/bin/weathercollector-pull-backups` and a job that runs daily at 09:10 (or on wake), and prints an `authorized_keys` line. **On the server**, append that line to `~/.ssh/authorized_keys` (one line, next to the Libre collector's):

```bash
nano ~/.ssh/authorized_keys
```

**Heartbeat for the pull:** another healthchecks.io check (period **1 day**, grace **1 day**); save its ping URL on the Mac:

```bash
printf '%s\n' 'https://hc-ping.com/<uuid>' > ~/.config/weathercollector/backup-ping-url && chmod 600 ~/.config/weathercollector/backup-ping-url
```

Test a pull now, from the Mac:

```bash
~/.local/bin/weathercollector-pull-backups
```

Backups land in `~/Backups/WeatherCollector` (30 kept). They grow with the archive (about 1.2 GB a year before compression).
