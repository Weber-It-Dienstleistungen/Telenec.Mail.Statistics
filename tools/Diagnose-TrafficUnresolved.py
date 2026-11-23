#!/usr/bin/env python3
"""Nur lesende, aggregierte Diagnose der nicht zugeordneten SMTP-Annahmen.

Keine Mailadressen, Queue-IDs, Nachrichteninhalte oder Einzelereignisse
in der Konsolenausgabe. Benutzt die Parser-Funktionen des produktiven
Traffic-Collectors, veraendert aber weder Snapshot noch Mailserver.
"""
from __future__ import annotations

import collections
import datetime as dt
from pathlib import Path
import runpy
import sys
from zoneinfo import ZoneInfo

UTC = dt.timezone.utc
COLLECTOR = Path(
    "/usr/local/libexec/telenec-mail-statistics/telenec-mail-traffic-collector.py"
)
LOG_DIR = Path("/var/log")


def timezone():
    zone_file = Path("/etc/timezone")
    if zone_file.is_file():
        return ZoneInfo(zone_file.read_text(encoding="utf-8").strip())
    marker = "/zoneinfo/"
    target = str(Path("/etc/localtime").resolve())
    if marker not in target:
        raise RuntimeError("Log-Zeitzone nicht ermittelbar.")
    return ZoneInfo(target.split(marker, 1)[1])


def main():
    if not COLLECTOR.is_file():
        raise RuntimeError("Installierter Traffic-Collector fehlt.")

    api = runpy.run_path(str(COLLECTOR), run_name="_diagnose_only_")
    now = dt.datetime.now(UTC)
    start = now.replace(minute=0, second=0, microsecond=0) - dt.timedelta(hours=23)
    context_start = start - dt.timedelta(hours=3)
    end = start + dt.timedelta(hours=24)
    tz = timezone()
    post = api["POSTFIX"]
    qmanager_pattern = api["QMANAGER"]

    accepted = collections.defaultdict(set)
    confirmed = collections.defaultdict(set)
    all_qmgr = collections.defaultdict(list)
    milter = collections.defaultdict(set)
    cleanup = collections.defaultdict(list)
    processes = collections.defaultdict(list)
    scanned = 0
    files = 0

    for path in api["iter_log_paths"](LOG_DIR, context_start):
        files += 1
        for line in api["iter_lines"](path):
            if "postfix/" not in line:
                continue
            at = api["read_timestamp"](line, now, tz)
            if at is None or at < context_start or at > now:
                continue
            match = post.search(line)
            if match is None:
                continue

            scanned += 1
            qid = match.group("queue")
            process = match.group("process")
            details = match.group("details")

            # Bei der Diagnose wird nur die Plausibilitaet
            # des SASL-Wertes geprueft, nicht der Wert gespeichert.
            if (process == "smtpd"
                    and details.startswith("client=")
                    and "sasl_username=" in details):
                sasl = api["SASL"].search(details)
                if sasl and api["normalize_account"](sasl.group("account")):
                    accepted[qid].add(at)

            if process == "qmgr":
                is_confirmed = bool(qmanager_pattern.search(details))
                all_qmgr[qid].append((at, is_confirmed))
                if is_confirmed:
                    confirmed[qid].add(at)
            elif process == "cleanup":
                is_milter = details.startswith("milter-reject:")
                cleanup[qid].append((at, is_milter))
                if is_milter:
                    milter[qid].add(at)
            elif process not in ("smtpd",):
                processes[qid].append((at, process))

    reasons = collections.Counter()
    other_processes = collections.Counter()
    confirmed_count = rejected_count = unresolved_count = 0
    recent_unresolved = 0

    window = dt.timedelta(minutes=15)
    for qid, event_times in accepted.items():
        acceptances = sorted(event_times)
        for index, at in enumerate(acceptances):
            if not start <= at <= now:
                continue
            limit = min(at + window, end)
            if index + 1 < len(acceptances):
                limit = min(limit, acceptances[index + 1])

            queued = any(at <= t < limit for t in confirmed.get(qid, ()))
            rejected = any(at <= t < limit for t in milter.get(qid, ()))

            if queued and rejected:
                reasons["widerspruechlich"] += 1
            elif rejected:
                rejected_count += 1
            elif queued:
                confirmed_count += 1
            else:
                unresolved_count += 1
                if now - at < window:
                    recent_unresolved += 1
                if any(at <= t < limit for t, _ in all_qmgr.get(qid, ())):
                    reasons["qmgr_ohne_erwartetes_from_size"] += 1
                elif any(t < at for t, _ in all_qmgr.get(qid, ())):
                    reasons["qmgr_nur_vor_dem_smtpd_ereignis"] += 1
                elif any(t >= limit for t, _ in all_qmgr.get(qid, ())):
                    reasons["qmgr_erst_nach_zuordnungsfenster"] += 1
                else:
                    reasons["keine_qmgr_zeile_mit_dieser_queue_id"] += 1

                if any(at <= t < limit for t, _ in cleanup.get(qid, ())):
                    reasons["cleanup_ohne_erkanntes_milter_reject"] += 1
                if any(t < at or t >= limit for t in milter.get(qid, ())):
                    reasons["milter_reject_nur_ausserhalb_zeitfenster"] += 1

                for process in {
                    proc for t, proc in processes.get(qid, ())
                    if at <= t < limit
                }:
                    # Nur fest definierte Prozessarten aggregieren.
                    if process in {
                        "smtp", "lmtp", "pipe", "local", "virtual",
                        "bounce", "error", "discard", "defer", "cleanup"
                    }:
                        other_processes[process] += 1

    print("=== Traffic-Diagnose (nur Zaehler, keine Accountdaten) ===")
    print("Dateien:", files)
    print("Gepruefte Postfix-Zeilen:", scanned)
    print("Zeitfenster UTC:", start.isoformat(), "bis", now.isoformat())
    print("Bestaetigt:", confirmed_count)
    print("Milter-abgewiesen:", rejected_count)
    print("Ungeklaert:", unresolved_count)
    print("Ungeklaert juenger als 15 Min:", recent_unresolved)
    for key in sorted(reasons):
        print("Zuordnung:", key, reasons[key])
    for key in sorted(other_processes):
        print("Weiterer Postfix-Prozess im Zeitfenster:", key, other_processes[key])
    print("Es wurden keine Snapshot-Dateien geschrieben.")
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (OSError, ValueError, RuntimeError, KeyError) as exc:
        # Fehlermeldungen koennten Pfade enthalten,
        # daher nur einen statischen Status ausgeben.
        print("Diagnose fehlgeschlagen (Dateizugriff oder Parser).",
              file=sys.stderr)
        sys.exit(2)
