#!/usr/bin/env bash
# Publish LibreCollector for Linux and install or update it on the host, then restart the service.
# Run on the dev machine from anywhere in the repo:  deploy/libre/deploy.sh [user@host]
# The host defaults to LIBRECOLLECTOR_SSH from deploy/local.env (see local.env.example).
# The host must be prepared first (deploy/libre/HOST-SETUP.md). You'll be asked for your sudo password on the host.
set -euo pipefail

# Braces make bash read the whole script before running it, so editing the file mid-run can't break a run.
{

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
if [ -f "$ROOT/deploy/local.env" ]; then . "$ROOT/deploy/local.env"; fi
HOST="${1:-${LIBRECOLLECTOR_SSH:-}}"
[ -n "$HOST" ] || { echo "Usage: deploy/libre/deploy.sh user@host (or set LIBRECOLLECTOR_SSH in deploy/local.env)" >&2; exit 2; }
OUT="$ROOT/artifacts/publish/libre/linux-x64"

# One shared SSH connection for every step, so the key is unlocked once per deploy.
CONTROL_DIR="$(mktemp -d /tmp/lc-ssh.XXXXXX)"   # short path: macOS limits socket paths to 104 bytes
SSH_OPTS=(-o ControlMaster=auto -o "ControlPath=$CONTROL_DIR/%C" -o ControlPersist=120)
cleanup() { ssh "${SSH_OPTS[@]}" -O exit "$HOST" 2>/dev/null || true; rm -rf "$CONTROL_DIR"; }
trap cleanup EXIT

echo "==> Publishing (Release, linux-x64, self-contained)"
rm -rf "$OUT"
dotnet publish "$ROOT/src/Libre/GreenHedges.Collectors.Libre.Worker" -c Release -r linux-x64 --self-contained -o "$OUT" -v quiet -nologo
# Development settings are never wanted on the host.
rm -f "$OUT/appsettings.Development.json"

echo "==> Copying to $HOST"
ssh "${SSH_OPTS[@]}" -fN "$HOST"   # open the shared connection (asks for the key passphrase once)
rsync -az --delete -e "ssh ${SSH_OPTS[*]}" "$OUT/" "$HOST:librecollector-release/"
scp -q "${SSH_OPTS[@]}" "$ROOT/deploy/libre/librecollector.service" "$HOST:librecollector-release.service"

echo "==> Installing and restarting on $HOST"
ssh -t "${SSH_OPTS[@]}" "$HOST" 'set -e
  sudo rsync -a --delete --chown=root:root ~/librecollector-release/ /opt/librecollector/
  sudo install -m 0644 -o root -g root ~/librecollector-release.service /etc/systemd/system/librecollector.service
  rm -rf ~/librecollector-release ~/librecollector-release.service
  sudo systemctl daemon-reload
  sudo systemctl enable --quiet librecollector
  sudo systemctl restart librecollector
  sleep 5
  systemctl --no-pager --lines=0 status librecollector || true
  echo
  sudo journalctl -u librecollector -n 15 --no-pager -o cat'

exit
}
