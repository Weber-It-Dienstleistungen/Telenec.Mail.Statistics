#!/usr/bin/env python3
"""
Telenec Mail Statistics – Postfix/Plesk Mailverkehrs-Collector (Sprint 36.1).

Sicherer Erststart: ausschließlich lesende Diagnose, kein Snapshot.
Snapshot-Schreiben nur nach ausdrücklicher Freigabe mit --write --output.

Zählregeln:
* Ausgehend: pro Postfix-Queue-ID genau eine durch SASL authentifizierte
  und durch qmgr bestätigte Einlieferung (nicht pro SMTP-Empfänger).
* Eingehend: pro (Queue-ID, Empfängerpostfach) genau eine erfolgreiche
  Postfix pipe-Zustellung über den Plesk-Transport plesk_virtual.
* Weder bloße Verbindungsversuche noch status=deferred/bounced zählen.
* systemlokale pickup-Einlieferungen werden diagnostisch erfasst, aber
  nicht als authentifizierte Benutzeraccounts ausgewiesen.

Diese Werte sind eine klar definierte, nicht zwingend vollständige
Sicht des Postfix-Verkehrs. Ein Content-Filter mit erneuter Einlieferung,
andere Annahmepfade und vertrauenswürdige nicht-SASL-Relays sind vor
der produktiven Freigabe separat zu bewerten.
"""

from __future__ import annotations

import argparse
import collections
import datetime as dt
import gzip
import grp
import json
import os
from pathlib import Path
import re
import sys
import tempfile
from zoneinfo import ZoneInfo


UTC = dt.timezone.utc
SYSLOG_TIME = re.compile(
    r"^(?P<stamp>[A-Z][a-z]{2}\s+\d{1,2}\s+\d\d:\d\d:\d\d)\s"
)
ISO_TIME = re.compile(
    r"^(?P<stamp>\d{4}-\d\d-\d\d[T ]\d\d:\d\d:\d\d"
    r"(?:\.\d+)?(?:Z|[+-]\d\d:?\d\d)?)\s"
)
POSTFIX = re.compile(
    r"postfix/(?P<process>[a-z0-9_]+)\[\d+\]:\s+"
    r"(?P<queue>[A-Za-z0-9]+):\s+(?P<details>.*)"
)
SASL = re.compile(r"(?:^|[,\s])sasl_username=(?P<account>[^,\s]+)")
TO = re.compile(r"(?:^|[,\s])to=<(?P<account>[^>]*)>")
STATUS_SENT = re.compile(r"(?:^|[,\s])status=sent(?:[,\s]|$)")
PLESK_VIRTUAL = re.compile(r"(?:^|[,\s])relay=plesk_virtual(?:[,\s]|$)")
QMANAGER = re.compile(r"(?:^|[,\s])from=<[^>]*>,\s*size=\d+")
MAXIMUM_ACCOUNT_LENGTH = 254


def read_timestamp(line: str, reference: dt.datetime, tz: ZoneInfo):
    """RFC3164 syslog oder ISO-8601; UTC-Zeitpunkt, ohne Logtext preiszugeben."""
    iso = ISO_TIME.match(line)
    if iso:
        raw = iso.group("stamp")
        try:
            parsed = dt.datetime.fromisoformat(raw.replace("Z", "+00:00"))
            if parsed.tzinfo is None:
                parsed = parsed.replace(tzinfo=tz)
            return parsed.astimezone(UTC)
        except ValueError:
            return None

    traditional = SYSLOG_TIME.match(line)
    if not traditional:
        return None

    try:
        partial = dt.datetime.strptime(
            traditional.group("stamp"), "%b %d %H:%M:%S"
        )
    except ValueError:
        return None

    # Logrotation und Jahreswechsel berücksichtigen: der nächstliegende
    # Zeitstempel gewinnt, ohne die Systemuhr zu verstellen.
    reference_local = reference.astimezone(tz)
    candidates = []
    for year in (reference_local.year - 1, reference_local.year,
                 reference_local.year + 1):
        try:
            candidate = partial.replace(year=year, tzinfo=tz)
            candidates.append(candidate.astimezone(UTC))
        except ValueError:
            continue

    return min(candidates, key=lambda value: abs(value - reference)) if candidates else None


