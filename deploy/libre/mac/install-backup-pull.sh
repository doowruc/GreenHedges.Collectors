#!/usr/bin/env bash
# One-off setup on the Mac: backup SSH key, the pull script, and a daily launchd job.
# Safe to re-run (updates the script and job, keeps the existing key).
# Reads LIBRECOLLECTOR_USER / LIBRECOLLECTOR_HOST (and optionally LIBRECOLLECTOR_BACKUP_DIR,
# LIBRECOLLECTOR_LAUNCHD_LABEL) from the environment or deploy/local.env, and writes them into the
# launchd job, which is how the scheduled pull gets them.
set -euo pipefail

# Braces make bash read the whole script before running it, so editing the file mid-run can't break a run.
{

REPO="$(cd "$(dirname "$0")/../../.." && pwd)"
if [ -f "$REPO/deploy/local.env" ]; then . "$REPO/deploy/local.env"; fi
LABEL="${LIBRECOLLECTOR_LAUNCHD_LABEL:-local.librecollector.pull-backups}"
KEY="$HOME/.ssh/librecollector_backup"
BIN="$HOME/.local/bin/librecollector-pull-backups"
PLIST="$HOME/Library/LaunchAgents/$LABEL.plist"
LOG="$HOME/Library/Logs/librecollector-pull-backups.log"
REMOTE_USER="${LIBRECOLLECTOR_USER:?Set LIBRECOLLECTOR_USER (your user on the server) in deploy/local.env or the environment}"
HOST="${LIBRECOLLECTOR_HOST:-librecollector.lan}"
DEST="${LIBRECOLLECTOR_BACKUP_DIR:-$HOME/Backups/LibreCollector}"

if [ ! -f "$KEY" ]; then
  # No passphrase: the job runs unattended. The host limits this key to read-only rsync of the backups folder.
  ssh-keygen -q -t ed25519 -N "" -C "librecollector-backup-pull" -f "$KEY"
fi

mkdir -p "$(dirname "$BIN")" "$HOME/Library/LaunchAgents" "$HOME/.config/librecollector"
install -m 0755 "$REPO/deploy/libre/mac/pull-backups.sh" "$BIN"

cat > "$PLIST" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>Label</key><string>$LABEL</string>
  <key>ProgramArguments</key><array><string>$BIN</string></array>
  <key>EnvironmentVariables</key>
  <dict>
    <key>LIBRECOLLECTOR_USER</key><string>$REMOTE_USER</string>
    <key>LIBRECOLLECTOR_HOST</key><string>$HOST</string>
    <key>LIBRECOLLECTOR_BACKUP_DIR</key><string>$DEST</string>
  </dict>
  <!-- Daily at 09:00; if the Mac was asleep, launchd runs it once on wake. -->
  <key>StartCalendarInterval</key><dict><key>Hour</key><integer>9</integer><key>Minute</key><integer>0</integer></dict>
  <key>StandardOutPath</key><string>$LOG</string>
  <key>StandardErrorPath</key><string>$LOG</string>
</dict>
</plist>
EOF

launchctl bootout "gui/$(id -u)/$LABEL" 2>/dev/null || true
launchctl bootstrap "gui/$(id -u)" "$PLIST"

echo "Installed $BIN and launchd job $LABEL (pulls $REMOTE_USER@$HOST into $DEST; log: $LOG)."
echo
echo "Add this line to ~/.ssh/authorized_keys for $REMOTE_USER on the host (one line):"
echo
echo "command=\"/usr/bin/rrsync -ro /var/lib/librecollector/backups/\",restrict $(cat "$KEY.pub")"

exit
}
