#!/usr/bin/env python3
"""Isolierte SQLite-Backup-Regressionstests. Keine Produktivdaten betroffen."""
from __future__ import annotations

import contextlib
import datetime as dt
import importlib.util
import io
import os
import sqlite3
import stat
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch


SCRIPT = Path(__file__).resolve().parents[1] / "telenec-statistics-db-backup.py"
spec = importlib.util.spec_from_file_location("statistics_backup", SCRIPT)
assert spec is not None and spec.loader is not None
backup = importlib.util.module_from_spec(spec)
spec.loader.exec_module(backup)


class StatisticsBackupTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.directory = Path(self.temp.name)
        self.source = self.directory / "product.db"
        self.dest = self.directory / "backups"
        self.dest.mkdir(mode=0o700)
        self.source_patch = patch.object(backup, "SOURCE", self.source)
        self.dest_patch = patch.object(backup, "DESTINATION", self.dest)
        self.source_patch.start()
        self.dest_patch.start()
        self.addCleanup(self.source_patch.stop)
        self.addCleanup(self.dest_patch.stop)

        connection = sqlite3.connect(self.source)
        try:
            connection.execute("CREATE TABLE example (id INTEGER PRIMARY KEY, text TEXT NOT NULL)")
            connection.execute("INSERT INTO example (text) VALUES (?)", ("internal test",))
            connection.commit()
        finally:
            connection.close()

    def test_consistent_backup_and_permissions(self):
        output = io.StringIO()
        with contextlib.redirect_stdout(output):
            backup.run_backup()
        files = backup.backup_files()
        self.assertEqual(len(files), 1)
        self.assertEqual(stat.S_IMODE(files[0].stat().st_mode), 0o600)
        backup.verify_database(files[0])
        with sqlite3.connect(files[0]) as db:
            self.assertEqual(db.execute("SELECT text FROM example").fetchone(), ("internal test",))
        self.assertNotIn("internal test", output.getvalue())
        self.assertEqual(len(list(self.dest.glob(".backup-*"))), 0)

    def test_prune_only_old_matching_backups(self):
        now = dt.datetime.now(dt.timezone.utc)
        old = (now - dt.timedelta(days=20)).strftime("%Y%m%dT%H%M%SZ")
        recent = now.strftime("%Y%m%dT%H%M%SZ")
        old_file = self.dest / f"statistics-{old}-01234567.db"
        new_file = self.dest / f"statistics-{recent}-fedcba98.db"
        unrelated = self.dest / "keep-me.txt"
        old_file.write_bytes(b"old")
        new_file.write_bytes(b"new")
        unrelated.write_bytes(b"unrelated")
        self.assertEqual(backup.prune_backups(now), 1)
        self.assertFalse(old_file.exists())
        self.assertTrue(new_file.exists())
        self.assertTrue(unrelated.exists())

    def test_corrupted_database_does_not_publish_backup(self):
        self.source.write_bytes(b"not a sqlite database")
        with self.assertRaises((sqlite3.DatabaseError, backup.BackupError)):
            backup.run_backup()
        self.assertEqual(backup.backup_files(), [])
        self.assertEqual(len(list(self.dest.glob(".backup-*"))), 0)

    def test_reject_symlink_destination(self):
        linked = self.directory / "linked-backups"
        linked.symlink_to(self.dest, target_is_directory=True)
        with patch.object(backup, "DESTINATION", linked):
            with self.assertRaises(backup.BackupError):
                backup.ensure_backup_directory()


if __name__ == "__main__":
    unittest.main()
