#!/usr/bin/env python3
"""Regressionstests fuer den Postfix-Collector – ausschliesslich Kunstlogs.

Kein produktiver Serverzugriff, keine echten Konten, kein Snapshot-Schreiben.
Aufruf: python3 -m unittest discover -s tools/tests -p 'test_mail_traffic_collector.py' -v
"""
from __future__ import annotations

import datetime as dt
import importlib.util
import tempfile
import unittest
from pathlib import Path
from zoneinfo import ZoneInfo


HERE = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location(
    "telenec_mail_traffic_collector",
    HERE / "telenec-mail-traffic-collector.py",
)
collector = importlib.util.module_from_spec(spec)
spec.loader.exec_module(collector)
NOW = dt.datetime(2026, 11, 23, 5, 30, tzinfo=dt.timezone.utc)


def row(second: int, pid: int, content: str, process: str = "smtpd") -> str:
    return (
        f"Nov 23 05:00:{second:02d} testhost "
        f"postfix/{process}[{pid}]: {content}\n"
    )


def connect(second=0, pid=100):
    return row(second, pid, "connect from unknown[127.0.0.1]")


def accepted(qid="A00001", second=2, pid=100, account="test@example.test"):
    return row(
        second, pid,
        f"{qid}: client=unknown[127.0.0.1], sasl_username={account}",
    )


def qmgr(qid="A00001", second=3, pid=201):
    return row(
        second, pid, f"{qid}: from=<test@example.test>, size=234, nrcpt=1",
        process="qmgr",
    )


def disconnect(second=5, pid=100, data="0/1"):
    return row(
        second, pid,
        f"disconnect from unknown[127.0.0.1] "
        f"ehlo=1 mail=1 rcpt=1 data={data} commands=4/5",
    )


def lost(second=4, pid=100):
    return row(second, pid, "lost connection after DATA from unknown[127.0.0.1]")


def analyze(*lines):
    with tempfile.TemporaryDirectory() as directory:
        Path(directory, "maillog").write_text(
            "".join(lines), encoding="utf-8"
        )
        return collector.analyze(Path(directory), NOW, ZoneInfo("UTC"))[1]


class MailTrafficSessionTests(unittest.TestCase):
    def test_proven_data_abort_is_not_counted_as_outgoing_or_unconfirmed(self):
        stats = analyze(connect(), accepted(), lost(), disconnect())
        self.assertEqual(stats["strict_data_aborted_auth"], 1)
        self.assertEqual(stats["unconfirmed_auth"], 0)
        self.assertEqual(stats["outgoing"], 0)

    def test_qmgr_confirmation_still_counts_even_if_disconnect_has_data_zero(self):
        # Positive queue confirmation takes precedence; never discard
        # a proven, successfully queued mail.
        stats = analyze(connect(), accepted(), qmgr(), disconnect(data="0/1"))
        self.assertEqual(stats["outgoing"], 1)
        self.assertEqual(stats["strict_data_aborted_auth"], 0)
        self.assertEqual(stats["unconfirmed_auth"], 0)

    def test_no_disconnect_remains_unresolved(self):
        stats = analyze(connect(), accepted(), lost())
        self.assertEqual(stats["unconfirmed_auth"], 1)
        self.assertEqual(stats["strict_data_aborted_auth"], 0)

    def test_no_connect_remains_unresolved(self):
        stats = analyze(accepted(), disconnect())
        self.assertEqual(stats["unconfirmed_auth"], 1)
        self.assertEqual(stats["strict_data_aborted_auth"], 0)

    def test_two_mail_candidates_in_one_session_remain_unresolved(self):
        stats = analyze(
            connect(),
            accepted(qid="A00001", second=1),
            accepted(qid="B00002", second=2),
            lost(), disconnect(),
        )
        self.assertEqual(stats["unconfirmed_auth"], 2)
        self.assertEqual(stats["strict_data_aborted_auth"], 0)

    def test_pid_reused_across_separate_connections_is_correctly_scoped(self):
        stats = analyze(
            connect(second=0),
            accepted(qid="A00001", second=1),
            lost(second=2),
            disconnect(second=3),
            connect(second=10),
            accepted(qid="B00002", second=11),
            qmgr(qid="B00002", second=12),
            disconnect(second=13, data="1/1"),
        )
        self.assertEqual(stats["strict_data_aborted_auth"], 1)
        self.assertEqual(stats["outgoing"], 1)
        self.assertEqual(stats["unconfirmed_auth"], 0)

    def test_data_without_attempt_is_not_a_proven_abort(self):
        stats = analyze(connect(), accepted(), disconnect(data="0/0"))
        self.assertEqual(stats["strict_data_aborted_auth"], 0)
        self.assertEqual(stats["unconfirmed_auth"], 1)

    def test_successful_data_command_without_queue_is_still_unresolved(self):
        stats = analyze(connect(), accepted(), disconnect(data="1/1"))
        self.assertEqual(stats["unconfirmed_auth"], 1)
        self.assertEqual(stats["strict_data_aborted_auth"], 0)

    def test_same_second_timestamp_is_ambiguous(self):
        stats = analyze(
            connect(second=2),
            accepted(second=2),
            disconnect(second=5),
        )
        self.assertEqual(stats["unconfirmed_auth"], 1)
        self.assertEqual(stats["strict_data_aborted_auth"], 0)

    def test_intervening_disconnect_invalidates_session(self):
        stats = analyze(
            connect(second=0),
            disconnect(second=1),
            accepted(second=2),
            disconnect(second=3),
        )
        self.assertEqual(stats["unconfirmed_auth"], 1)
        self.assertEqual(stats["strict_data_aborted_auth"], 0)

    def test_other_non_sasl_queue_event_invalidates_exclusivity(self):
        stats = analyze(
            connect(second=0),
            accepted(qid="A00001", second=1),
            row(
                2, 100,
                "B00002: client=unknown[127.0.0.1]",
            ),
            disconnect(second=5),
        )
        self.assertEqual(stats["unconfirmed_auth"], 1)
        self.assertEqual(stats["strict_data_aborted_auth"], 0)

    def test_incoming_mail_processing_is_not_changed(self):
        stats = analyze(
            row(
                3, 501,
                "C00003: to=<recipient@example.test>, "
                "relay=plesk_virtual, delay=0.1, status=sent (delivered)",
                process="pipe",
            )
        )
        self.assertEqual(stats["incoming"], 1)
        self.assertEqual(stats["outgoing"], 0)

    def test_repeated_same_log_line_is_idempotent(self):
        entries = [connect(), accepted(), lost(), disconnect()]
        stats = analyze(*(entries + entries))
        self.assertEqual(stats["strict_data_aborted_auth"], 1)
        self.assertEqual(stats["unconfirmed_auth"], 0)


if __name__ == "__main__":
    unittest.main()
