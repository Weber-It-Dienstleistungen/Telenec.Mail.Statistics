
#!/usr/bin/env python3
"""Signierte Statistikserver-Releases prüfen, vorbereiten und installieren.

Standard: nur prüfen. --prepare: Paket isoliert testen. --apply: neues Release
mit SQLite-Sicherung, Gesundheitsprüfung und automatischem Rollback installieren.
Die Datei wird von einem root-eigenen systemd-Dienst ausgeführt.
"""

from __future__ import annotations

import argparse
import fcntl
import hashlib
import json
import os
import re
import signal
import shutil
import sqlite3
import stat
import subprocess
import sys
import tempfile
import time
import urllib.error
import urllib.request
import zipfile
from contextlib import contextmanager
from pathlib import Path


UPDATES = Path('/var/www/vhosts/necnet.de/dav.necnet.de/updates/statistics-server')
SIGNERS = Path('/etc/telenec-mail-statistics-updater/allowed_signers')
STATE = Path('/var/lib/telenec-mail-statistics-updater')
APP = Path('/opt/telenec-mail-statistics')
DB = Path('/var/lib/telenec-mail-statistics/telenec-mail-statistics.db')
SERVICE = 'telenec-mail-statistics.service'
HEALTH_URL = 'http://127.0.0.1:5097/'
IDENTITY = 'telenec-statistics-release'
PRODUCT = 'Telenec.Mail.Statistics.Server'

VERS = re.compile(r'(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\Z')
SHA = re.compile(r'[0-9a-f]{64}\Z')
TXID = re.compile(r'[0-9a-f]{24}\Z')
MAX_ZIP = 256 * 1024 * 1024
MAX_UNPACKED = 1024 * 1024 * 1024


class UpdateError(Exception):
    pass


class UpdateInterrupted(Exception):
    pass


def version(value):
    if not isinstance(value, str) or VERS.fullmatch(value) is None:
        raise UpdateError(f'Ungueltige Version: {value!r}')
    return tuple(map(int, value.split('.')))


def private_state():
    if STATE.is_symlink() or not STATE.is_dir():
        raise UpdateError(f'Geschuetztes Verzeichnis fehlt: {STATE}')
    s = STATE.stat()
    if s.st_uid != 0 or stat.S_IMODE(s.st_mode) != 0o700:
        raise UpdateError(f'Verzeichnis muss root:root, 700 sein: {STATE}')


def read_regular(path, maximum):
    try:
        fd = os.open(path, os.O_RDONLY | os.O_NOFOLLOW)
        with os.fdopen(fd, 'rb') as stream:
            info = os.fstat(stream.fileno())
            if not stat.S_ISREG(info.st_mode) or info.st_size > maximum:
                raise UpdateError(f'Ungueltige Datei: {path.name}')
            result = stream.read(maximum + 1)
    except OSError as exc:
        raise UpdateError(f'Datei nicht lesbar: {path}: {exc}') from exc
    if len(result) > maximum:
        raise UpdateError(f'Datei zu gross: {path.name}')
    return result


def copy_regular(source, dest, limit):
    """Datei ohne Symlink-Folge in privaten Bereich kopieren und hashen."""
    digest = hashlib.sha256()
    length = 0
    try:
        with os.fdopen(os.open(source, os.O_RDONLY | os.O_NOFOLLOW), 'rb') as inp:
            if not stat.S_ISREG(os.fstat(inp.fileno()).st_mode):
                raise UpdateError(f'Keine regulaere Datei: {source.name}')
            with dest.open('xb') as out:
                os.chmod(dest, 0o600)
                while chunk := inp.read(1024 * 1024):
                    length += len(chunk)
                    if length > limit:
                        raise UpdateError(f'Datei zu gross: {source.name}')
                    digest.update(chunk)
                    out.write(chunk)
                out.flush()
                os.fsync(out.fileno())
    except OSError as exc:
        raise UpdateError(f'Kopieren fehlgeschlagen: {source.name}: {exc}') from exc
    return length, digest.hexdigest()


def no_duplicate_keys(pairs):
    result = {}
    for key, val in pairs:
        if key in result:
            raise UpdateError(f'Doppelter JSON-Schluessel: {key}')
        result[key] = val
    return result


