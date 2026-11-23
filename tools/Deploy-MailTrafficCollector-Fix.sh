#!/usr/bin/env bash
# Issue #2 – kontrollierter Austausch NUR des Mail-Traffic-Collectors.
# Keine Mailserver-, Postfix-, Plesk-, Datenbank- oder API-Aenderung.
set -euo pipefail
umask 077

if [[ "$#" -ne 1 || ( "$1" != "--check" && "$1" != "--apply" ) ]]; then
    echo "Aufruf: bash $0 --check | --apply" >&2
    exit 2
fi
MODE="$1"
if [[ "$(id -u)" -ne 0 ]]; then
    echo "FEHLER: Nur als root ausfuehren." >&2
    exit 2
fi

SOURCE="/root/telenec-traffic-test/tools/telenec-mail-traffic-collector.py"
TEST_DIR="/root/telenec-traffic-test/tools/tests"
DEST="/usr/local/libexec/telenec-mail-statistics/telenec-mail-traffic-collector.py"
SERVICE="telenec-mail-traffic-snapshot.service"
TIMER="telenec-mail-traffic-snapshot.timer"
SNAPSHOT="/var/lib/telenec-mail-statistics/mail-traffic-preview.json"
BACKUP_DIR="/root/telenec-traffic-test/backups"

for file in "$SOURCE" "$DEST" "$TEST_DIR/test_mail_traffic_collector.py"; do
    if [[ ! -f "$file" || -L "$file" ]]; then
        echo "FEHLER: Datei fehlt oder ist Symlink: $file" >&2
        exit 2
    fi
done

if ! systemctl show "$SERVICE" -p ExecStart --value |
    grep -Fq -- "$DEST"; then
    echo "FEHLER: Systemd-Service nutzt unerwarteten Collector-Pfad." >&2
    exit 2
fi

if ! grep -Fq -- 'strict_data_aborted_auth' "$SOURCE" ||
   ! grep -Fq -- 'if t > accepted_at and kind == "connect"' "$SOURCE"; then
    echo "FEHLER: Reparaturkandidat ist nicht die erwartete Version." >&2
    exit 2
fi

/usr/bin/python3 -m py_compile "$SOURCE"
/usr/bin/python3 -m unittest discover \
    -s "$TEST_DIR" -p test_mail_traffic_collector.py -v
echo "Synthetische Regressionstests erfolgreich."

if [[ "$MODE" == "--check" ]]; then
    echo "VORPRUEFUNG OK. Produktive Datei und Timer unveraendert."
    exit 0
fi

install -d -o root -g root -m 0700 "$BACKUP_DIR"
BACKUP="$BACKUP_DIR/telenec-mail-traffic-collector-$(date -u +%Y%m%dT%H%M%SZ)-$$.py"
cp -p -- "$DEST" "$BACKUP"
chmod 0600 "$BACKUP"
echo "Vorherige Version gesichert: $BACKUP"

timer_was_active=0
installed=0
completed=0
temp=""

cleanup() {
    local rc=$?
    trap - EXIT
    if [[ -n "$temp" && -e "$temp" ]]; then
        rm -f -- "$temp" || true
    fi
    if [[ "$completed" != 1 && "$installed" == 1 ]]; then
        echo "FEHLER: Abnahme fehlgeschlagen. Stelle vorherigen Collector wieder her." >&2
        local restore
        restore="$(mktemp "$DEST.rollback.XXXXXX")"
        install -o root -g root -m 0755 "$BACKUP" "$restore"
        mv -f -- "$restore" "$DEST"
        # Die alte Version kann denselben bereits bekannten Fehler haben.
        systemctl reset-failed "$SERVICE" || true
    fi
    if [[ "$timer_was_active" == 1 ]]; then
        systemctl start "$TIMER" ||
            echo "WARNUNG: Timer konnte nicht reaktiviert werden!" >&2
    fi
    exit "$rc"
}
trap cleanup EXIT

if systemctl is-active --quiet "$TIMER"; then
    timer_was_active=1
    systemctl stop "$TIMER"
fi

temp="$(mktemp "$DEST.next.XXXXXX")"
install -o root -g root -m 0755 "$SOURCE" "$temp"
mv -f -- "$temp" "$DEST"
temp=""
installed=1

echo "Starte NUR den Mail-Traffic-Snapshot-Dienst zur Abnahme ..."
systemctl reset-failed "$SERVICE"
systemctl restart "$SERVICE"

# Nur ein frischer, gueltiger Snapshot darf den Deploy abschliessen.
python3 - "$SNAPSHOT" <<'PY'
import datetime as dt
import json
from pathlib import Path
import sys

path = Path(sys.argv[1])
with path.open("r", encoding="utf-8") as stream:
    obj = json.load(stream)
generated = dt.datetime.fromisoformat(obj["generatedAtUtc"].replace("Z", "+00:00"))
age = (dt.datetime.now(dt.timezone.utc) - generated).total_seconds()
if not -120 <= age <= 300:
    raise SystemExit("FEHLER: Kein frischer Mailverkehrs-Snapshot.")
if not isinstance(obj.get("hours"), list) or len(obj["hours"]) != 24:
    raise SystemExit("FEHLER: Ungueltige Stundenliste im Snapshot.")
for field in ("topSenders", "topRecipients"):
    if not isinstance(obj.get(field), list) or len(obj[field]) > 3:
        raise SystemExit("FEHLER: Ungueltige Rankings im Snapshot.")
print("ABNAHME OK: Frischer 24h-Snapshot. Rankingschema gueltig.")
print("Datenstand UTC:", generated.isoformat())
print("Ranking-Anzahl:", len(obj["topSenders"]), "/", len(obj["topRecipients"]))
PY

completed=1
if [[ "$timer_was_active" == 1 ]]; then
    systemctl start "$TIMER"
    timer_was_active=0
fi
echo "Collector erfolgreich aktualisiert. Unit-Dateien unveraendert."
echo "Status Timer: $(systemctl is-active "$TIMER" || true)"
echo "Backup der alten Version: $BACKUP"
