#!/usr/bin/env python3
"""Fail a CI job unless a VSTest TRX contains a complete, non-empty green run."""

from __future__ import annotations

import argparse
import pathlib
import sys
import xml.etree.ElementTree as ET


def counter_value(counters: ET.Element, name: str) -> int:
    try:
        return int(counters.attrib.get(name, "0"))
    except ValueError as exc:
        raise ValueError(f"TRX counter {name!r} is not an integer") from exc


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--path", required=True, type=pathlib.Path)
    args = parser.parse_args()

    if not args.path.is_file():
        print(f"TRX_NOT_FOUND path={args.path}", file=sys.stderr)
        return 2

    try:
        root = ET.parse(args.path).getroot()
        counters = next((node for node in root.iter() if node.tag.rsplit("}", 1)[-1] == "Counters"), None)
        if counters is None:
            raise ValueError("TRX Counters element is missing")

        total = counter_value(counters, "total")
        executed = counter_value(counters, "executed")
        passed = counter_value(counters, "passed")
        failed = sum(counter_value(counters, name) for name in ("failed", "error", "timeout", "aborted"))
        skipped = sum(counter_value(counters, name) for name in ("notExecuted", "inconclusive"))
    except (ET.ParseError, OSError, ValueError) as exc:
        print(f"TRX_INVALID reason={exc}", file=sys.stderr)
        return 2

    print(f"TRX_COUNTERS total={total} executed={executed} passed={passed} failed={failed} skipped={skipped}")
    if total <= 0 or executed != total or passed != total or failed != 0 or skipped != 0:
        print("TRX_REGRESSION_FAIL expected a non-empty run with every test passed", file=sys.stderr)
        return 1
    print("TRX_REGRESSION_PASS")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