def parse_manifest(data):
    try:
        manifest = json.loads(data, object_pairs_hook=no_duplicate_keys)
    except (ValueError, UnicodeError) as exc:
        raise UpdateError('Ungueltiges Manifest-JSON') from exc
    if not isinstance(manifest, dict):
        raise UpdateError('Manifest muss ein Objekt sein')
    if type(manifest.get('schemaVersion')) is not int or manifest['schemaVersion'] != 1:
        raise UpdateError('Unbekannte Manifestversion')
    if manifest.get('product') != PRODUCT or manifest.get('runtimeIdentifier') != 'linux-x64':
        raise UpdateError('Falsches Produkt oder Zielsystem')
    v = manifest.get('version')
    version(v)
    expected = f'telenec-mail-statistics-server-linux-x64-{v}.zip'
    if manifest.get('package') != expected:
        raise UpdateError('Ungueltiger Paketname')
    if type(manifest.get('sizeBytes')) is not int or not 0 < manifest['sizeBytes'] <= MAX_ZIP:
        raise UpdateError('Ungueltige ZIP-Groesse')
    if not isinstance(manifest.get('sha256'), str) or not SHA.fullmatch(manifest['sha256']):
        raise UpdateError('Ungueltiger SHA256-Wert')
    return manifest


def verify_signature(manifest_path, signature_path):
    try:
        proc = subprocess.run(
            ['/usr/bin/ssh-keygen', '-Y', 'verify', '-f', str(SIGNERS),
             '-I', IDENTITY, '-n', IDENTITY, '-s', str(signature_path)],
            input=read_regular(manifest_path, 16384), capture_output=True,
            check=False, timeout=15)
    except (OSError, subprocess.TimeoutExpired) as exc:
        raise UpdateError('Signaturpruefung nicht moeglich') from exc
    if proc.returncode != 0:
        raise UpdateError('Signatur ungueltig oder Schluessel nicht vertrauenswuerdig')


def hash_file(path, limit):
    digest = hashlib.sha256()
    length = 0
    try:
        with os.fdopen(os.open(path, os.O_RDONLY | os.O_NOFOLLOW), 'rb') as stream:
            if not stat.S_ISREG(os.fstat(stream.fileno()).st_mode):
                raise UpdateError('Paket ist keine regulaere Datei')
            while block := stream.read(1024 * 1024):
                length += len(block)
                if length > limit:
                    raise UpdateError('Paket zu gross')
                digest.update(block)
    except OSError as exc:
        raise UpdateError(f'Paket nicht lesbar: {exc}') from exc
    return length, digest.hexdigest()


def read_version():
    return read_regular(STATE / 'installed-version', 100).decode('ascii').strip()


def sync_directory(path):
    fd = os.open(path, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW)
    try:
        os.fsync(fd)
    finally:
        os.close(fd)


def atomic_json(path, value):
    atomic_bytes(path, (json.dumps(value, sort_keys=True) + '\n').encode('utf-8'))


def atomic_bytes(path, data):
    fd, temp = tempfile.mkstemp(prefix='.writing-', dir=path.parent)
    try:
        with os.fdopen(fd, 'wb') as stream:
            os.fchmod(stream.fileno(), 0o600)
            stream.write(data)
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temp, path)
        sync_directory(path.parent)
    finally:
        if os.path.exists(temp):
            os.unlink(temp)


@contextmanager
def update_lock():
    private_state()
    fd = os.open(STATE / 'updater.lock', os.O_RDWR | os.O_CREAT | os.O_NOFOLLOW, 0o600)
    try:
        if not stat.S_ISREG(os.fstat(fd).st_mode):
            raise UpdateError('Ungueltige Lockdatei')
        fcntl.flock(fd, fcntl.LOCK_EX | fcntl.LOCK_NB)
        yield
    except BlockingIOError as exc:
        raise UpdateError('Ein anderer Updater laeuft bereits') from exc
    finally:
        os.close(fd)