def normalize_account(value: str):
    value = value.strip().strip("<>").lower()
    if (not value or len(value) > MAXIMUM_ACCOUNT_LENGTH
            or "@" not in value or "\r" in value or "\n" in value
            or " " in value):
        return None
    return value


def iter_log_paths(directory: Path, earliest: dt.datetime):
    """Nur relevante aktuelle und rotierte Dateien; keine beliebigen Dateien."""
    candidates = [
        directory / "maillog",
        directory / "maillog.processed",
        *sorted(directory.glob("maillog.processed.*.gz")),
    ]
    seen = set()

    for path in candidates:
        if path in seen:
            continue
        seen.add(path)

        try:
            stat = path.stat()
        except FileNotFoundError:
            continue

        if not path.is_file():
            continue

        # Eine rotierte Datei kann Ereignisse enthalten, deren Logzeit
        # vor ihrer Dateizeit liegt. Wir lassen 48 Stunden Reserve.
        if (path.name.endswith(".gz")
                and stat.st_mtime < (earliest - dt.timedelta(hours=48)).timestamp()):
            continue

        yield path


def iter_lines(path: Path):
    opener = gzip.open if path.name.endswith(".gz") else open
    with opener(path, "rt", encoding="utf-8", errors="replace") as stream:
        yield from stream


