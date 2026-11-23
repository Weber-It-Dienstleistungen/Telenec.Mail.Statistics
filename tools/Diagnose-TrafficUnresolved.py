#!/usr/bin/env python3
"""Nur lesende, aggregierte Diagnose der nicht zugeordneten SMTP-Annahmen.

Keine Mailadressen, Queue-IDs, Nachrichteninhalte oder Einzelereignisse
in der Konsolenausgabe. Benutzt die Parser-Funktionen des produktiven
Traffic-Collectors, veraendert aber weder Snapshot noch Mailserver.
"""
from __future__ import annotations

import collections
import datetime as dt
import re
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


def classify_event(process: str, details: str) -> str:
    """Only predefined event categories; never retain or print raw details."""
    if process == "smtpd":
        if details.startswith("client="):
            return "smtpd_client"
        if "milter-reject:" in details:
            return "smtpd_milter_reject"
        if "reject:" in details:
            return "smtpd_reject"
        if "warning:" in details:
            return "smtpd_warning"
        return "smtpd_other"
    if process == "cleanup":
        if details.startswith("milter-reject:"):
            return "cleanup_milter_reject"
        if details.startswith("message-id="):
            return "cleanup_message_id"
        if details.startswith("filter:"):
            return "cleanup_filter"
        if details.startswith("hold:"):
            return "cleanup_hold"
        if details.startswith("discard:"):
            return "cleanup_discard"
        return "cleanup_other"
    if process == "qmgr":
        return "qmgr"
    if process in {"bounce", "defer", "error", "discard",
                   "smtp", "lmtp", "pipe", "local", "virtual"}:
        return "other_" + process
    return "other_process"


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
    # The original productive parser does not retain smtpd PID.
    # Diagnostic-only: compare queue-stamped smtpd lines with
    # unkeyed connection events from the same process, within
    # the short observation window.
    accepted_processes = collections.defaultdict(set)
    accepted_by_pid = collections.defaultdict(set)
    connection_events = collections.defaultdict(list)
    smtpd_pattern = re.compile(
        r"postfix/smtpd\[(?P<pid>\d+)\]:\s+(?P<details>.*)"
    )
    confirmed = collections.defaultdict(set)
    all_qmgr = collections.defaultdict(list)
    milter = collections.defaultdict(set)
    cleanup = collections.defaultdict(list)
    processes = collections.defaultdict(list)
    # Store only event kinds + times, never raw log lines, names or bodies.
    lifecycle = collections.defaultdict(list)
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

            smtpd_match = smtpd_pattern.search(line)
            if smtpd_match:
                pid = smtpd_match.group("pid")
                text = smtpd_match.group("details")
                # Preserve only a coarse event type, NEVER the
                # address, user, IP, payload or raw disconnect line.
                kind = None
                if text.startswith("connect from "):
                    kind = "connect"
                elif text.startswith("disconnect from "):
                    data = re.search(r"(?:^|\s)data=(\d+)/(\d+)(?:\s|$)", text)
                    if data:
                        sent, attempted = map(int, data.groups())
                        kind = ("disconnect_data_used" if sent > 0 else
                                "disconnect_data_failed" if attempted > 0 else
                                "disconnect_no_data_attempt")
                    else:
                        kind = "disconnect_no_data_count"
                elif text.startswith("lost connection after "):
                    kind = "lost_connection"
                elif text.startswith("timeout after "):
                    kind = "timeout"
                elif "NOQUEUE: reject:" in text:
                    kind = "noqueue_reject"
                elif "milter-reject:" in text:
                    kind = "milter_reject"
                if kind:
                    connection_events[pid].append((at, kind))

            match = post.search(line)
            if match is None:
                continue

            scanned += 1
            qid = match.group("queue")
            process = match.group("process")
            details = match.group("details")
            lifecycle[qid].append((at, classify_event(process, details)))

            # Bei der Diagnose wird nur die Plausibilitaet
            # des SASL-Wertes geprueft, nicht der Wert gespeichert.
            if (process == "smtpd"
                    and details.startswith("client=")
                    and "sasl_username=" in details):
                sasl = api["SASL"].search(details)
                if sasl and api["normalize_account"](sasl.group("account")):
                    accepted[qid].add(at)
                    if smtpd_match:
                        process_id = smtpd_match.group("pid")
                        accepted_processes[(qid, at)].add(process_id)
                        accepted_by_pid[process_id].add((qid, at))

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
    orphan_lifecycle = collections.Counter()
    smtpd_endings = collections.Counter()
    confirmed_session_aborts = collections.Counter()
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

                # Which *other* events exist for these orphaned IDs?
                # Each tag is counted at most once per unresolved
                # acceptance, so counts are comparable to 55.
                other_kinds = {
                    kind for t, kind in lifecycle.get(qid, ())
                    if at <= t < limit and kind != "smtpd_client"
                }
                if not other_kinds:
                    orphan_lifecycle["nur_smtpd_client"] += 1
                for kind in other_kinds:
                    orphan_lifecycle[kind] += 1

                # Connection-wide messages have NO queue ID.
                # Time/PID are hints only: a reused smtpd process
                # may have handled several distinct connections.
                # End at the next known connection start to avoid
                # attributing a later client's disconnect.
                hints = set()
                for pid in accepted_processes.get((qid, at), ()):
                    later_connect = [
                        t for t, kind in connection_events.get(pid, ())
                        if at < t <= limit and kind == "connect"
                    ]
                    close = min([limit, *later_connect])
                    hints.update(
                        kind for t, kind in connection_events.get(pid, ())
                        if at <= t <= close and kind != "connect"
                    )
                if not hints:
                    smtpd_endings["kein_zuordenbares_smtpd_ende"] += 1
                for kind in hints:
                    smtpd_endings[kind] += 1

                # Stronger evidence than loose PID/time hints:
                # require an explicit connect event, no intervening
                # disconnect or new connect, one queued smtpd
                # candidate in the entire connection, and the
                # connection's own disconnect with data=0/N (N>0).
                # A reused PID alone NEVER proves an abort.
                candidate_pids = accepted_processes.get((qid, at), ())
                if len(candidate_pids) != 1:
                    confirmed_session_aborts["pid_unbekannt_oder_mehrdeutig"] += 1
                else:
                    pid = next(iter(candidate_pids))
                    session_events = sorted(
                        connection_events.get(pid, ()),
                        key=lambda event: event[0]
                    )
                    opens = [
                        stamp for stamp, kind in session_events
                        if kind == "connect" and stamp < at
                    ]
                    if not opens:
                        confirmed_session_aborts["kein_eindeutiger_connect_start"] += 1
                    else:
                        opened = max(opens)
                        earlier_ends = [
                            stamp for stamp, kind in session_events
                            if kind.startswith("disconnect")
                            and opened <= stamp <= at
                        ]
                        later_opens = [
                            stamp for stamp, kind in session_events
                            if kind == "connect" and stamp > at
                        ]
                        next_open = min(later_opens) if later_opens else None
                        ends = [
                            (stamp, kind)
                            for stamp, kind in session_events
                            if kind.startswith("disconnect")
                            and stamp > at
                            and (next_open is None or stamp < next_open)
                        ]
                        if earlier_ends or not ends:
                            confirmed_session_aborts["verbindung_nicht_belegt"] += 1
                        else:
                            ended, end_kind = min(ends)
                            candidates = [
                                (other_qid, stamp)
                                for other_qid, stamp in accepted_by_pid[pid]
                                if opened < stamp < ended
                            ]
                            # In unusual same-second events, the
                            # order is unverifiable: do not classify.
                            boundaries_ambiguous = any(
                                stamp == opened or stamp == ended
                                for _other_qid, stamp in accepted_by_pid[pid]
                            )
                            if (len(candidates) != 1
                                    or candidates[0] != (qid, at)
                                    or boundaries_ambiguous):
                                confirmed_session_aborts["mehrere_oder_unklare_queue_ids"] += 1
                            elif end_kind == "disconnect_data_failed":
                                confirmed_session_aborts["eindeutige_sitzung_data_0_von_n"] += 1
                            else:
                                confirmed_session_aborts["sitzung_ohne_belegten_data_abbruch"] += 1

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
    print("=== Weitere Ereignisse derselben Queue-ID (aggregiert) ===")
    for kind in sorted(orphan_lifecycle):
        print("Ungeklaerte Vorgaenge mit Ereignistyp:", kind,
              orphan_lifecycle[kind])
    print("Event-Typen koennen pro Vorgang mehrfach zutreffen.")
    print("=== SMTPD-Verbindungsabschluesse derselben Prozesse ===")
    print("Nur Hinweise ueber PID/Zeit, keine verifizierte Queue-Zuordnung.")
    for kind in sorted(smtpd_endings):
        print("Ungeklaerte Vorgaenge mit SMTPD-Hinweis:", kind,
              smtpd_endings[kind])
    print("=== Strikte Verbindungszuordnung (kein produktives Urteil) ===")
    for kind in sorted(confirmed_session_aborts):
        print("Sitzungsevidenz:", kind, confirmed_session_aborts[kind])
    print("Eine DATA=0/N-Sitzung beweist keine erfolgreiche Einlieferung.")
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