def snapshot_release(directory):
    """Ausschliesslich signierte, zusammenpassende Kopien nutzen."""
    for filename in ('release.json', 'release.json.sig'):
        copy_regular(UPDATES / filename, directory / filename, 16384)
    verify_signature(directory / 'release.json', directory / 'release.json.sig')
    manifest = parse_manifest(read_regular(directory / 'release.json', 16384))
    size, digest = copy_regular(UPDATES / manifest['package'], directory / manifest['package'], MAX_ZIP)
    if size != manifest['sizeBytes'] or digest != manifest['sha256']:
        raise UpdateError('ZIP weicht vom signierten Manifest ab')
    return manifest, directory / manifest['package']


def unpack(package, destination):
    """Nur regulaere ZIP-Eintraege, keine Symlinks oder Pfadtraversierung."""
    destination.mkdir(mode=0o700)
    total = 0
    seen = set()
    files = 0
    try:
        with zipfile.ZipFile(package) as archive:
            entries = archive.infolist()
            if len(entries) > 2000:
                raise UpdateError('Zu viele ZIP-Eintraege')
            for entry in entries:
                path = entry.filename
                parts = path.rstrip('/').split('/')
                if (not path or path.startswith('/') or '\\' in path or '\x00' in path
                    or ':' in parts[0] or any(x in ('', '.', '..') for x in parts)):
                    raise UpdateError(f'Ungueltiger ZIP-Pfad: {path!r}')
                normalized = '/'.join(parts).casefold()
                if normalized in seen:
                    raise UpdateError(f'Doppelter ZIP-Eintrag: {path}')
                seen.add(normalized)
                mode = stat.S_IFMT(entry.external_attr >> 16)
                if mode not in (0, stat.S_IFDIR if entry.is_dir() else stat.S_IFREG):
                    raise UpdateError(f'Unsicherer ZIP-Dateityp: {path}')
                if entry.flag_bits & 1:
                    raise UpdateError('Verschluesselter ZIP-Eintrag')
                total += entry.file_size
                if entry.file_size > MAX_ZIP or total > MAX_UNPACKED:
                    raise UpdateError('ZIP entpackt zu gross')
                target = destination.joinpath(*parts)
                if entry.is_dir():
                    target.mkdir(parents=True, exist_ok=True)
                    target.chmod(0o755)
                else:
                    target.parent.mkdir(parents=True, exist_ok=True)
                    with archive.open(entry) as src, target.open('xb') as dst:
                        count = 0
                        while chunk := src.read(1024 * 1024):
                            count += len(chunk)
                            if count > entry.file_size:
                                raise UpdateError('ZIP enthaelt mehr Daten als angegeben')
                            dst.write(chunk)
                    if count != entry.file_size:
                        raise UpdateError('ZIP-Groesse stimmt nicht')
                    target.chmod(0o644)
                    files += 1
    except (zipfile.BadZipFile, OSError, RuntimeError, EOFError) as exc:
        raise UpdateError(f'ZIP konnte nicht sicher entpackt werden: {exc}') from exc
    required = ('Telenec.Mail.Statistics.Server.dll',
                'Telenec.Mail.Statistics.Server.runtimeconfig.json')
    if any(not (destination / name).is_file() for name in required):
        raise UpdateError('Zwingende Serverdateien fehlen im ZIP')
    destination.chmod(0o755)
    return files


def invoke_systemctl(action):
    proc = subprocess.run(['/usr/bin/systemctl', action, SERVICE],
                          capture_output=True, text=True, timeout=60)
    if proc.returncode:
        raise UpdateError(f'systemctl {action} fehlgeschlagen: {proc.stderr.strip()}')


def service_active():
    proc = subprocess.run(['/usr/bin/systemctl', 'is-active', '--quiet', SERVICE],
                          capture_output=True, timeout=10)
    return proc.returncode == 0


def check_db(path=None):
    path = DB if path is None else path
    source = sqlite3.connect(f'{path.as_uri()}?mode=ro', uri=True, timeout=5)
    try:
        status = source.execute('PRAGMA quick_check').fetchone()
        if not status or status[0] != 'ok':
            raise UpdateError(f'SQLite quick_check meldet {status}')
    finally:
        source.close()