def hourly_index(time: dt.datetime, start: dt.datetime) -> int:
    return int((time - start).total_seconds() // 3600)


def analyze(directory: Path, clock: dt.datetime, timezone: ZoneInfo):
    # 24 stündliche Balken ab hh:00; der letzte Balken ist laufend
    # und daher noch nicht abgeschlossen.
    current_hour = clock.replace(minute=0, second=0, microsecond=0)
    start = current_hour - dt.timedelta(hours=23)
    end = current_hour + dt.timedelta(hours=1)
    observed_until = clock
    context_start = start - dt.timedelta(hours=3)

    submissions = {}
    queued = set()
    pickups = {}
    deliveries = {}

    files = []
    parsed_lines = 0
    relevant_lines = 0
    sasl_events = 0
    sasl_client_events = 0
    sasl_other_events = 0
    accepted_sasl_queue_ids = set()
    qmgr_events = 0
    qmgr_matching_pattern = 0
    sasl_queue_ids = set()
    qmgr_queue_ids = set()
    earliest = None
    latest = None

    for path in iter_log_paths(directory, context_start):
        files.append(path.name)
        for line in iter_lines(path):
            if "postfix/" not in line:
                continue

            at = read_timestamp(line, clock, timezone)
            if at is None or at < context_start or at > observed_until:
                continue

            match = POSTFIX.search(line)
            if match is None:
                continue

            parsed_lines += 1
            earliest = at if earliest is None else min(earliest, at)
            latest = at if latest is None else max(latest, at)

            qid = match.group("queue")
            process = match.group("process")
            details = match.group("details")

            if process == "smtpd" and "sasl_username=" in details:
                sasl_events += 1
                sasl_queue_ids.add(qid)
                # Eine Queue-ID allein belegt keine angenommene Nachricht.
                # Nur SMTPD-Ereignisse in der Form "ID: client=...,
                # sasl_username=..." als Versandkandidaten betrachten.
                # Andere SASL-Meldungen werden ausschliesslich diagnostiziert.
                if details.startswith("client="):
                    sasl_client_events += 1
                    accepted_sasl_queue_ids.add(qid)
                    sasl = SASL.search(details)
                    if sasl:
                        account = normalize_account(sasl.group("account"))
                        if account is not None:
                            previous = submissions.get(qid)
                            if previous is None or at < previous[0]:
                                submissions[qid] = (at, account)
                            relevant_lines += 1
                else:
                    sasl_other_events += 1

            elif process == "qmgr":
                qmgr_events += 1
                qmgr_queue_ids.add(qid)
                if QMANAGER.search(details):
                    qmgr_matching_pattern += 1
                    queued.add(qid)
                    relevant_lines += 1

            elif process == "pickup":
                previous = pickups.get(qid)
                if previous is None or at < previous:
                    pickups[qid] = at
                relevant_lines += 1

            elif (process == "pipe"
                  and STATUS_SENT.search(details)
                  and PLESK_VIRTUAL.search(details)):
                recipient_match = TO.search(details)
                if recipient_match:
                    recipient = normalize_account(
                        recipient_match.group("account")
                    )
                    if recipient is not None:
                        key = (qid, recipient)
                        previous = deliveries.get(key)
                        if previous is None or at < previous:
                            deliveries[key] = at
                        relevant_lines += 1

    hours = []
    for index in range(24):
        hour = start + dt.timedelta(hours=index)
        hours.append({
            "hourStartUtc": hour.isoformat().replace("+00:00", "Z"),
            "outgoing": 0,
            "incoming": 0,
        })

    senders = collections.Counter()
    recipients = collections.Counter()
    confirmed_outgoing = 0
    unconfirmed_submissions = 0
    all_submissions_in_window = 0

    for qid, (at, account) in submissions.items():
        if at < start:
            continue

        all_submissions_in_window += 1
        if qid not in queued:
            unconfirmed_submissions += 1
            continue

        index = hourly_index(at, start)
        if 0 <= index < 24:
            hours[index]["outgoing"] += 1
            senders[account] += 1
            confirmed_outgoing += 1

    confirmed_incoming = 0
    for (_qid, account), at in deliveries.items():
        if at < start:
            continue
        index = hourly_index(at, start)
        if 0 <= index < 24:
            hours[index]["incoming"] += 1
            recipients[account] += 1
            confirmed_incoming += 1

    pickups_in_window = sum(
        1 for qid, at in pickups.items()
        if start <= at < end and qid in queued
    )

    def ranking(counter):
        return [
            {"account": account, "count": count}
            for account, count in sorted(
                counter.items(), key=lambda item: (-item[1], item[0])
            )[:3]
        ]

    snapshot = {
        "generatedAtUtc": clock.isoformat().replace("+00:00", "Z"),
        "windowStartUtc": start.isoformat().replace("+00:00", "Z"),
        "windowEndUtc": end.isoformat().replace("+00:00", "Z"),
        "hours": hours,
        "topSenders": ranking(senders),
        "topRecipients": ranking(recipients),
    }

    diagnostics = {
        "files": files,
        "first_seen": earliest,
        "last_seen": latest,
        "parsed_lines": parsed_lines,
        "relevant_lines": relevant_lines,
        "outgoing": confirmed_outgoing,
        "incoming": confirmed_incoming,
        "unconfirmed_auth": unconfirmed_submissions,
        "submission_queue_ids": all_submissions_in_window,
        "sasl_events": sasl_events,
        "sasl_client_events": sasl_client_events,
        "sasl_other_events": sasl_other_events,
        "unique_sasl_queue_ids": len(sasl_queue_ids),
        "unique_accepted_sasl_queue_ids": len(accepted_sasl_queue_ids),
        "accepted_sasl_qmgr_overlap": len(accepted_sasl_queue_ids & queued),
        "qmgr_events": qmgr_events,
        "unique_qmgr_queue_ids": len(qmgr_queue_ids),
        "qmgr_matching_pattern": qmgr_matching_pattern,
        "sasl_qmgr_overlap": len(sasl_queue_ids & qmgr_queue_ids),
        "sasl_queued_overlap": len(sasl_queue_ids & queued),
        "local_pickup": pickups_in_window,
        "unique_senders": len(senders),
        "unique_recipients": len(recipients),
    }
    return snapshot, diagnostics


def atomic_write(output: Path, snapshot: dict, group: str | None):
    if not output.parent.is_dir():
        raise RuntimeError(
            "Das Ausgabe-Verzeichnis muss bereits existieren."
        )

    data = json.dumps(
        snapshot, ensure_ascii=False, separators=(",", ":")
    ).encode("utf-8")

    if len(data) > 256 * 1024:
        raise RuntimeError("Aggregierte Daten überschreiten 256 KiB.")

    fd, temp = tempfile.mkstemp(
        prefix=".mail-traffic-", suffix=".tmp", dir=output.parent
    )
    try:
        os.fchmod(fd, 0o600)
        if group is not None:
            os.fchown(fd, -1, grp.getgrnam(group).gr_gid)
            os.fchmod(fd, 0o640)

        with os.fdopen(fd, "wb") as stream:
            stream.write(data)
            stream.flush()
            os.fsync(stream.fileno())

        os.replace(temp, output)
    except BaseException:
        try:
            os.unlink(temp)
        except FileNotFoundError:
            pass
        raise


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Postfix/Plesk: 24h-Mailverkehr aggregieren (standardmäßig nur Diagnose)"
    )
    parser.add_argument(
        "--log-dir", type=Path, default=Path("/var/log"),
        help="Verzeichnis mit maillog und maillog.processed*"
    )
    parser.add_argument(
        "--timezone", default="auto",
        help="Zeitzone von RFC3164-Logs; auto liest die Hostzeitzone. "
             "Mit --timezone UTC oder --timezone Europe/Berlin ueberschreiben."
    )
    parser.add_argument(
        "--write", action="store_true",
        help="Erst nach gesonderter Freigabe einen aggregierten Snapshot schreiben"
    )
    parser.add_argument(
        "--output", type=Path,
        help="Zieldatei für den aggregierten Snapshot (nur mit --write)"
    )
    parser.add_argument(
        "--group", help="Optionale Unix-Gruppe für die Datei (Dateirechte 0640)"
    )
    args = parser.parse_args()

    if args.write and args.output is None:
        parser.error("--write benötigt --output")
    if args.output and not args.write:
        parser.error("--output ist nur mit --write erlaubt")
    if args.group and not args.write:
        parser.error("--group ist nur mit --write erlaubt")

    try:
        if args.timezone == "auto":
            # RFC3164-Syslog benutzt normalerweise die Host-Zeitzone.
            # Auf Ubuntu zuerst /etc/timezone, sonst /etc/localtime nutzen.
            timezone_file = Path("/etc/timezone")
            if timezone_file.is_file():
                zone_name = timezone_file.read_text(
                    encoding="utf-8"
                ).strip()
            else:
                localtime = str(Path("/etc/localtime").resolve())
                marker = "/zoneinfo/"
                if marker not in localtime:
                    raise RuntimeError(
                        "Log-Zeitzone nicht ermittelbar: --timezone setzen."
                    )
                zone_name = localtime.split(marker, 1)[1]
        else:
            zone_name = args.timezone

        zone = ZoneInfo(zone_name)
        now = dt.datetime.now(UTC)
        snapshot, diagnostic = analyze(args.log_dir, now, zone)

        print("=== Telenec Mailverkehr – Diagnose (ohne Accountnamen) ===")
        print("Log-Zeitzone:", zone_name)
        print("Zeitfenster (24 Stundenbloecke; aktuelle Stunde unvollstaendig):", snapshot["windowStartUtc"], "bis",
              snapshot["windowEndUtc"])
        print("Eingelesene Dateien:", ", ".join(diagnostic["files"]) or "keine")
        print("Postfix-Zeilen im erweiterten Zeitraum:", diagnostic["parsed_lines"])
        print("Ausgehende authentifizierte Einlieferungen:", diagnostic["outgoing"])
        print("Erfolgreiche Plesk-Postfachzustellungen:", diagnostic["incoming"])
        print("SASL-Einlieferungen ohne qmgr-Bestätigung:",
              diagnostic["unconfirmed_auth"])
        print("SASL-Ereigniszeilen (erweiterter Zeitraum):",
              diagnostic["sasl_events"])
        print("Davon SMTP-Annahmeereignisse (client=):",
              diagnostic["sasl_client_events"])
        print("Andere SASL-Zeilen (nicht als Versand gezaehlt):",
              diagnostic["sasl_other_events"])
        print("Eindeutige Annahme-Queue-IDs:",
              diagnostic["unique_accepted_sasl_queue_ids"])
        print("Annahme-Queue-IDs mit qmgr-from/size:",
              diagnostic["accepted_sasl_qmgr_overlap"])
        print("Unterschiedliche SASL-Queue-IDs:",
              diagnostic["unique_sasl_queue_ids"])
        print("qmgr-Ereigniszeilen:", diagnostic["qmgr_events"])
        print("Unterschiedliche qmgr-Queue-IDs:",
              diagnostic["unique_qmgr_queue_ids"])
        print("qmgr-Zeilen mit erwartetem from/size-Muster:",
              diagnostic["qmgr_matching_pattern"])
        print("SASL-Queue-IDs auch in qmgr (beliebige Zeile):",
              diagnostic["sasl_qmgr_overlap"])
        print("SASL-Queue-IDs auch in qmgr-from/size:",
              diagnostic["sasl_queued_overlap"])
        print("Lokale Pickup-Einlieferungen, separat:", diagnostic["local_pickup"])
        print("Unterschiedliche Senderaccounts:", diagnostic["unique_senders"])
        print("Unterschiedliche Empfängeraccounts:", diagnostic["unique_recipients"])
        print("=== Stundenwerte (UTC, anonym) ===")
        for hour in snapshot["hours"]:
            print(hour["hourStartUtc"],
                  "ausgehend", hour["outgoing"],
                  "eingehend", hour["incoming"])

        if not diagnostic["files"] or not diagnostic["parsed_lines"]:
            print("FEHLER: Keine auswertbaren Postfix-Zeilen gefunden.", file=sys.stderr)
            return 2

        missing = diagnostic["unconfirmed_auth"]
        submissions_total = diagnostic["submission_queue_ids"]
        if submissions_total and missing / submissions_total > 0.20:
            print(
                "WARNUNG: Ueber 20 % der SASL-Queue-IDs ohne "
                "qmgr-from/size-Bestaetigung. Bei Snapshot-Schreiben "
                "abbrechen; die Versandstatistik waere unvollstaendig.",
                file=sys.stderr,
            )
            if args.write:
                return 2

        # Kein unvollständiger 24-Stunden-Bericht: dies ist nur ein
        # konservativer Plausibilitätscheck, kein Ersatz für Logrotation-Audit.
        first = diagnostic["first_seen"]
        start = dt.datetime.fromisoformat(
            snapshot["windowStartUtc"].replace("Z", "+00:00")
        )
        if first is None or first > start + dt.timedelta(minutes=15):
            print(
                "WARNUNG: Der Beginn des Zeitfensters wird durch die "
                "gefundenen Logs nicht hinreichend abgedeckt. "
                "Snapshot wird nicht freigegeben.",
                file=sys.stderr,
            )
            return 2

        if args.write:
            atomic_write(args.output, snapshot, args.group)
            print("Aggregierter Snapshot geschrieben:", args.output)
        else:
            print("Nur Diagnose: keine Datei geschrieben und kein Dienst geändert.")

        return 0
    except (OSError, RuntimeError, ValueError, KeyError) as exception:
        print("FEHLER:", exception, file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main())
