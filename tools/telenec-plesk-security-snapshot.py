#!/usr/bin/env python3
"""Telenec Mail Security: read-only Plesk statistics -> protected JSON snapshot.

The Plesk command is fixed, never derived from user input.
Default: diagnostic only. With --write --output, the only effect is
an atomic replacement of the expressly configured snapshot file.
No mailbox or mail-server settings are changed.
"""
from __future__ import annotations

import argparse
import datetime as dt
import json
import os
from pathlib import Path
import pwd
import subprocess
import sys
import tempfile

UTC = dt.timezone.utc
COMMAND = (
    "plesk", "sbin", "mailmng-outgoing",
    "--fetch-statistics", "--with-limits", "--time-step=hour"
)
MAX_SOURCE_BYTES = 64 * 1024 * 1024
MAX_SNAPSHOT_BYTES = 2 * 1024 * 1024
MAX_ACCOUNTS = 150_000
MAX_ACTIVE = 5_000


class SourceError(Exception):
    pass


def exact_nonnegative_int(value, allow_missing=False):
    if value is None and allow_missing:
        return None
    if type(value) is not int or value < 0:
        raise SourceError("Unerwarteter Zahlenwert in der Plesk-Statistik.")
    return value


def normal_email(raw, domain):
    if not isinstance(raw, str):
        raise SourceError("Postfachkennung ist kein Text.")
    value = raw.strip().lower()
    if "@" not in value:
        if not domain:
            raise SourceError(
                "Plesk liefert einen lokalen Postfachnamen ohne Domainzuordnung."
            )
        value = value + "@" + domain

    if (len(value) > 254 or len(value) < 3 or value.count("@") != 1
            or any(c.isspace() or ord(c) < 32 for c in value)):
        raise SourceError("Ungueltige Postfachkennung in Plesk-Daten.")
    local, host = value.rsplit("@", 1)
    if not local or "." not in host:
        raise SourceError("Ungueltige Domain in Postfachkennung.")
    return value


def collect_mailboxes(root):
    accounts = []
    seen = set()
    nodes = 0
    stack = [(root, None)]
    while stack:
        node, domain = stack.pop()
        nodes += 1
        if nodes > 250_000:
            raise SourceError("Plesk-Schema ist zu gross oder unerwartet.")
        if isinstance(node, list):
            stack.extend((item, domain) for item in reversed(node))
            continue
        if not isinstance(node, dict):
            continue

        raw_name = node.get("name")
        if (isinstance(raw_name, str)
                and "." in raw_name and "@" not in raw_name
                and "/" not in raw_name and len(raw_name) <= 253):
            # Domain-Objekte tragen ihren eigenen Namen.
            # Andere Objekte mit domainartigem Namen sind keine
            # eigenstaendigen Postfaecher.
            domain = raw_name.strip().lower()

        if "mails" in node:
            mails = node["mails"]
            if not isinstance(mails, list):
                raise SourceError("Plesk-Schema: mails muss eine Liste sein.")
            for mail in mails:
                if not isinstance(mail, dict):
                    raise SourceError("Plesk-Schema: Mailobjekt nicht erkannt.")
                account = normal_email(mail.get("name"), domain)
                if account in seen:
                    raise SourceError(
                        "Doppelte Postfachkennung in der Plesk-Ausgabe."
                    )
                seen.add(account)
                accounts.append((account, mail))
                if len(accounts) > MAX_ACCOUNTS:
                    raise SourceError("Zu viele Postfachobjekte.")
        for key, value in node.items():
            if key != "mails" and isinstance(value, (dict, list)):
                stack.append((value, domain))
    if not accounts:
        raise SourceError("Keine Plesk-Postfaecher gefunden.")
    return accounts


def parse_snapshot(source):
    accounts = collect_mailboxes(source)
    active = []
    ignored_empty_periods = 0
    accounts_without_usable_periods = 0
    for account, mail in accounts:
        limit = mail.get("out_limit")
        if type(limit) is not int or limit < 0:
            raise SourceError("Unerwarteter Plesk-Versandlimitwert.")
        stats = mail.get("stats")
        if not isinstance(stats, list) or len(stats) > 100:
            raise SourceError("Unerwartete Plesk-Statistikliste.")
        if not stats:
            continue

        periods = []
        total_passed = 0
        total_rejected = 0
        has_passed = False
        has_rejected = False
        distinct = set()
        for item in stats:
            if not isinstance(item, dict):
                raise SourceError("Unerwarteter Plesk-Stundenwert.")
            raw_time = item.get("time")
            if (not isinstance(raw_time, str) or not raw_time
                    or len(raw_time) > 64
                    or any(ord(char) < 32 for char in raw_time)):
                raise SourceError("Unerwartetes Plesk-Zeitformat.")
            if raw_time in distinct:
                raise SourceError("Doppelter Stundenwert desselben Postfachs.")
            distinct.add(raw_time)
            passed = exact_nonnegative_int(item.get("passed"), True)
            rejected = exact_nonnegative_int(item.get("rejected"), True)
            if passed is None and rejected is None:
                # Plesk kann Statistikzeilen liefern, die lediglich
                # "time" oder bislang nicht ausgewertete Zusatzfelder
                # (z. B. passed_fixed) enthalten. Nicht als 0 werten,
                # sondern gesondert zaehlen und ohne Abbruch auslassen.
                ignored_empty_periods += 1
                continue
            if passed is not None:
                has_passed = True
                total_passed += passed
            if rejected is not None:
                has_rejected = True
                total_rejected += rejected
            periods.append({
                "time": raw_time,
                "passed": passed,
                "rejected": rejected,
            })
        if not periods:
            accounts_without_usable_periods += 1
            continue

        active.append({
            "account": account,
            "outLimit": limit,
            "passed": total_passed if has_passed else None,
            "rejected": total_rejected if has_rejected else None,
            "periods": sorted(periods, key=lambda row: row["time"]),
        })
        if len(active) > MAX_ACTIVE:
            raise SourceError("Zu viele aktive Accounts im Snapshot.")

    active.sort(key=lambda account: (
        -(account["rejected"] or 0),
        -(account["passed"] or 0),
        account["account"]
    ))
    return {
        "generatedAtUtc": dt.datetime.now(UTC).isoformat().replace("+00:00", "Z"),
        "source": "plesk-mailmng-outgoing-hour",
        "mailboxCount": len(accounts),
        "accounts": active,
        "_diagnostic": {
            "ignoredEmptyPeriods": ignored_empty_periods,
            "accountsWithoutUsablePeriods": accounts_without_usable_periods,
        },
    }