def health_check(timeout=60):
    deadline = time.monotonic() + timeout
    last = 'noch keine Antwort'
    while time.monotonic() < deadline:
        try:
            if not service_active():
                raise UpdateError('Dienst noch nicht aktiv')
            with urllib.request.urlopen(HEALTH_URL, timeout=3) as response:
                body = response.read(100).decode('utf-8').strip()
                if response.status != 200 or body != 'Telenec Mail Statistics Server':
                    raise UpdateError(f'Falsche HTTP-Antwort: {response.status}, {body!r}')
            check_db()
            return
        except (OSError, sqlite3.Error, UnicodeError, UpdateError,
                urllib.error.URLError) as exc:
            last = str(exc)
            time.sleep(2)
    raise UpdateError(f'Gesundheitscheck fehlgeschlagen: {last}')


def check_space(package):
    """Vor Dienststopp ausreichend Raum fuer Stage, App- und DB-Sicherung pruefen."""
    try:
        with zipfile.ZipFile(package) as archive:
            unpacked = sum(info.file_size for info in archive.infolist())
    except (zipfile.BadZipFile, OSError) as exc:
        raise UpdateError('ZIP kann nicht fuer Speicherplatzpruefung gelesen werden') from exc
    app_bytes = sum(Path(root, name).stat().st_size
                    for root, _, files in os.walk(APP)
                    for name in files)
    db_bytes = DB.stat().st_size
    mib = 1024 * 1024
    requirements = (
        (APP.parent, 2 * app_bytes + unpacked + 64 * mib),
        (STATE, 2 * db_bytes + 64 * mib),
        (DB.parent, 2 * db_bytes + 32 * mib),
    )
    for path, needed in requirements:
        free = shutil.disk_usage(path).free
        if free < needed:
            raise UpdateError(f'Zu wenig freier Speicherplatz unter {path}: '
                              f'{free // mib} MiB frei, {needed // mib} MiB benoetigt')


def backup_db(destination):
    if DB.is_symlink() or not DB.is_file():
        raise UpdateError('Produktivdatenbank fehlt oder ist ein Symlink')
    source = sqlite3.connect(f'{DB.as_uri()}?mode=ro', uri=True, timeout=15)
    try:
        target = sqlite3.connect(str(destination))
        try:
            source.backup(target, pages=256, sleep=0.1)
        finally:
            target.close()
    finally:
        source.close()
    check_db(destination)
    with destination.open('rb') as stream:
        os.fsync(stream.fileno())
    sync_directory(destination.parent)


def restore_db(backup, ownership):
    """Nur bei gestopptem Dienst; WAL-Dateien danach nicht wiederverwenden."""
    if service_active():
        raise UpdateError('Datenbankwiederherstellung bei laufendem Dienst untersagt')
    check_db(backup)
    if DB.is_symlink():
        raise UpdateError('Datenbankpfad ist ein Symlink')
    fd, temp = tempfile.mkstemp(prefix='.stats-restore-', dir=DB.parent)
    try:
        with os.fdopen(fd, 'wb') as dst, backup.open('rb') as src:
            shutil.copyfileobj(src, dst)
            dst.flush()
            os.fchmod(dst.fileno(), ownership['mode'])
            os.fchown(dst.fileno(), ownership['uid'], ownership['gid'])
            os.fsync(dst.fileno())
        for suffix in ('-wal', '-shm'):
            sidecar = Path(str(DB) + suffix)
            if sidecar.is_symlink():
                raise UpdateError('SQLite-Sidecar ist ein Symlink')
            sidecar.unlink(missing_ok=True)
        os.replace(temp, DB)
        sync_directory(DB.parent)
    finally:
        if os.path.exists(temp):
            os.unlink(temp)
    check_db()


def verify_app_tree(path):
    if path.is_symlink() or not path.is_dir():
        raise UpdateError(f'Anwendungsverzeichnis fehlt oder ist Symlink: {path}')
    for root, dirs, files in os.walk(path, followlinks=False):
        for name in dirs + files:
            candidate = Path(root) / name
            if candidate.is_symlink():
                raise UpdateError(f'Symlink im Anwendungsverzeichnis: {candidate}')


