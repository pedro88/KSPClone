# Specs — spec-driven development

The spec is the contract; code follows it. Pipeline and artifacts:

```
constitution → spec → plan → roadmap (milestone → slice → task) → implement
   (rules)    (what)  (how)        (execution breakdown)           (code)
```

| File | Role | Changes when |
|---|---|---|
| [constitution.md](./constitution.md) | Non-negotiable principles | An architectural principle is amended |
| [spec.md](./spec.md) | *What* the system does (EARS, testable, no tech) | Behaviour changes |
| [plan.md](./plan.md) | *How* — tech mapped to requirements + ADRs | Technology changes |
| [roadmap.md](./roadmap.md) | Milestones → slices → tasks, each citing requirements | Execution order changes |
| [tasks/](./tasks/) | Code-ready tickets (81), expanded per milestone | A ticket is added/refined |
| [../CONTEXT.md](../CONTEXT.md) | Glossary (ubiquitous language) | A term is coined/sharpened |
| [../docs/adr/](../docs/adr/) | Architecture decisions + rationale | A hard-to-reverse decision is made |

## The four levels

- **Spec** — a single source of truth in EARS. Every requirement has a stable ID and is testable. No technology. Lives in `spec.md`.
- **Milestone** — a demonstrable increment. Its exit criterion names the requirements that must be *live and verified*. Lives in `roadmap.md`.
- **Slice** — a thin vertical path (server → wire → client) delivering a subset of a milestone's requirements. The thing you actually build and demo. Smallest first.
- **Task** — atomic. Cites the requirement(s) it satisfies and a concrete acceptance check. A task is done when its check passes and its requirement is verifiable.

## Traceability rule

Every task → cites a requirement (REQ-ID). Every plan decision → cites the requirement it satisfies + the ADR that records it. This is what lets the spec stay the source of truth: you can always answer "why does this code exist?" by walking task → requirement → constitution.

## Working loop

1. Pick the next **slice** in `roadmap.md` (top-down; spine first).
2. Resolve any **open item** that gates it (see the table at the bottom of `roadmap.md`).
3. Implement its **tasks**; each is green when its acceptance check passes.
4. When all a milestone's slices pass, verify the **exit criterion** requirements end-to-end → milestone done.
5. If behaviour needs to change, edit `spec.md` *first*, then code — never the reverse.
