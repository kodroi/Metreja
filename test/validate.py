#!/usr/bin/env python3
"""Metreja NDJSON output validator.

Validates:
- Valid JSON per line, first event is run_metadata
- Required fields per event type
- Timestamps monotonically increasing
- deltaNs non-negative
- Enter/leave balance (accounting for exceptions)
- Reports statistics (total events, unique methods, max depth, timing ranges)
"""

import json
import sys
from collections import defaultdict


REQUIRED_FIELDS = {
    "run_metadata": ["event", "tsNs", "pid", "runId"],
    "enter": ["event", "tsNs", "pid", "runId", "tid", "depth", "asm", "ns", "cls", "m", "async"],
    "leave": ["event", "tsNs", "pid", "runId", "tid", "depth", "asm", "ns", "cls", "m", "async", "deltaNs"],
    "exception": ["event", "tsNs", "pid", "runId", "tid", "asm", "ns", "cls", "m", "exType"],
}


def validate(path: str) -> bool:
    errors = []
    warnings = []

    total_events = 0
    event_counts = defaultdict(int)
    unique_methods = set()
    max_depth = 0
    min_ts = float("inf")
    max_ts = 0
    prev_ts = -1
    min_delta = float("inf")
    max_delta = 0
    enter_count = 0
    leave_count = 0
    exception_count = 0
    first_event = None

    # Per-thread enter/leave tracking
    thread_stacks = defaultdict(int)

    try:
        with open(path, "r", encoding="utf-8") as f:
            for line_num, raw_line in enumerate(f, start=1):
                raw_line = raw_line.strip()
                if not raw_line:
                    continue

                # Parse JSON
                try:
                    event = json.loads(raw_line)
                except json.JSONDecodeError as e:
                    errors.append(f"Line {line_num}: Invalid JSON: {e}")
                    continue

                total_events += 1
                event_type = event.get("event", "unknown")
                event_counts[event_type] += 1

                # Track first event
                if first_event is None:
                    first_event = event_type
                    if event_type != "run_metadata":
                        errors.append(f"Line {line_num}: First event must be 'run_metadata', got '{event_type}'")

                # Check required fields
                if event_type in REQUIRED_FIELDS:
                    for field in REQUIRED_FIELDS[event_type]:
                        if field not in event:
                            errors.append(f"Line {line_num}: Missing required field '{field}' in '{event_type}' event")

                # Check timestamps
                ts = event.get("tsNs", 0)
                if ts > 0:
                    min_ts = min(min_ts, ts)
                    max_ts = max(max_ts, ts)
                    if ts < prev_ts and event_type != "run_metadata":
                        warnings.append(f"Line {line_num}: Timestamp {ts} < previous {prev_ts} (non-monotonic)")
                    prev_ts = ts

                # Event-specific checks
                if event_type == "enter":
                    enter_count += 1
                    depth = event.get("depth", 0)
                    max_depth = max(max_depth, depth)
                    tid = event.get("tid", 0)
                    thread_stacks[tid] += 1

                    method_key = f"{event.get('asm', '')}.{event.get('ns', '')}.{event.get('cls', '')}.{event.get('m', '')}"
                    unique_methods.add(method_key)

                elif event_type == "leave":
                    leave_count += 1
                    tid = event.get("tid", 0)
                    thread_stacks[tid] -= 1

                    delta = event.get("deltaNs", 0)
                    if delta < 0:
                        errors.append(f"Line {line_num}: Negative deltaNs: {delta}")
                    else:
                        min_delta = min(min_delta, delta)
                        max_delta = max(max_delta, delta)

                    method_key = f"{event.get('asm', '')}.{event.get('ns', '')}.{event.get('cls', '')}.{event.get('m', '')}"
                    unique_methods.add(method_key)

                elif event_type == "exception":
                    exception_count += 1

    except FileNotFoundError:
        print(f"ERROR: File not found: {path}")
        return False
    except Exception as e:
        print(f"ERROR: Failed to read file: {e}")
        return False

    # Check enter/leave balance
    unbalanced_threads = {tid: count for tid, count in thread_stacks.items() if count != 0}
    if unbalanced_threads and exception_count == 0:
        for tid, count in unbalanced_threads.items():
            warnings.append(f"Thread {tid}: {count} unmatched enter(s) (no exceptions to account for)")

    # Report
    print(f"=== Metreja NDJSON Validation: {path} ===")
    print()

    if errors:
        print(f"ERRORS ({len(errors)}):")
        for err in errors[:20]:
            print(f"  {err}")
        if len(errors) > 20:
            print(f"  ... and {len(errors) - 20} more")
        print()

    if warnings:
        print(f"WARNINGS ({len(warnings)}):")
        for warn in warnings[:10]:
            print(f"  {warn}")
        if len(warnings) > 10:
            print(f"  ... and {len(warnings) - 10} more")
        print()

    print("Statistics:")
    print(f"  Total events:    {total_events}")
    for evt_type, count in sorted(event_counts.items()):
        print(f"    {evt_type}: {count}")
    print(f"  Unique methods:  {len(unique_methods)}")
    print(f"  Max call depth:  {max_depth}")
    print(f"  Enter count:     {enter_count}")
    print(f"  Leave count:     {leave_count}")
    print(f"  Exception count: {exception_count}")
    if min_ts != float("inf"):
        print(f"  Timestamp range: {min_ts} - {max_ts} ({(max_ts - min_ts) / 1_000_000:.2f} ms)")
    if min_delta != float("inf"):
        print(f"  Delta range:     {min_delta} - {max_delta} ns")

    print()
    if errors:
        print(f"RESULT: FAILED ({len(errors)} error(s), {len(warnings)} warning(s))")
        return False
    else:
        print(f"RESULT: PASSED ({len(warnings)} warning(s))")
        return True


if __name__ == "__main__":
    if len(sys.argv) != 2:
        print(f"Usage: {sys.argv[0]} <output.ndjson>")
        sys.exit(1)

    success = validate(sys.argv[1])
    sys.exit(0 if success else 1)
