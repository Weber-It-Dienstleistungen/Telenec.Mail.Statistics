#!/usr/bin/env python3
"""
Telenec Mail Security Center – Plesk Outgoing Mail Control, Lese-Collector.

Erster Baustein: Nur Analyse, kein Schreiben, keine Sperren, keine E-Mails.
Ruft die vorhandene Plesk-Schnittstelle mit festen Argumenten auf und
untersucht deren Datenstruktur und aggregierte Zahlen.

Bewusst KEIN fertiger Abuse-Detector:
- Plesk-Statistiken sind nicht identisch mit Postfix-Einlieferungen.
- Die interne Plesk-JSON-Schnittstelle ist nicht versionsstabil.
- Ein Wert "passed" allein beweist keinen erfolgreichen Mailversand.
- Ein out_limit ist nicht automatisch das tatsächlich wirksame Limit
  (Domain und Abonnement können zusätzlich begrenzen).
"""

from __future__ import annotations

import argparse
import collections
import json
import subprocess
import sys
from pathlib import Path
from typing import Any

COMMAND = (
    "plesk",
    "sbin",
    "mailmng-outgoing",
    "--fetch-statistics",
    "--with-limits",
    "--time-step=hour",
)

MAX_JSON_BYTES = 64 * 1024 * 1024
MAX_ACCOUNTS = 150_000
MAX_STATS_PER_ACCOUNT = 35_000
MAX_NODES = 250_000


class CollectorError(Exception):
    pass


def is_int(value: Any) -> bool:
    return type(value) is int


def read_plesk_json(timeout: int) -> Any:
    try:
        process = subprocess.run(
            COMMAND, capture_output=True, timeout=timeout, check=False
        )
    except (OSError, subprocess.TimeoutExpired) as exc:
        raise CollectorError(
            "Plesk-Auswertung konnte nicht innerhalb des Zeitlimits "
            "ausgefuehrt werden."
        ) from exc

    if process.returncode != 0:
        # Das Plesk-stderr kann kundenbezogene Daten enthalten:
        # Hier bewusst weder stdout noch stderr ausgeben.
        raise CollectorError(
            f"Plesk-Abfrage fehlgeschlagen (Exit-Code {process.returncode})."
        )

    if not process.stdout or len(process.stdout) > MAX_JSON_BYTES:
        raise CollectorError(
            "Plesk hat leere oder zu grosse JSON-Daten zurueckgegeben."
        )

    try:
        return json.loads(process.stdout)
    except (UnicodeDecodeError, ValueError) as exc:
        raise CollectorError(
            "Plesk-Ausgabe ist kein gueltiges JSON. Keine Sicherheitswertung."
        ) from exc


def find_mail_records(tree: Any) -> tuple[list[dict], int]:
    """
    Findet ausschliesslich konkrete Eintraege in 'mails'-Arrays.
    Andere Plesk-Objekte (Subscriptions, Domains) werden NICHT als
    einzelne Mailaccounts interpretiert.
    """
    accounts: list[dict] = []
    domain_count = 0
    visited = 0
    stack = [tree]

    while stack:
        node = stack.pop()
        visited += 1
        if visited > MAX_NODES:
            raise CollectorError("Plesk-Datenstruktur ueberschreitet den Grenzwert.")

        if isinstance(node, list):
            stack.extend(reversed(node))
            continue
        if not isinstance(node, dict):
            continue

        if isinstance(node.get("domains"), list):
            domain_count += len(node["domains"])

        if "mails" in node:
            mails = node["mails"]
            if not isinstance(mails, list):
                raise CollectorError("Plesk-Schema geaendert: mails ist keine Liste.")

            for mail in mails:
                if not isinstance(mail, dict):
                    raise CollectorError("Plesk-Schema geaendert: Postfach ist kein Objekt.")
                accounts.append(mail)
                if len(accounts) > MAX_ACCOUNTS:
                    raise CollectorError("Ungewoehnlich viele Mailobjekte.")

        for key, value in node.items():
            if key != "mails" and isinstance(value, (dict, list)):
                stack.append(value)

    if not accounts:
        raise CollectorError(
            "Keine mails-Datensaetze gefunden: Schema oder Berechtigung pruefen."
        )

    return accounts, domain_count


