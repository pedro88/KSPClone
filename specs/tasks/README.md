# Code-ready tasks

Fine-grained tickets expanded from [../roadmap.md](../roadmap.md). Each ticket is implementable without further design questions and cites the [spec.md](../spec.md) requirement(s) it satisfies. Terms in *italics* are defined in [../../CONTEXT.md](../../CONTEXT.md).

| Milestone | File | Tickets | IDs |
|---|---|---|---|
| M0 — Skeleton (the spine) | [M0.md](./M0.md) | 23 | M0-T01 … M0-T23 |
| M1 — Physics bubble + prediction | [M1.md](./M1.md) | 19 | M1-T01 … M1-T19 |
| M2 — Multi-crew stations | [M2.md](./M2.md) | 11 | M2-T01 … M2-T11 |
| M2.5 — Presentation & surface | [M2.5-celestial-skybox.md](./M2.5-celestial-skybox.md) | 2 | M2.5-T01 … M2.5-T02 |
| M3 — Collaborative VAB | [M3.md](./M3.md) | 8 | M3-T01 … M3-T08 |
| M4 — Comms & ground control | [M4.md](./M4.md) | 12 | M4-T01 … M4-T12 |
| M5 — Progression (Science mode) | [M5.md](./M5.md) | 8 | M5-T01 … M5-T08 |
| **Total** | | **83** | |

Status (code): M0–M3 + M2.5 code-complete; M4/M5 not started. Live detail in [../roadmap.md](../roadmap.md).

## Ticket format

```
### M{n}-T{nn} — <imperative title>
- Satisfies:   REQ-IDs from spec.md
- Depends on:  earlier ticket IDs (or —)
- Blocked by:  open plan/spec item (only where applicable)
- Intent:      what & why, 1–2 sentences
- Touches:     concrete C#/Unity systems + Postgres tables
- Steps:       concrete implementation steps
- Acceptance:  observable, testable check
- Est:         S | M | L
```

## Coverage

All 49 spec requirements are cited by at least one ticket; no ticket cites a requirement that doesn't exist in `spec.md`. Verify with:

```sh
python3 - <<'EOF'
import re,glob
spec=set(re.findall(r'\*\*((?:TIME|ORBIT|PHYS|SUSP|NET|CREW|COMMS|BUILD|PROG|PERSIST)-\d+)\*\*',open('specs/spec.md').read()))
cited=set()
for f in glob.glob('specs/tasks/M*.md'): cited|=set(re.findall(r'(?:TIME|ORBIT|PHYS|SUSP|NET|CREW|COMMS|BUILD|PROG|PERSIST)-\d+',open(f).read()))
print("uncovered:", sorted(spec-cited) or "none"); print("phantom:", sorted(cited-spec) or "none")
EOF
```

## Order of work

Top-down, spine first (Constitution Art. 10): M0 → M1 → M2 → M3 → M4, with M5 interleavable from M1 once tech needs meaning. Within a milestone, follow slice order and ticket `Depends on`. Resolve any `Blocked by` open item (see the table at the bottom of [../roadmap.md](../roadmap.md)) before starting a blocked ticket.
