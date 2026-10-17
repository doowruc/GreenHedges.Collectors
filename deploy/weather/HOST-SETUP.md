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

## Later

Backups, off-site copies, alerts and the heartbeat follow, shared with the Libre collector; their setup will be added here.