def inspect(root: Any) -> None:
    accounts, domain_count = find_mail_records(root)
    limits = collections.Counter()
    account_fields = collections.Counter()
    stats_fields = collections.Counter()
    time_types = collections.Counter()
    unknown_stats = 0
    stats_rows = 0
    rows_with_passed = 0
    rows_with_rejected = 0
    passed_sum = 0
    rejected_sum = 0
    nonempty_accounts = 0
    invalid_accounts = 0
    invalid_stats = 0
    accounts_at_reported_limit = 0
    accounts_with_explicit_rejections = 0

    for mail in accounts:
        for field in mail:
            if isinstance(field, str) and field.isidentifier():
                account_fields[field] += 1

        name = mail.get("name")
        limit = mail.get("out_limit")
        stats = mail.get("stats")
        if not isinstance(name, str) or not name.strip() or not is_int(limit):
            invalid_accounts += 1
            continue

        limits[limit] += 1

        if not isinstance(stats, list) or len(stats) > MAX_STATS_PER_ACCOUNT:
            invalid_stats += 1
            continue

        if stats:
            nonempty_accounts += 1

        has_passed_at_limit = False
        has_rejected = False

        for row in stats:
            if not isinstance(row, dict):
                invalid_stats += 1
                continue

            stats_rows += 1
            for field in row:
                if isinstance(field, str) and field.isidentifier():
                    stats_fields[field] += 1

            if "time" in row:
                time_types[type(row["time"]).__name__] += 1

            passed = row.get("passed")
            if is_int(passed) and passed >= 0:
                rows_with_passed += 1
                passed_sum += passed
                # Zeitscheibenwerte sind keine gleitenden 60-Minuten-Limits.
                # Dieser Wert ist nur ein Suchhinweis, keine Sperrgrundlage.
                if limit > 0 and passed >= limit:
                    has_passed_at_limit = True
            elif passed is not None:
                invalid_stats += 1

            rejected = row.get("rejected")
            if is_int(rejected) and rejected >= 0:
                rows_with_rejected += 1
                rejected_sum += rejected
                if rejected > 0:
                    has_rejected = True
            elif rejected is not None:
                invalid_stats += 1
            if "rejected" not in row:
                unknown_stats += 1

        if has_passed_at_limit:
            accounts_at_reported_limit += 1
        if has_rejected:
            accounts_with_explicit_rejections += 1

    if invalid_accounts or invalid_stats:
        raise CollectorError(
            "Plesk-Daten enthalten unerwartete Feldtypen. "
            f"Betroffene Mailobjekte: {invalid_accounts}; "
            f"ungueltige Statistikangaben: {invalid_stats}. "
            "Es wurde keine Sicherheitsbewertung erstellt."
        )

    print("=== Telenec Mail Security – Plesk-Diagnose (nur lesend) ===")
    print("Mailobjekte:", len(accounts))
    print("Domain-Zuordnungen in Plesk-Struktur:", domain_count)
    print("Mailobjekte mit nichtleerer stats-Liste:", nonempty_accounts)
    print("Statistikzeilen insgesamt:", stats_rows)
    print("Anzahl verwendeter Versandlimits:")
    for limit, count in sorted(limits.items()):
        print(f"  Limit {limit}: {count} Mailobjekte")

    print("Mailobjekt-Felder:", ", ".join(sorted(account_fields)))
    print("Statistik-Felder:", ", ".join(sorted(stats_fields)) or "(keine)")
    print("Datentypen fuer Zeitangaben:",
          ", ".join(f"{k}={v}" for k, v in sorted(time_types.items())) or "(keine)")
    print("Statistikzeilen mit numerischem passed:", rows_with_passed)
    print("Summe der gelieferten passed-Felder:", passed_sum)
    print("Accounts mit einer Zeitscheibe >= individuellem Limit:",
          accounts_at_reported_limit)

    if rows_with_rejected:
        print("Statistikzeilen mit numerischem rejected:", rows_with_rejected)
        print("Summe der gelieferten rejected-Felder:", rejected_sum)
        print("Accounts mit explizitem rejected > 0:",
              accounts_with_explicit_rejections)
    else:
        print(
            "ACHTUNG: Kein numerisches rejected-Feld im bisherigen "
            "Datenmaterial nachgewiesen. Ablehnungen koennen aus "
            "diesem JSON noch nicht sicher ausgewertet werden."
        )

    if unknown_stats:
        print("Zeilen ohne rejected-Feld:", unknown_stats)

    print("Keine Mailadressen, keine Rohdaten, keine Datei ausgegeben.")
    print("Keine Sperren, keine Plesk-Einstellungen geaendert.")


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Plesk Outgoing Mail Control schema- und zaehlungsdiagnostisch lesen."
    )
    parser.add_argument(
        "--timeout", type=int, default=120,
        help="Timeout fuer die Plesk-Abfrage in Sekunden (Default: 120)"
    )
    args = parser.parse_args()

    if not 1 <= args.timeout <= 600:
        parser.error("--timeout muss zwischen 1 und 600 liegen.")

    try:
        root = read_plesk_json(args.timeout)
        inspect(root)
        return 0
    except CollectorError as exc:
        print(f"FEHLER: {exc}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main())
