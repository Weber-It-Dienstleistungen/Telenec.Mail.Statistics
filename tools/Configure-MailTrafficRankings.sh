#!/usr/bin/env bash
# Sprint 41: Top-3-Accountlisten fuer den bestehenden Postfix-Snapshot
# explizit zuschalten oder wieder abschalten.
#
# WICHTIG: Die Rankings enthalten E-Mail-Adressen. Nur nach
# Datenschutzfreigabe und nur fuer berechtigte Administratoren
# einschalten. Keine Aenderung an Plesk, Mailkonten oder Security-Timer.
#
# Aufruf:
#   bash Configure-MailTrafficRankings.sh --status
#   sudo bash Configure-MailTrafficRankings.sh --enable
#   sudo bash Configure-MailTrafficRankings.sh --disable
#
# Das bestehende Unit-File bleibt unveraendert. Ein systemd Drop-In
# uebersteuert ausschliesslich ExecStart des Traffic-Collectors.
set -euo pipefail

MODE="${1:-}"
if [[ "$#" -ne 1 || ( "$MODE" != "--enable" && "$MODE" != "--disable" && "$MODE" != "--status" ) ]]; then
    echo "Aufruf: $0 --status | --enable | --disable" >&2
    exit 2
fi

SERVICE="telenec-mail-traffic-snapshot.service"
TIMER="telenec-mail-traffic-snapshot.timer"
BASE_UNIT="/etc/systemd/system/${SERVICE}"
DROP_IN_DIR="/etc/systemd/system/${SERVICE}.d"
DROP_IN="${DROP_IN_DIR}/10-account-rankings.conf"
SNAPSHOT="/var/lib/telenec-mail-statistics/mail-traffic-preview.json"

COLLECTOR="/usr/local/libexec/telenec-mail-statistics/telenec-mail-traffic-collector.py"
BASE_COMMAND="ExecStart=/usr/bin/python3 ${COLLECTOR} --write --output ${SNAPSHOT} --group telenecstats --omit-account-rankings"
RANKING_COMMAND="ExecStart=/usr/bin/python3 ${COLLECTOR} --write --output ${SNAPSHOT} --group telenecstats"
MARKER="# Managed by Configure-MailTrafficRankings.sh (Sprint 41)"

if [[ ! -f "$BASE_UNIT" ]] || ! grep -Fxq -- "$BASE_COMMAND" "$BASE_UNIT"; then
    echo "FEHLER: Die erwartete Traffic-Service-Unit fehlt oder weicht ab. Keine Aenderung." >&2
    exit 2
fi

if [[ "$MODE" == "--status" ]]; then
    if [[ -f "$DROP_IN" ]] && grep -Fxq -- "$MARKER" "$DROP_IN"; then
        echo "Top-3-Rankings: eingeschaltet (via systemd Drop-In)"
    elif [[ -e "$DROP_IN" ]]; then
        echo "Top-3-Rankings: unbekannte manuelle Override-Konfiguration vorhanden"
        exit 2
    else
        echo "Top-3-Rankings: ausgeschaltet (bestehende Unit enthaelt --omit-account-rankings)"
    fi
    echo "Timer: $(systemctl is-active "$TIMER" || true)"
    if [[ -f "$SNAPSHOT" ]]; then
        /usr/bin/python3 - "$SNAPSHOT" <<'PY'
import json
import sys
with open(sys.argv[1], encoding="utf-8") as stream:
    snapshot = json.load(stream)
for key in ("topSenders", "topRecipients"):
    rows = snapshot.get(key)
    if not isinstance(rows, list) or len(rows) > 3:
        raise SystemExit("Snapshot enthaelt keine gueltige Ranking-Liste: " + key)
    print(key + ": " + str(len(rows)) + " Eintraege (keine Kontodaten in Konsolenausgabe)")
PY
    fi
    exit 0
fi

if [[ "$(id -u)" -ne 0 ]]; then
    echo "FEHLER: --enable und --disable nur als root." >&2
    exit 2
fi

if [[ -L "$DROP_IN_DIR" || -L "$DROP_IN" ]]; then
    echo "FEHLER: Drop-In ist ein Symlink. Keine Aenderung." >&2
    exit 2
fi
if [[ -e "$DROP_IN" ]] && ! grep -Fxq -- "$MARKER" "$DROP_IN"; then
    echo "FEHLER: Fremde Drop-In-Datei wird nicht ueberschrieben." >&2
    exit 2
fi

if [[ "$MODE" == "--enable" ]]; then
    if [[ -f "$DROP_IN" ]]; then
        echo "Top-3-Rankings bereits eingeschaltet."
        exit 0
    fi
    install -d -o root -g root -m 0755 "$DROP_IN_DIR"
    TEMP="$(mktemp "${DROP_IN_DIR}/.rankings.XXXXXX")"
    trap 'rm -f -- "$TEMP"' EXIT
    cat >"$TEMP" <<EOF
$MARKER
[Unit]
Description=Telenec Mail Statistics - Mailverkehrs-Snapshot mit Top-3-Accountlisten

[Service]
ExecStart=
$RANKING_COMMAND
EOF
    chown root:root "$TEMP"
    chmod 0644 "$TEMP"
    mv -- "$TEMP" "$DROP_IN"
    systemctl daemon-reload
    if ! systemctl restart "$SERVICE"; then
        echo "FEHLER: Collector nach Aktivierung fehlgeschlagen; Drop-In wird zurueckgenommen." >&2
        rm -f -- "$DROP_IN"
        systemctl daemon-reload
        exit 1
    fi
    echo "Top-3-Rankings eingeschaltet; Collector erfolgreich erneuert."
    echo "Der vorhandene Timer bleibt unveraendert: $(systemctl is-active "$TIMER" || true)"
    echo "Die Rankings werden ausschliesslich im geschuetzten Snapshot und der Admin-API bereitgestellt."
    exit 0
fi

# --disable: Vorherige anonymisierte Einheit wieder herstellen.
if [[ ! -f "$DROP_IN" ]]; then
    echo "Top-3-Rankings bereits ausgeschaltet."
    exit 0
fi
rm -- "$DROP_IN"
systemctl daemon-reload
if ! systemctl restart "$SERVICE"; then
    echo "FEHLER: Collector nach Abschaltung fehlgeschlagen." >&2
    echo "ACHTUNG: Aeltere Snapshot-Datei kann noch personenbezogene Daten enthalten." >&2
    exit 1
fi
echo "Top-3-Rankings ausgeschaltet; Snapshot ohne Accountlisten erneuert."
echo "Timer: $(systemctl is-active "$TIMER" || true)"
