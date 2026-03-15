# Design Philosophy

## Mission

Metreja is a .NET call-path profiler built for machines. It gives AI agents and automated scripts the ability to measure, analyze, and compare application performance without human intervention.

## Audience

The CLI is designed for two consumers:

- **AI agents** that drive profiling sessions as part of autonomous performance analysis workflows
- **Scripts and pipelines** that run profiling in CI, benchmarks, or automated regression detection

Metreja is not designed for human-interactive use. Developers who want to explore profiles visually should use established UI-based profilers such as dotTrace, PerfView, or Visual Studio Diagnostics. Metreja exists because those tools cannot be driven programmatically — they require a human in the seat.

## Core Design Principles

### The CLI is a data layer, not an intelligence layer

The CLI profiles, captures, aggregates, and surfaces data. It never interprets what that data means for a specific codebase. It does not suggest fixes, classify methods by domain role, or embed heuristics about what constitutes "too slow." That intelligence belongs to the consumer — whether that's an AI agent, a skill, or a custom script.

### Structured data aggregation is in scope

Sorting, filtering, ranking, computing timing deltas, and formatting output are CLI responsibilities. These operations transform raw trace data into queryable results without making judgments. The line is clear: factual computation stays in the CLI; interpretation stays out.

Consumers can also read and analyze the raw NDJSON output directly when needed. However, the CLI's aggregation commands should be the primary interface — they are faster, more efficient, and produce structured results that reduce the amount of work the consumer has to do. When a consumer finds themselves repeatedly processing raw output for a use case the CLI doesn't cover, that's a signal to propose a new aggregation command.

### Programmatic-first

Every operation is a shell command. Every output is machine-readable. No interactive prompts, no TUI, no GUI. The CLI assumes its caller can parse structured text and make decisions — it does not assume its caller is a human.

### Lightweight

Metreja is fast enough to fit into the daily development cycle. A profiling session should impose minimal overhead on the profiled application and minimal setup cost on the caller, so that profiling becomes a routine part of development rather than a special occasion. Heavy instrumentation and deep interactive exploration are left to full-featured profilers.

## Goals

- **Enable agent-driven performance analysis.** Give AI agents the tools to run a complete measure, analyze, compare loop without human intervention.
- **Fit into the daily development cycle.** Profiling should be as routine as running tests — fast to set up, fast to execute, fast to analyze.
- **Provide structured, aggregated output.** Surface profiling data in a form that agents and scripts can consume efficiently, reducing the need to parse raw traces.
- **Be the data foundation, not the full stack.** Provide reliable, accurate profiling data that any consumer — skills, scripts, or custom tooling — can build on top of.

## Non-Goals

- **Not a replacement for full-featured profilers.** Tools like dotTrace, PerfView, and Visual Studio Diagnostics serve developers who want deep interactive exploration. Metreja does not compete with them.
- **Not a monitoring or APM tool.** No always-on production instrumentation, no dashboards, no alerting.
- **Not an analysis engine.** No built-in recommendations, no "this is slow because..." logic. Interpretation belongs to the consumer.
- **Not a UI tool.** No TUI, no web dashboard, no visualizations. If you want a visual experience, use a tool built for that.

## Data Format: NDJSON

Metreja uses Newline-Delimited JSON (NDJSON) as its trace output format.

- **Streamable.** Events are written one line at a time as they occur. No buffering an entire trace in memory, no writing a header that depends on knowing the full contents. The profiler can crash mid-session and every line written so far is still valid.
- **Parseable with standard tools.** Any language, any platform, any script can read JSON. No proprietary format, no SDK dependency, no deserialization library to install.
- **One event per line.** Each line is self-contained. Consumers can grep, filter, count, or stream events without understanding the full trace structure.
- **Appendable.** Multiple profiling sessions can write to the same file or be concatenated trivially.

NDJSON is the current wire format. The format could change if a better option emerges. Consumers should rely on the CLI's aggregation commands as their primary interface rather than coupling tightly to the raw format. The CLI is the stable contract.

## Relationship Between CLI and Skills

The CLI documentation is the contract. Any agent that can read the docs and execute shell commands can use Metreja directly.

The recommended way to use Metreja is through the `metreja-profiler` agent skill, which encodes knowledge of how to use the CLI effectively — how to configure sessions for common scenarios, which analysis commands to run, and how to interpret results. It handles the orchestration so the agent can focus on solving the performance problem.

Anyone can also build their own skill or integration by reading the CLI documentation. The CLI has no awareness of skills or agents. It receives commands, produces output, and exits. This separation keeps the CLI simple and ensures it remains useful as a building block for any automation approach.