def txn_paths(id):
    if not TXID.fullmatch(id):
        raise UpdateError('Ungueltige Transaktionskennung')
    return {
        'snapshot': APP.parent / f'.telenec-stats-snapshot-{id}',
        'stage': APP.parent / f'.telenec-stats-stage-{id}',
        'displaced': APP.parent / f'.telenec-stats-displaced-{id}',
        'restore': APP.parent / f'.telenec-stats-restore-{id}',
        'failed': APP.parent / f'.telenec-stats-failed-{id}',
        'backup': STATE / 'backups' / id / 'database.db',
    }


def read_pending():
    path = STATE / 'pending.json'
    if not path.exists():
        return None
    try:
        entry = json.loads(read_regular(path, 4096))
    except ValueError as exc:
        raise UpdateError('Journal ist nicht lesbar') from exc
    if not isinstance(entry, dict) or entry.get('phase') not in ('prepared', 'switching', 'committed'):
        raise UpdateError('Ungueltiges Update-Journal')
    txn_paths(entry.get('id', ''))
    version(entry.get('previous'))
    version(entry.get('target'))
    if not isinstance(entry.get('sha256'), str) or not SHA.fullmatch(entry['sha256']):
        raise UpdateError('Ungueltiger Journal-Pruefwert')
    if not isinstance(entry.get('ownership'), dict):
        raise UpdateError('Datenbankeigentum im Journal fehlt')
    for field in ('uid', 'gid', 'mode'):
        if type(entry['ownership'].get(field)) is not int:
            raise UpdateError('Datenbankeigentum im Journal ungueltig')
    return entry


def write_pending(entry):
    atomic_json(STATE / 'pending.json', entry)


def finish_pending():
    (STATE / 'pending.json').unlink(missing_ok=True)
    sync_directory(STATE)


def mark_failed(entry):
    atomic_json(STATE / 'failed-release.json',
                {'version': entry['target'], 'sha256': entry['sha256']})


def fsync_tree(path):
    """Dateikopien vor dem Umschalten auf Datentraeger schreiben."""
    for root, dirs, files in os.walk(path):
        for name in files:
            with (Path(root) / name).open('rb') as stream:
                os.fsync(stream.fileno())
        sync_directory(Path(root))


def prune_snapshots(retain=3):
    """Nur alte, eindeutig benannte Sicherungen entfernen."""
    prefix = '.telenec-stats-snapshot-'
    snapshots = []
    for path in APP.parent.glob(prefix + '*'):
        if path.is_dir() and not path.is_symlink() and TXID.fullmatch(path.name[len(prefix):]):
            snapshots.append(path)

    def backup_time(path):
        txid = path.name[len(prefix):]
        backup_dir = STATE / 'backups' / txid
        return backup_dir.stat().st_mtime_ns if backup_dir.is_dir() else path.stat().st_mtime_ns

    snapshots.sort(key=backup_time, reverse=True)
    for snapshot in snapshots[retain:]:
        txid = snapshot.name[len(prefix):]
        safe_remove_tree(snapshot)
        safe_remove_tree(STATE / 'backups' / txid)


def safe_remove_tree(path):
    if path.is_symlink():
        raise UpdateError(f'Unsicherer Symlink beim Aufraeumen: {path}')
    if path.is_dir():
        shutil.rmtree(path)


