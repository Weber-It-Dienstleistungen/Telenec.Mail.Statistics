#!/usr/bin/env bash
# Sprint 42: Zeitgesteuerte lokale SQLite-Sicherung einrichten.
#
# Erst Installation, dann bewusst:
#   systemctl start telenec-mail-statistics-db-backup.service
#   systemctl enable --now telenec-mail-statistics-db-backup.timer
#
# Keine Aenderung am Mailserver, Statistikdienst oder Updater.
set -euo pipefail

if [ "$(id -u)" -ne 0 ]; then
    echo "FEHLER: Dieses Installationsskript muss als root laufen." >&2
    exit 2
fi

SOURCE="/root/telenec-statistics-db-backup.py"
SCRIPT="/usr/local/libexec/telenec-mail-statistics/telenec-statistics-db-backup.py"
BACKUP_DIR="/var/backups/telenec-mail-statistics"
DB="/var/lib/telenec-mail-statistics/telenec-mail-statistics.db"
SERVICE="/etc/systemd/system/telenec-mail-statistics-db-backup.service"
TIMER="/etc/systemd/system/telenec-mail-statistics-db-backup.timer"
MARKER="# Managed by Install-StatisticsDatabaseBackup.sh (Sprint 42)"

if [ ! -f "$SOURCE" ] || [ -L "$SOURCE" ]; then
    echo "FEHLER: Quellskript fehlt oder ist ein Symlink: $SOURCE" >&2
    exit 2
fi
/usr/bin/python3 -m py_compile "$SOURCE"

if ! id telenecstats >/dev/null 2>&1 || ! getent group telenecstats >/dev/null; then
    echo "FEHLER: Dienstbenutzer telenecstats fehlt." >&2
    exit 2
fi
if [ ! -f "$DB" ] || [ -L "$DB" ]; then
    echo "FEHLER: Produktive SQLite-Datenbank fehlt oder ist ein Symlink." >&2
    exit 2
fi
if [ -e "$BACKUP_DIR" ] && [ ! -d "$BACKUP_DIR" ]; then
    echo "FEHLER: Backup-Ziel ist kein Verzeichnis." >&2
    exit 2
fi
if [ -L "$BACKUP_DIR" ]; then
    echo "FEHLER: Backup-Ziel darf kein Symlink sein." >&2
    exit 2
fi

for file in "$SERVICE" "$TIMER"; do
    if [ -e "$file" ] && ! grep -Fxq -- "$MARKER" "$file"; then
        echo "FEHLER: Vorhandene, nicht von diesem Skript verwaltete Unit: $file" >&2
        exit 2
    fi
done

# Backups liegen getrennt von der Produktivdatenbank und sind
# ausschliesslich fuer den Backup-Dienstnutzer zugreifbar.
install -d -o telenecstats -g telenecstats -m 0700 "$BACKUP_DIR"
install -d -o root -g root -m 0755 /usr/local/libexec/telenec-mail-statistics
install -o root -g root -m 0755 "$SOURCE" "$SCRIPT"

cat >"$SERVICE" <<EOF
$MARKER
[Unit]
Description=Telenec Mail Statistics - konsistente taegliche SQLite-Datensicherung
Documentation=man:systemd.service(5)
ConditionPathExists=$SCRIPT
ConditionPathExists=$DB

[Service]
Type=oneshot
User=telenecstats
Group=telenecstats
WorkingDirectory=/
UMask=0077
Environment=PYTHONDONTWRITEBYTECODE=1
ExecStart=/usr/bin/python3 $SCRIPT --backup
TimeoutStartSec=5min
Nice=10
NoNewPrivileges=true
PrivateTmp=true
ProtectSystem=strict
ProtectHome=true
# Die SQLite-Live-Datenbank kann WAL/-shm-Sidecars benutzen, die auch
# bei einer nur lesenden Online-Backup-Verbindung Schreibzugriff auf
# das VERZEICHNIS benoetigen. Die eigentliche DB bleibt separat ro.
# Nur die beiden expliziten Verzeichnisse sind im sonst ro-Dateisystem
# beschreibbar. Der Backup-Worker oeffnet SOURCE stets mit mode=ro.
ReadWritePaths=/var/lib/telenec-mail-statistics $BACKUP_DIR
ReadOnlyPaths=$DB
ProtectKernelTunables=true
ProtectKernelModules=true
ProtectControlGroups=true
LockPersonality=true
RestrictSUIDSGID=true
SystemCallArchitectures=native
RestrictAddressFamilies=AF_UNIX
StandardOutput=journal
StandardError=journal
EOF

cat >"$TIMER" <<EOF
$MARKER
[Unit]
Description=Telenec Mail Statistics - taegliches SQLite-Backup
Documentation=man:systemd.timer(5)

[Timer]
OnCalendar=*-*-* 02:30:00 UTC
AccuracySec=1min
RandomizedDelaySec=5min
Persistent=true
Unit=telenec-mail-statistics-db-backup.service

[Install]
WantedBy=timers.target
EOF
chmod 0644 "$SERVICE" "$TIMER"
chown root:root "$SERVICE" "$TIMER"
systemctl daemon-reload

echo ""
echo "=== Backup-Infrastruktur vorbereitet ==="
stat -c '%U:%G | %a | %n' "$BACKUP_DIR" "$SCRIPT" "$SERVICE" "$TIMER"
echo ""
echo "Backup-Timer wurde NICHT aktiviert."
echo "Naechster Schritt (manueller Test):"
echo "  systemctl start telenec-mail-statistics-db-backup.service"
echo "  systemctl status telenec-mail-statistics-db-backup.service --no-pager"
echo ""
echo "Nach erfolgreichem Test und Integritaetskontrolle:"
echo "  systemctl enable --now telenec-mail-statistics-db-backup.timer"
echo ""
echo "ACHTUNG: Lokale Backups sind kein Offsite-Backup."
