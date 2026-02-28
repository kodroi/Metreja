#!/usr/bin/env python3
"""Validate Metreja NDJSON trace files for structural correctness."""

import json
import sys

REQUIRED_BASE_FIELDS = {"event", "tsNs", "pid", "sessionId"}

EVENT_FIELDS = {
    "session_metadata": {"scenario"},
    "enter": {"tid", "depth", "asm", "ns", "cls", "m", "async"},
    "leave": {"tid", "depth", "asm", "ns", "cls", "m", "async", "deltaNs"},
    "exception": {"tid", "asm", "ns", "cls", "m", "exType"},
    "gc_start": {"gen0", "gen1", "gen2", "reason"},
    "gc_end": {"durationNs"},
    "alloc_by_class": {"tid", "className", "count"},
}


def validate_file(path: str) -> tuple[int, int]:
    errors = 0
    line_count = 0

    with open(path, "r", encoding="utf-8") as f:
        for line_num, line in enumerate(f, start=1):
            line = line.strip()
            if not line:
                continue

            line_count += 1

            try:
                obj = json.loads(line)
            except json.JSONDecodeError as e:
                print(f"  ERROR line {line_num}: invalid JSON: {e}")
                errors += 1
                continue

            missing_base = REQUIRED_BASE_FIELDS - set(obj.keys())
            if missing_base:
                print(f"  ERROR line {line_num}: missing base fields: {missing_base}")
                errors += 1

            event_type = obj.get("event")
            if event_type not in EVENT_FIELDS:
                print(f"  ERROR line {line_num}: unknown event type: {event_type}")
                errors += 1
                continue

            required = EVENT_FIELDS[event_type]
            missing = required - set(obj.keys())
            if missing:
                print(f"  ERROR line {line_num}: {event_type} missing fields: {missing}")
                errors += 1

    return line_count, errors


def main() -> int:
    if len(sys.argv) != 2:
        print(f"Usage: {sys.argv[0]} <ndjson-file>")
        return 1

    path = sys.argv[1]
    print(f"Validating: {path}")

    line_count, errors = validate_file(path)

    if errors == 0:
        print(f"  PASSED: {line_count} events validated, 0 errors.")
        return 0
    else:
        print(f"  FAILED: {line_count} events, {errors} error(s).")
        return 1


if __name__ == "__main__":
    sys.exit(main())
