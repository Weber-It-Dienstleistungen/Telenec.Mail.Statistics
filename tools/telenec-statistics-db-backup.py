#!/usr/bin/env python3
"""Telenec Mail Statistics: konsistente, lokale SQLite-Sicherung.

Nur fuer den Linux-Server. Keine Mailbox- oder Plesk-Aktionen.

  --backup        Eine neue, gepruefte Sicherung erstellen
  --status        Sicherungen zaehlen (ohne Datensaetze auszugeben)
  --verify-latest Jüngste Sicherung per SQLite integrity_check pruefen

Der systemd-Dienst laeuft als telenecstats (nicht root).
Die Datensicherung ist nur fuer diesen Account lesbar (0600).
"""

from __future__ import annotations

import argparse
import datetime as dt
import os
import re
import secrets
import shutil
import sqlite3
import stat
import sys
import tempfile
from pathlib import Path

SOURCE = Path("/var/lib/telenec-mail-statistics/telenec-mail-statistics.db")
DESTINATION = Path("/var/backups/telenec-mail-statistics")
RETENTION_DAYS = 14
FILE_PATTERN = re.compile(r"statistics-(\d{8}T\d{6}Z)-([a-f0-9]{8})\.db\Z")
UTC = dt.timezone.utc


class BackupError(Exception):
    pass


def ensure_backup_directory() -> None:
    if DESTINATION.is_symlink() or not DESTINATION.is_dir():
        raise BackupError("Geschuetztes Backup-Verzeichnis fehlt oder ist ein Symlink.")
    info = DESTINATION.stat()
    if info.st_uid != os.geteuid() or stat.S_IMODE(info.st_mode) != 0o700:
        raise BackupError("Backup-Verzeichnis muss dem Dienstnutzer gehoeren und Modus 0700 haben.")


def ensure_source() -> None:
    if SOURCE.is_symlink() or not SOURCE.is_file():
        raise BackupError("Produktive Datenbank fehlt oder ist ein Symlink.")
    info = SOURCE.stat()
    if not stat.S_ISREG(info.st_mode) or info.st_size <= 0:
        raise BackupError("Produktive Datenbank ist ungueltig.")


def open_readonly(database: Path) -> sqlite3.Connection:
    # "mode=ro" verhindert versehentliche Erstellung einer leeren Datenbank.
    return sqlite3.connect(database.as_uri() + "?mode=ro", uri=True, timeout=30)


def verify_database(database: Path) -> None:
    if database.is_symlink() or not database.is_file():
        raise BackupError("Sicherungsdatei fehlt oder ist ein Symlink.")
    connection = open_readonly(database)
    try:
        result = connection.execute("PRAGMA integrity_check").fetchone()
        if result != ("ok",):
            raise BackupError("SQLite meldet einen Integritaetsfehler.")
    finally:
        connection.close()


def backup_files() -> list[Path]:
    files: list[Path] = []
    for item in DESTINATION.iterdir():
        if FILE_PATTERN.fullmatch(item.name):
            if item.is_symlink() or not item.is_file():
                raise BackupError("Unzulaessiger Eintrag im Backup-Verzeichnis.")
            files.append(item)
    return sorted(files, reverse=True)


def prune_backups(now: dt.datetime) -> int:
    """Nur eigene Dateien, erst nach erfolgreicher neuer Sicherung entfernen."""
    cutoff = now - dt.timedelta(days=RETENTION_DAYS)
    removed = 0
    for item in backup_files():
        match = FILE_PATTERN.fullmatch(item.name)
        assert match is not None
        created = dt.datetime.strptime(match.group(1), "%Y%m%dT%H%M%SZ").replace(tzinfo=UTC)
        if created < cutoff:
            item.unlink()
            removed += 1
    return removed


def fsync_directory() -> None:
    fd = os.open(DESTINATION, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW)
    try:
        os.fsync(fd)
    finally:
        os.close(fd)


def run_backup() -> None:
    ensure_backup_directory()
    ensure_source()

    free = shutil.disk_usage(DESTINATION).free
    minimum_free = SOURCE.stat().st_size * 3 + 16 * 1024 * 1024
    if free < minimum_free:
        raise BackupError("Nicht genug freier Speicher fuer eine sichere Datenbankkopie.")

    now = dt.datetime.now(UTC)
    filename = f"statistics-{now:%Y%m%dT%H%M%SZ}-{secrets.token_hex(4)}.db"
    target = DESTINATION / filename
    temp_fd, temp_name = tempfile.mkstemp(prefix=".backup-", suffix=".tmp", dir=DESTINATION)
    temporary = Path(temp_name)

    try:
        os.fchmod(temp_fd, 0o600)
        os.close(temp_fd)
        temp_fd = -1

        # SQLite Online Backup API: konsistent auch bei schreibender Anwendung
        # und vorhandenem WAL. Kein rohes Kopieren der .db-Datei.
        source = open_readonly(SOURCE)
        try:
            output = sqlite3.connect(str(temporary), timeout=30)
            try:
                source.backup(output, pages=256, sleep=0.1)
            finally:
                output.close()
        finally:
            source.close()

        verify_database(temporary)
        if temporary.stat().st_size <= 0:
            raise BackupError("SQLite-Sicherung ist leer.")

        with temporary.open("rb") as stream:
            os.fsync(stream.fileno())

        os.replace(temporary, target)
        fsync_directory()
        removed = prune_backups(now)
        fsync_directory()

        print(f"SICHERUNG OK: {target.name} ({target.stat().st_size} Bytes)")
        print(f"Integritaetspruefung: ok; lokal bereinigt: {removed}; Aufbewahrung: {RETENTION_DAYS} Tage")
        print("Kein Offsite-Backup: externe, unabhaengige Datensicherung separat einrichten.")
    finally:
        if temp_fd >= 0:
            os.close(temp_fd)
        temporary.unlink(missing_ok=True)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    action = parser.add_mutually_exclusive_group(required=True)
    action.add_argument("--backup", action="store_true")
    action.add_argument("--status", action="store_true")
    action.add_argument("--verify-latest", action="store_true")
    args = parser.parse_args()

    try:
        ensure_backup_directory()
        if args.backup:
            run_backup()
        else:
            files = backup_files()
            print(f"Lokale Sicherungen: {len(files)} (Aufbewahrung: {RETENTION_DAYS} Tage)")
            if files:
                latest = files[0]
                print(f"Neueste Sicherung: {latest.name} ({latest.stat().st_size} Bytes)")
                if args.verify_latest:
                    verify_database(latest)
                    print("Integritaetspruefung der neuesten Sicherung: ok")
            elif args.verify_latest:
                raise BackupError("Noch keine Sicherung vorhanden.")
        return 0
    except (BackupError, OSError, sqlite3.Error, ValueError) as error:
        print(f"SICHERUNGSFEHLER: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
