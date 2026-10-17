#!/usr/bin/env bash
# Publish a collector for Linux and install or update it on the host, then restart its service.
# Run on the dev machine from anywhere in the repo:  deploy/deploy.sh <libre|weather> [user@host]
# (deploy/libre/deploy.sh and deploy/weather/deploy.sh do the same for their collector.)
# The host defaults to COLLECTORS_SSH (or LIBRECOLLECTOR_SSH) from deploy/local.env (see local.env.example).
# The host must be prepared first (deploy/<collector>/HOST-SETUP.md). You'll be asked for your sudo password on the host.
set -euo pipefail

# Braces make bash read the whole script before running it, so editing the file mid-run can't break a run.
{

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
if [ -f "$ROOT/deploy/local.env" ]; then . "$ROOT/deploy/local.env"; fi

case "${1:-}" in
  libre)   PROJECT="src/Libre/GreenHedges.Collectors.Libre.Worker";     SERVICE=librecollector ;;
  weather) PROJECT="src/Weather/GreenHedges.Collectors.Weather.Worker"; SERVICE=weathercollector ;;
  *) echo "Usage: deploy/deploy.sh <libre|weather> [user@host]" >&2; exit 2 ;;
esac
COLLECTOR="$1"
UNIT="$ROOT/deploy/$COLLECTOR/$SERVICE.service"
HOST="${2:-${COLLECTORS_SSH:-${LIBRECOLLECTOR_SSH:-}}}"
case "$HOST" in
  ""|-*) echo "Usage: deploy/deploy.sh $COLLECTOR [user@host] (or set COLLECTORS_SSH in deploy/local.env)" >&2; exit 2 ;;
esac
OUT="$ROOT/artifacts/publish/$COLLECTOR/linux-x64"

# One shared SSH connection for every step, so the key is unlocked once per deploy.
CONTROL_DIR="$(mktemp -d /tmp/lc-ssh.XXXXXX)"   # short path: macOS limits socket paths to 104 bytes
SSH_OPTS=(-o ControlMaster=auto -o "ControlPath=$CONTROL_DIR/%C" -o ControlPersist=120)
cleanup() { ssh "${SSH_OPTS[@]}" -O exit "$HOST" 2>/dev/null || true; rm -rf "$CONTROL_DIR"; }
trap cleanup EXIT

echo "==> Publishing $SERVICE (Release, linux-x64, self-contained)"
rm -rf "$OUT"
dotnet publish "$ROOT/$PROJECT" -c Release -r linux-x64 --self-contained -o "$OUT" -v quiet -nologo
# Development settings are never wanted on the host.
rm -f "$OUT/appsettings.Development.json"

echo "==> Copying to $HOST"
ssh "${SSH_OPTS[@]}" -fN "$HOST"   # open the shared connection (asks for the key passphrase once)
rsync -az --delete -e "ssh ${SSH_OPTS[*]}" "$OUT/" "$HOST:$SERVICE-release/"
scp -q "${SSH_OPTS[@]}" "$UNIT" "$HOST:$SERVICE-release.service"

echo "==> Installing and restarting $SERVICE on $HOST"
ssh -t "${SSH_OPTS[@]}" "$HOST" "set -e
  sudo rsync -a --delete --chown=root:root ~/$SERVICE-release/ /opt/$SERVICE/
  sudo install -m 0644 -o root -g root ~/$SERVICE-release.service /etc/systemd/system/$SERVICE.service
  rm -rf ~/$SERVICE-release ~/$SERVICE-release.service
  sudo systemctl daemon-reload
  sudo systemctl enable --quiet $SERVICE
  sudo systemctl restart $SERVICE
  sleep 5
  systemctl --no-pager --lines=0 status $SERVICE || true
  echo
  sudo journalctl -u $SERVICE -n 15 --no-pager -o cat"

exit
}
