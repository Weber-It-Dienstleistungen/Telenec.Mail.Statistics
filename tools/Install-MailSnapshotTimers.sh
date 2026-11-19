#!/usr/bin/env bash
# Telenec Mail Statistics – automatische, strikt lesende Snapshot-Erzeugung.
# Installiert zwei eigenstaendige systemd-Oneshot-Dienste plus Timer.
# Startet weder Plesk neu noch veraendert es Postfaecher.
set -euo pipefail

if [ "$(id -u)" -ne 0 ]; then
    echo "FEHLER: Installation nur als root." >&2
    exit 2
fi

SOURCE_DIR="/root"
INSTALL_DIR="/usr/local/libexec/telenec-mail-statistics"
SNAPSHOT_DIR="/var/lib/telenec-mail-statistics"

TRAFFIC="telenec-mail-traffic-collector.py"
SECURITY="telenec-plesk-security-snapshot.py"

for name in "$TRAFFIC" "$SECURITY"; do
    if [ ! -f "$SOURCE_DIR/$name" ]; then
        echo "FEHLER: Quellskript fehlt: $SOURCE_DIR/$name" >&2
        exit 2
    fi
    /usr/bin/python3 -m py_compile "$SOURCE_DIR/$name"
done

if ! grep -q -- '--omit-account-rankings' "$SOURCE_DIR/$TRAFFIC"; then
    echo "FEHLER: Der aktualisierte Traffic-Collector mit Ranking-Schutz fehlt." >&2
    exit 2
fi

if ! id telenecstats >/dev/null 2>&1 ||
   ! getent group telenecstats >/dev/null 2>&1; then
    echo "FEHLER: Dienstbenutzer oder Gruppe telenecstats fehlt." >&2
    exit 2
fi

if [ ! -d "$SNAPSHOT_DIR" ]; then
    echo "FEHLER: Geschuetztes Snapshot-Verzeichnis existiert nicht." >&2
    exit 2
fi

if [ ! -f /var/log/maillog ]; then
    echo "FEHLER: Postfix-Log /var/log/maillog nicht vorhanden." >&2
    exit 2
fi

install -d -o root -g root -m 0755 "$INSTALL_DIR"
install -o root -g root -m 0755 "$SOURCE_DIR/$TRAFFIC" "$INSTALL_DIR/$TRAFFIC"
install -o root -g root -m 0755 "$SOURCE_DIR/$SECURITY" "$INSTALL_DIR/$SECURITY"

cat >/etc/systemd/system/telenec-mail-security-snapshot.service <<'UNIT'
[Unit]
Description=Telenec Mail Security - Plesk-Snapshot erneuern (nur lesend)
Documentation=man:systemd.service(5)
After=local-fs.target

[Service]
Type=oneshot
User=root
Group=root
UMask=0077
WorkingDirectory=/
ExecStart=/usr/bin/python3 /usr/local/libexec/telenec-mail-statistics/telenec-plesk-security-snapshot.py --write --output /var/lib/telenec-mail-statistics/mail-security-preview.json --owner telenecstats
TimeoutStartSec=180
Nice=10
UNIT

cat >/etc/systemd/system/telenec-mail-security-snapshot.timer <<'UNIT'
[Unit]
Description=Telenec Mail Security - Plesk-Snapshot alle 5 Minuten erneuern

[Timer]
OnBootSec=2min
OnUnitInactiveSec=5min
AccuracySec=15s
Unit=telenec-mail-security-snapshot.service

[Install]
WantedBy=timers.target
UNIT

cat >/etc/systemd/system/telenec-mail-traffic-snapshot.service <<'UNIT'
[Unit]
Description=Telenec Mail Statistics - anonymen Mailverkehrs-Snapshot erneuern
Documentation=man:systemd.service(5)
After=local-fs.target

[Service]
Type=oneshot
User=root
Group=root
UMask=0077
WorkingDirectory=/
ExecStart=/usr/bin/python3 /usr/local/libexec/telenec-mail-statistics/telenec-mail-traffic-collector.py --write --output /var/lib/telenec-mail-statistics/mail-traffic-preview.json --group telenecstats --omit-account-rankings
TimeoutStartSec=180
Nice=10
UNIT

cat >/etc/systemd/system/telenec-mail-traffic-snapshot.timer <<'UNIT'
[Unit]
Description=Telenec Mail Statistics - anonymen Traffic-Snapshot alle 5 Minuten erneuern

[Timer]
OnBootSec=2min
OnUnitInactiveSec=5min
AccuracySec=15s
Unit=telenec-mail-traffic-snapshot.service

[Install]
WantedBy=timers.target
UNIT

systemctl daemon-reload

echo "Pruefe beide Collector-Dienste einmalig vor der Timer-Aktivierung ..."
systemctl start telenec-mail-security-snapshot.service
systemctl start telenec-mail-traffic-snapshot.service

echo "Beide Collector-Dienste erfolgreich; schalte automatische Timer ein ..."
systemctl enable --now telenec-mail-security-snapshot.timer
systemctl enable --now telenec-mail-traffic-snapshot.timer

echo ""
echo "=== Telenec Mail Snapshots – Installation abgeschlossen ==="
systemctl list-timers --all --no-pager |
    grep -E 'telenec-mail-(security|traffic)-snapshot' || true
stat -c '%U:%G | Rechte: %a | %n' \
    "$SNAPSHOT_DIR/mail-security-preview.json" \
    "$SNAPSHOT_DIR/mail-traffic-preview.json"
echo "Nur Snapshot-Erneuerung; keine Plesk-Account-Aktionen."