def fetch_from_plesk(timeout):
    try:
        proc = subprocess.run(
            COMMAND, capture_output=True, check=False, timeout=timeout
        )
    except (OSError, subprocess.TimeoutExpired) as exc:
        raise SourceError("Plesk-Abfrage konnte nicht ausgefuehrt werden.") from exc
    if proc.returncode != 0:
        # Never print stderr: it may contain private account identifiers.
        raise SourceError(
            f"Plesk-Abfrage fehlgeschlagen (Exit-Code {proc.returncode})."
        )
    if not proc.stdout or len(proc.stdout) > MAX_SOURCE_BYTES:
        raise SourceError("Plesk-Daten sind leer oder zu umfangreich.")
    try:
        return json.loads(proc.stdout)
    except (UnicodeDecodeError, ValueError) as exc:
        raise SourceError("Plesk lieferte kein gueltiges JSON.") from exc


def write_atomically(snapshot, path, owner):
    path = Path(path)
    if not path.is_absolute() or not path.parent.is_dir():
        raise SourceError("Snapshot-Pfad muss absolut sein und der Ordner existieren.")
    try:
        user = pwd.getpwnam(owner)
    except KeyError as exc:
        raise SourceError("Der angegebene Snapshot-Benutzer existiert nicht.") from exc
    payload = (json.dumps(snapshot, ensure_ascii=False, separators=(",", ":"))
               + "\n").encode("utf-8")
    if len(payload) > MAX_SNAPSHOT_BYTES:
        raise SourceError("Snapshot ueberschreitet den zulaessigen Grenzwert.")
    fd, name = tempfile.mkstemp(prefix=".mail-security-", dir=path.parent)
    try:
        os.fchmod(fd, 0o600)
        os.fchown(fd, user.pw_uid, user.pw_gid)
        with os.fdopen(fd, "wb") as file:
            file.write(payload)
            file.flush()
            os.fsync(file.fileno())
        os.replace(name, path)
    finally:
        if os.path.exists(name):
            os.unlink(name)


def main():
    parser = argparse.ArgumentParser(
        description="Plesk-Sicherheitsstatistiken lesen; kein Eingriff in Postfaecher."
    )
    parser.add_argument("--timeout", type=int, default=120)
    parser.add_argument("--write", action="store_true")
    parser.add_argument("--output")
    parser.add_argument("--owner", default="telenecstats")
    args = parser.parse_args()
    if not 1 <= args.timeout <= 600:
        parser.error("Zeitlimit muss zwischen 1 und 600 Sekunden liegen.")
    if args.write != bool(args.output):
        parser.error("--write und --output muessen gemeinsam angegeben werden.")
    try:
        snapshot = parse_snapshot(fetch_from_plesk(args.timeout))
        print("=== Telenec Mail Security – Plesk-Snapshot ===")
        print("Mailobjekte:", snapshot["mailboxCount"])
        print("Accounts mit Plesk-Statistik:", len(snapshot["accounts"]))
        print("Accounts mit abgewiesenen Versandversuchen:",
              sum(1 for row in snapshot["accounts"]
                  if (row["rejected"] or 0) > 0))
        print("Plesk-Zeilen ohne passed/rejected (nicht als 0 gezaehlt):",
              snapshot["_diagnostic"]["ignoredEmptyPeriods"])
        print("Accounts nur mit nicht auswertbaren Zeilen:",
              snapshot["_diagnostic"]["accountsWithoutUsablePeriods"])
        print("Plesk-Statistik: Stundenbezogene Quellwerte, kein 24h-Vollbild.")
        if args.write:
            write_atomically(snapshot, args.output, args.owner)
            print("Geschuetzter Snapshot aktualisiert; keine Accountnamen ausgegeben.")
        else:
            print("Diagnose: keine Datei geschrieben und kein Dienst geaendert.")
        return 0
    except (SourceError, OSError) as exc:
        print(f"FEHLER: {exc}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main())