def recover_pending():
    """Bei abgebrochener Transaktion zuerst den alten Zustand herstellen."""
    entry = read_pending()
    if entry is None:
        return False
    paths = txn_paths(entry['id'])
    print(f'Unterbrochenes Update gefunden: {entry["previous"]} -> {entry["target"]}, Phase {entry["phase"]}')
    if entry['phase'] == 'committed':
        health_check()
        safe_remove_tree(paths['displaced'])
        safe_remove_tree(paths['stage'])
        finish_pending()
        print('Abgeschlossenes Update bereinigt.')
        return True
    if entry['phase'] == 'prepared':
        if not service_active():
            invoke_systemctl('start')
            health_check()
        mark_failed(entry)
        safe_remove_tree(paths['stage'])
        finish_pending()
        print('Vorbereitung abgebrochen; Release zur Sicherheit gesperrt.')
        return True

    if not paths['snapshot'].is_dir() or not paths['backup'].is_file():
        raise UpdateError('ROLLBACK NICHT MOEGLICH: Sicherung fehlt; Journal bleibt bestehen')
    print('Stelle Anwendung und Datenbank aus Sicherungen wieder her ...')
    invoke_systemctl('stop')
    if service_active():
        raise UpdateError('Dienst laeuft trotz Stop-Anforderung')
    safe_remove_tree(paths['restore'])
    verify_app_tree(paths['snapshot'])
    shutil.copytree(paths['snapshot'], paths['restore'])
    paths['restore'].chmod(0o755)
    safe_remove_tree(paths['failed'])
    if APP.exists() or APP.is_symlink():
        if APP.is_symlink():
            raise UpdateError('Produktivpfad ist ein Symlink')
        os.replace(APP, paths['failed'])
    os.replace(paths['restore'], APP)
    sync_directory(APP.parent)
    restore_db(paths['backup'], entry['ownership'])
    atomic_bytes(STATE / 'installed-version', (entry['previous'] + '\n').encode('ascii'))
    mark_failed(entry)
    invoke_systemctl('start')
    health_check()
    finish_pending()
    safe_remove_tree(paths['failed'])
    safe_remove_tree(paths['displaced'])
    safe_remove_tree(paths['stage'])
    print('ROLLBACK ERFOLGREICH: alte Anwendung und Datenbank wiederhergestellt.')
    return True


def perform_update(manifest, package):
    installed = read_version()
    if version(manifest['version']) <= version(installed):
        print(f'Kein Update: installiert {installed}, angeboten {manifest["version"]}.')
        return
    failed_path = STATE / 'failed-release.json'
    if failed_path.exists():
        failed = json.loads(read_regular(failed_path, 4096))
        if failed.get('version') == manifest['version'] and failed.get('sha256') == manifest['sha256']:
            print('Release nach vorherigem Fehlschlag gesperrt; keine erneute Installation.')
            return
    if not service_active():
        raise UpdateError('Produktivdienst ist vor Update nicht aktiv')
    health_check(timeout=10)
    verify_app_tree(APP)
    if DB.is_symlink():
        raise UpdateError('Datenbank darf kein Symlink sein')
    check_space(package)
    db_stat = DB.stat()
    if not stat.S_ISREG(db_stat.st_mode):
        raise UpdateError('Ungueltige produktive Datenbank')
    id = os.urandom(12).hex()
    paths = txn_paths(id)
    (STATE / 'backups').mkdir(mode=0o700, exist_ok=True)
    backup_parent = paths['backup'].parent
    backup_parent.mkdir(mode=0o700)
    entry = {
        'id': id,
        'phase': 'prepared',
        'previous': installed,
        'target': manifest['version'],
        'sha256': manifest['sha256'],
        'ownership': {'uid': db_stat.st_uid, 'gid': db_stat.st_gid,
                      'mode': stat.S_IMODE(db_stat.st_mode)},
    }
    try:
        print('Sichere vorhandene Anwendungsdateien ...')
        shutil.copytree(APP, paths['snapshot'])
        paths['snapshot'].chmod(0o700)
        fsync_tree(paths['snapshot'])
        print('Bereite signierte neue Anwendungsdateien vor ...')
        count = unpack(package, paths['stage'])
        fsync_tree(paths['stage'])
        print(f'Paket vorbereitet: {count} Dateien.')
        write_pending(entry)
    except (OSError, UpdateError, sqlite3.Error, UpdateInterrupted):
        if not (STATE / 'pending.json').exists():
            safe_remove_tree(paths['stage'])
            safe_remove_tree(paths['snapshot'])
            safe_remove_tree(backup_parent)
        raise
    try:
        invoke_systemctl('stop')
        if service_active():
            raise UpdateError('Dienst blieb trotz Stop-Anforderung aktiv')
        print('Erstelle konsistente Datenbanksicherung am Umschaltpunkt ...')
        backup_db(paths['backup'])
        entry['phase'] = 'switching'
        write_pending(entry)
        os.replace(APP, paths['displaced'])
        os.replace(paths['stage'], APP)
        sync_directory(APP.parent)
        invoke_systemctl('start')
        health_check()
        atomic_bytes(STATE / 'installed-version', (manifest['version'] + '\n').encode('ascii'))
        entry['phase'] = 'committed'
        write_pending(entry)
    except (OSError, sqlite3.Error, UpdateError, UpdateInterrupted, subprocess.TimeoutExpired) as exc:
        print(f'UPDATE FEHLGESCHLAGEN: {exc}', file=sys.stderr)
        try:
            mark_failed(entry)
            recover_pending()
        except Exception as rollback_exc:
            raise UpdateError(f'KRITISCH: Rollback fehlgeschlagen: {rollback_exc}') from rollback_exc
        raise UpdateError('Update fehlgeschlagen; Rollback wurde ausgefuehrt') from exc

    # Nach erfolgreichem Commit sind Aufraeumfehler kein Rollback-Grund mehr.
    print(f'UPDATE ERFOLGREICH: Version {manifest["version"]} installiert.')
    try:
        safe_remove_tree(paths['displaced'])
        finish_pending()
        prune_snapshots()
    except (OSError, UpdateError) as exc:
        print(f'WARNUNG: Nachbereitung unvollstaendig: {exc}', file=sys.stderr)
    print(f'Wiederherstellungskopie: {paths["snapshot"]}.')


