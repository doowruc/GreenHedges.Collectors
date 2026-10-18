#!/usr/bin/env bash
# Pull one collector's backups from the host to this Mac. Run daily by launchd (see install-backup-pull.sh,
# which installs this as ~/.local/bin/<collector>-pull-backups and sets the variables below in the job).
# Uses a dedicated SSH key that the host restricts to read-only rsync of that collector's backups folder.
set -euo pipefail

# Braces make bash read the whole script before running it, so editing the file mid-run can't break a run.
{

# Jobs installed before the scripts were shared set only the LIBRECOLLECTOR_* variables.
NAME="${COLLECTOR_NAME:-librecollector}"
HOST="${BACKUP_HOST:-${LIBRECOLLECTOR_HOST:?BACKUP_HOST not set; re-run deploy/mac/install-backup-pull.sh}}"
REMOTE_USER="${BACKUP_USER:-${LIBRECOLLECTOR_USER:?BACKUP_USER not set; re-run deploy/mac/install-backup-pull.sh}}"
DEST="${BACKUP_DIR:-${LIBRECOLLECTOR_BACKUP_DIR:?BACKUP_DIR not set; re-run deploy/mac/install-backup-pull.sh}}"
KEY="$HOME/.ssh/${NAME}_backup"
KEEP=30                                                  # the host keeps 7; the Mac keeps a month
PING_URL_FILE="$HOME/.config/$NAME/backup-ping-url"

# macOS's /usr/bin/rsync is openrsync; use Homebrew's rsync 3.x for compatibility with rrsync on the host.
RSYNC=/opt/homebrew/bin/rsync
[ -x "$RSYNC" ] || { echo "Homebrew rsync not found: brew install rsync" >&2; exit 1; }

PING_URL=""
[ -f "$PING_URL_FILE" ] && PING_URL="$(tr -d '[:space:]' < "$PING_URL_FILE")"

ping_hc() { [ -n "$PING_URL" ] && curl -fsS -m 10 --retry 3 -o /dev/null "$PING_URL$1" || true; }
trap 'ping_hc /fail' ERR

echo "$(date '+%Y-%m-%d %H:%M:%S') pulling $NAME backups from $HOST"
mkdir -p "$DEST"

"$RSYNC" -a --ignore-existing --include="$NAME-*.db.gz" --exclude='*' \
  -e "ssh -i $KEY -o IdentitiesOnly=yes -o BatchMode=yes -o ConnectTimeout=20" \
  "$REMOTE_USER@$HOST:./" "$DEST/"

# Keep the newest $KEEP backups (names sort by timestamp).
find "$DEST" -maxdepth 1 -name "$NAME-*.db.gz" | sort -r | tail -n +$((KEEP + 1)) | while read -r old; do
  rm -f "$old" && echo "deleted $old"
done

# Sanity check: the newest copy must be a valid gzip and no more than 2 days old.
newest="$(find "$DEST" -maxdepth 1 -name "$NAME-*.db.gz" | sort -r | head -1)"
[ -n "$newest" ] || { echo "no backups found" >&2; false; }
gzip -t "$newest"
[ -z "$(find "$newest" -mtime +2)" ] || { echo "newest backup is more than 2 days old: $newest" >&2; false; }

echo "ok: newest is $(basename "$newest")"
ping_hc ""

exit
}
