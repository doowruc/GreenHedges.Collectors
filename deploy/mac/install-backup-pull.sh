#!/usr/bin/env bash
# One-off setup on the Mac for one collector: backup SSH key, the pull script, and a daily launchd job.
#   deploy/mac/install-backup-pull.sh <libre|weather>
# Safe to re-run (updates the script and job, keeps the existing key).
# Reads, from the environment or deploy/local.env, for the collector's prefix P (LIBRECOLLECTOR or
# WEATHERCOLLECTOR): P_USER and P_HOST (else COLLECTORS_USER / COLLECTORS_HOST, else LIBRECOLLECTOR_USER /
# LIBRECOLLECTOR_HOST), and optionally P_BACKUP_DIR and P_LAUNCHD_LABEL. It writes them into the launchd
# job, which is how the scheduled pull gets them.
set -euo pipefail

# Braces make bash read the whole script before running it, so editing the file mid-run can't break a run.
{

REPO="$(cd "$(dirname "$0")/../.." && pwd)"
if [ -f "$REPO/deploy/local.env" ]; then . "$REPO/deploy/local.env"; fi

# Each collector gets its own key (the host limits a key to one backups folder) and its own time.
case "${1:-}" in
  libre)   NAME=librecollector;   TITLE=LibreCollector;   MINUTE=0 ;;
  weather) NAME=weathercollector; TITLE=WeatherCollector; MINUTE=10 ;;
  *) echo "Usage: deploy/mac/install-backup-pull.sh <libre|weather>" >&2; exit 2 ;;
esac
P="$(echo "$NAME" | tr '[:lower:]' '[:upper:]')"
setting() { local v="${P}_$1" c="COLLECTORS_$1" l="LIBRECOLLECTOR_$1"; echo "${!v:-${!c:-${!l:-}}}"; }

LABEL_VAR="${P}_LAUNCHD_LABEL"; LABEL="${!LABEL_VAR:-local.$NAME.pull-backups}"
DEST_VAR="${P}_BACKUP_DIR";     DEST="${!DEST_VAR:-$HOME/Backups/$TITLE}"
REMOTE_USER="$(setting USER)"
HOST="$(setting HOST)"
[ -n "$REMOTE_USER" ] && [ -n "$HOST" ] || {
  echo "Set ${P}_USER and ${P}_HOST (or COLLECTORS_USER and COLLECTORS_HOST) in deploy/local.env or the environment" >&2; exit 2; }

KEY="$HOME/.ssh/${NAME}_backup"
BIN="$HOME/.local/bin/$NAME-pull-backups"
PLIST="$HOME/Library/LaunchAgents/$LABEL.plist"
LOG="$HOME/Library/Logs/$NAME-pull-backups.log"

if [ ! -f "$KEY" ]; then
  # No passphrase: the job runs unattended. The host limits this key to read-only rsync of the backups folder.
  ssh-keygen -q -t ed25519 -N "" -C "$NAME-backup-pull" -f "$KEY"
fi

mkdir -p "$(dirname "$BIN")" "$HOME/Library/LaunchAgents" "$HOME/.config/$NAME"
install -m 0755 "$REPO/deploy/mac/pull-backups.sh" "$BIN"

cat > "$PLIST" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>Label</key><string>$LABEL</string>
  <key>ProgramArguments</key><array><string>$BIN</string></array>
  <key>EnvironmentVariables</key>
  <dict>
    <key>COLLECTOR_NAME</key><string>$NAME</string>
    <key>BACKUP_USER</key><string>$REMOTE_USER</string>
    <key>BACKUP_HOST</key><string>$HOST</string>
    <key>BACKUP_DIR</key><string>$DEST</string>
  </dict>
  <!-- Daily at 09:$(printf '%02d' "$MINUTE"); if the Mac was asleep, launchd runs it once on wake. -->
  <key>StartCalendarInterval</key><dict><key>Hour</key><integer>9</integer><key>Minute</key><integer>$MINUTE</integer></dict>
  <key>StandardOutPath</key><string>$LOG</string>
  <key>StandardErrorPath</key><string>$LOG</string>
</dict>
</plist>
PLIST

launchctl bootout "gui/$(id -u)/$LABEL" 2>/dev/null || true
launchctl bootstrap "gui/$(id -u)" "$PLIST"

echo "Installed $BIN and launchd job $LABEL (pulls $REMOTE_USER@$HOST into $DEST; log: $LOG)."
echo
echo "Add this line to ~/.ssh/authorized_keys for $REMOTE_USER on the host (one line):"
echo
echo "command=\"/usr/bin/rrsync -ro /var/lib/$NAME/backups/\",restrict $(cat "$KEY.pub")"

exit
}