def run(mode):
    if mode in ('prepare', 'apply') and os.geteuid() != 0:
        raise UpdateError('Vorbereitung/Installation nur als root')
    if mode in ('prepare', 'apply'):
        with update_lock():
            if mode == 'apply' and recover_pending():
                return
            if not (UPDATES / 'release.json').exists() and not (UPDATES / 'release.json.sig').exists():
                print('Kein Server-Release bereitgestellt.')
                return
            with tempfile.TemporaryDirectory(prefix='release-', dir=STATE) as temporary:
                manifest, package = snapshot_release(Path(temporary))
                print(f'Signatur und ZIP gueltig: {manifest["version"]}.')
                if mode == 'prepare':
                    count = unpack(package, Path(temporary) / 'app')
                    print(f'VORBEREITUNG OK: {count} Dateien entpackt, keine Installation.')
                else:
                    perform_update(manifest, package)
    else:
        private_state()
        if read_pending() is not None:
            raise UpdateError('Unterbrochenes Update erkannt; --apply muss zuerst Wiederherstellung ausfuehren')
        if not (UPDATES / 'release.json').exists() and not (UPDATES / 'release.json.sig').exists():
            print('Kein Server-Release bereitgestellt.')
            return
        with tempfile.TemporaryDirectory(prefix='check-', dir=STATE) as temporary:
            manifest, _ = snapshot_release(Path(temporary))
            installed = read_version()
            cmp = version(manifest['version']) > version(installed)
            print(f'Signatur und ZIP gueltig: {manifest["version"]}.')
            print(f'Installiert: {installed}; angeboten: {manifest["version"]}; '
                  f'{"UPDATE VERFUEGBAR" if cmp else "kein Update noetig"}.')
            print('Nur Pruefung: Dienst und Datenbank unveraendert.')


def main():
    def interrupted(signum, _frame):
        raise UpdateInterrupted(f'Signal {signum} erhalten')

    signal.signal(signal.SIGTERM, interrupted)
    signal.signal(signal.SIGINT, interrupted)
    parser = argparse.ArgumentParser(description=__doc__)
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument('--prepare', action='store_true', help='Signiertes ZIP isoliert entpacken')
    mode.add_argument('--apply', action='store_true', help='Neues Release installieren und pruefen')
    args = parser.parse_args()
    try:
        run('apply' if args.apply else 'prepare' if args.prepare else 'check')
        return 0
    except (UpdateError, UpdateInterrupted, OSError, sqlite3.Error, subprocess.TimeoutExpired, ValueError) as exc:
        print(f'UPDATE ABGELEHNT: {exc}', file=sys.stderr)
        return 1


if __name__ == '__main__':
    sys.exit(main())
