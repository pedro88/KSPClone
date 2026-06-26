# Spec-Driven Development (SDD) & EARS — Reference

> **Why this matters for us.** We run SDD: `constitution → spec → plan → roadmap → tasks → implement`, with `spec.md` in EARS as the single source of truth (see [`specs/README.md`](../../specs/README.md)).
> This doc collects the authoritative methodology behind that pipeline — GitHub Spec Kit, AWS Kiro, and Mavin's EARS — so we apply it the way its authors intended.
> Use it to sanity-check our artifacts, settle "how should a requirement read?" debates, and decide which tooling (if any) to adopt.

---

## 1. The SDD pipeline

Spec-Driven Development inverts the usual order: **the specification is the primary artifact and code is its expression**, not the other way around. From GitHub's methodology doc: *"The specification becomes the primary artifact. Code becomes its expression in a particular language and framework"* — and *"When specifications and implementation plans generate code, there is no gap — only transformation."*

The pipeline that all the major tools converge on:

| Stage | Question it answers | Tool term (Spec Kit / Kiro) | Our file |
|---|---|---|---|
| **Constitution / steering** | What are the non-negotiable rules? | `constitution.md` / "steering" memory | `specs/constitution.md` |
| **Specify** | *What* should it do? (testable, no tech) | `spec.md` / `requirements.md` | `specs/spec.md` (EARS) |
| **Plan / design** | *How* — tech, architecture, ADRs | `plan.md` / `design.md` | `specs/plan.md` (+ `docs/adr/`) |
| **Tasks** | Ordered, atomic, traceable work | `tasks.md` | `specs/roadmap.md` + `specs/tasks/` |
| **Implement** | Code that satisfies the tasks | `/implement` | the codebase |

**Human checkpoints are the point, not friction.** Each tool inserts review gates between stages: validate the spec for completeness before planning; audit the plan for missing pieces *and over-engineering* before tasks; confirm prerequisites before implementing. The toolkit structures judgment; it does not automate it away.

Three principles underpin the whole approach (Spec Kit):
1. **Intent-driven** — spec defines *what* and *why* before *how*.
2. **Rich specification** with guardrails (the constitution) and organizational principles.
3. **Multi-step refinement**, not one-shot generation from a prompt.

**Bidirectional feedback:** production incidents and learnings should flow *back* into the spec for the next cycle — failures become spec updates, not isolated hotfixes. This is exactly our rule #5: *edit `spec.md` first, then code — never the reverse.*

---

## 2. GitHub Spec Kit (`github/spec-kit`)

Open-source toolkit + CLI ([`specify`](https://github.com/github/spec-kit)) that scaffolds SDD and drives it via slash commands across 30+ AI agents. Tagline: *"focus on product scenarios and predictable outcomes instead of vibe coding."*

**Directory layout (`.specify/`):**
```
.specify/
├── memory/constitution.md      # governing principles
├── scripts/bash/               # automation helpers
├── templates/                  # spec/plan/tasks templates (+ overrides/)
├── extensions/templates/       # added commands
└── presets/templates/          # workflow customizations
```

**Slash-command workflow (in order):**

| Command | Purpose | Maps to our |
|---|---|---|
| `/speckit.constitution` | Establish principles & governance | `constitution.md` |
| `/speckit.specify` | Requirements & user stories | `spec.md` |
| `/speckit.clarify` | Resolve underspecified areas (structured Q&A) | *(manual today)* |
| `/speckit.plan` | Technical roadmap, stack, architecture | `plan.md` |
| `/speckit.tasks` | Ordered tasks w/ dependencies & parallel markers | `roadmap.md` + `tasks/` |
| `/speckit.implement` | Execute tasks | code |
| `/speckit.analyze` | Cross-artifact consistency & coverage review | *(manual today)* |
| `/speckit.checklist` | Generate quality-validation checklists | *(manual today)* |

Plan-stage may also emit `data-model.md`, contracts (`api-spec.json`), `research.md`, `quickstart.md`. The constitution is treated as *"compile-time checks for architectural principles"* — applied to every change regardless of which model generates it.

---

## 3. AWS Kiro (`kiro.dev`)

IDE-based agentic tool whose spec workflow is the leanest of the three: a **trio of markdown files per feature** ([docs/specs](https://kiro.dev/docs/specs/)).

- `requirements.md` — user stories + **EARS** acceptance criteria
- `design.md` — architecture, mermaid sequence diagrams, component interfaces, data models
- `tasks.md` — discrete, trackable implementation tasks

**Three-phase workflow:** Requirements → Design → Tasks, each a reviewable document. Two variants: **Requirements-First** (default) and [**Design-First**](https://kiro.dev/docs/specs/feature-specs/tech-design-first/) (derive requirements from a known architecture); a "Quick Plan" can auto-generate all three for well-understood work. Checkpoints are *collaborative reviews* ("confirm when the architecture meets your needs") rather than hard gates.

**Notable practices** ([best-practices](https://kiro.dev/docs/specs/best-practices/)):
- **One focused spec per feature** (auth, catalog, cart, payments) — *not* one monolithic spec. (We deliberately keep one `spec.md`; Kiro's per-feature split is worth noting if ours grows unwieldy.)
- Store specs in Git beside code; continuously refine them.
- For bug fixes, pin existing behavior explicitly: `WHEN [condition] THEN the system SHALL CONTINUE TO [existing behavior]` — regression prevention as a requirement.
- **Run-all-tasks** builds a dependency graph and runs independent tasks in concurrent "waves" — a model for how our roadmap slices could parallelize.

---

## 4. EARS — Easy Approach to Requirements Syntax

Created by **Alistair Mavin and colleagues at Rolls-Royce**, analyzing airworthiness regulations; first published at the **RE'09** requirements-engineering conference ([official guide](https://alistairmavin.com/ears/)). EARS constrains natural-language requirements to a few sentence patterns, *"reducing or even eliminating common problems found in natural-language requirements."* Every requirement follows: `[optional condition keyword] the <system> shall <response>`.

The five patterns — **template (1 line) + example** each:

1. **Ubiquitous** (always active, no keyword)
   - `The <system> shall <response>.`
   - *The mobile phone shall have a mass of less than XX grams.*

2. **State-driven** — `WHILE`
   - `While <precondition>, the <system> shall <response>.`
   - *While there is no card in the ATM, the ATM shall display "insert card to begin".*

3. **Event-driven** — `WHEN`
   - `When <trigger>, the <system> shall <response>.`
   - *When "mute" is selected, the laptop shall suppress all audio output.*

4. **Unwanted behaviour** — `IF / THEN`
   - `If <trigger>, then the <system> shall <response>.`
   - *If an invalid credit card number is entered, then the website shall display "please re-enter details".*

5. **Optional feature** — `WHERE`
   - `Where <feature is included>, the <system> shall <response>.`
   - *Where the car has a sunroof, the car shall have a sunroof control panel.*

**Complex/compound requirements** chain keywords, e.g. `While <precondition>, when <trigger>, the <system> shall <response>.` Keep these rare — one trigger + one response per requirement is the testable ideal.

**Why it's testable:** each pattern collapses to a single claim with no ambiguity about *scope*, *trigger*, or *response*. One requirement → one acceptance check. This is the property our spec relies on ("every requirement has a stable ID and is testable").

---

## 5. Traceability practices

The discipline that keeps the spec the source of truth: you can always answer *"why does this code exist?"* by walking **task → requirement → constitution** (our rule).

- **Stable requirement IDs.** Every EARS statement gets a `REQ-ID`; tasks and tests cite it. (We do this.)
- **Forward links.** Requirement → design/plan decision (+ ADR) → task → test. Spec Kit's `/analyze` exists specifically to check this coverage automatically.
- **Backward links.** Each plan/ADR decision cites the requirement it satisfies. (Our plan.md rule.)
- **Principle-to-code traceability** (the mature end, "Constitutional SDD"): constraints map to code at file/line granularity — e.g. a security rule mapped to a CWE entry and the lines enforcing it.
- **Keep specs alive.** Behavior change ⇒ update spec *and* code together (spec-anchored). Specs abandoned post-launch are the #1 failure mode.

**Spec maturity ladder** (useful vocabulary): *spec-first* (scaffold, then discard) → *spec-anchored* (spec + code evolve together — **where we sit**) → *spec-as-source* (only the spec is edited; code is generated, e.g. Tessl's `// GENERATED FROM SPEC - DO NOT EDIT`).

---

## 6. How AI agents consume specs (why EARS is parsable)

When the "developer" is an LLM that can't read your mind and will confidently produce plausible-but-wrong code, the spec's *structure* is what makes it reliable:

- Each EARS pattern is a **single testable claim** — an agent can read it, generate code, and write a test that verifies it, **without guessing** scope/trigger/response.
- Controlled language (EARS, or RFC 2119 `MUST/SHOULD/MAY`) makes conditions, triggers, responses, exceptions, and acceptance criteria **explicit** — exactly the fields an agent needs to act and self-verify.
- The constitution gives the agent guardrails that hold **regardless of which model** runs the task.
- **Caution — don't over-feed.** ETH Zurich research cited by Pluralsight found *adding more context files increases agent steps without improving success rates*. High-signal specs beat verbose ones. Martin Fowler's review echoes this: heavy SDD over-specifies small changes ("sledgehammer to crack a nut") and large up-front specs fight proven small-iteration practice. **Calibrate rigor to scope** — full pipeline for milestones, lightweight notes for trivial fixes.

---

## Annotated links (verified 2026-06-26)

**Primary — Spec Kit**
- [github/spec-kit](https://github.com/github/spec-kit) — repo, README, CLI, `.specify/` layout, slash commands.
- [spec-kit/spec-driven.md](https://github.com/github/spec-kit/blob/main/spec-driven.md) — the methodology manifesto (power inversion, executable specs, constitution as compile-time checks). **Read this first.**
- [Spec Kit Quick Start](https://github.github.com/spec-kit/quickstart.html) — official docs site.

**Primary — Kiro**
- [kiro.dev/docs/specs](https://kiro.dev/docs/specs/) — requirements/design/tasks trio, three-phase workflow, EARS in requirements.
- [Kiro spec best practices](https://kiro.dev/docs/specs/best-practices/) — per-feature specs, living specs, `CONTINUE TO` regression pattern.
- [Kiro Design-First workflow](https://kiro.dev/docs/specs/feature-specs/tech-design-first/) — design→requirements variant, review checkpoints.

**Primary — EARS**
- [alistairmavin.com/ears](https://alistairmavin.com/ears/) — Mavin's official guide: 5 patterns, templates, examples, origin. **Canonical.**
- [EARS paper (Mavin & Wilkinson, RE'09 PDF)](https://ccy05327.github.io/SDD/08-PDF/Easy%20Approach%20to%20Requirements%20Syntax%20(EARS).pdf) — the original Rolls-Royce paper.

**Analysis / best practices**
- [Martin Fowler — Understanding SDD: Kiro, spec-kit, Tessl](https://martinfowler.com/articles/exploring-gen-ai/sdd-3-tools.html) — sharp comparison; over-specification & "semantic diffusion" cautions; maturity ladder.
- [Pluralsight — SDD with AI](https://www.pluralsight.com/resources/blog/software-development/spec-driven-development-with-AI-SDD) — maturity levels, traceability, anti-over-specification checklist, ETH Zurich finding.
- [Microsoft Dev Blog — Diving into SDD with Spec Kit](https://developer.microsoft.com/blog/spec-driven-development-spec-kit) — practical walkthrough.
- [LogRocket — Exploring SDD with GitHub Spec Kit](https://blog.logrocket.com/github-spec-kit/) — hands-on.

---

## How we apply this — checklist for this repo

- [ ] **Spec stays tech-free and EARS-shaped.** Every line in `spec.md` matches one of the 5 patterns; reject prose requirements in review.
- [ ] **One trigger → one response per `REQ-ID`.** Split compound requirements unless a chained `While…when…` is genuinely needed.
- [ ] **Spec-first edits.** Behavior change ⇒ edit `spec.md` before touching code (rule #5). PRs that change behavior without a spec diff get bounced.
- [ ] **Traceability holds both ways.** Each `tasks/` ticket cites a `REQ-ID` + acceptance check; each `plan.md` decision cites the requirement + its ADR.
- [ ] **Use `CONTINUE TO` for regressions.** Bug-fix tasks add an EARS requirement pinning the behavior that must *not* break.
- [ ] **Constitution is the gate.** Before implementing a slice, confirm it doesn't violate `constitution.md`; record hard-to-reverse choices as ADRs.
- [ ] **Calibrate rigor.** Full pipeline for milestones/slices; lightweight note for trivial fixes — don't sledgehammer.
- [ ] **Run the human checkpoints.** Review spec for completeness → audit plan for gaps *and over-engineering* → confirm task acceptance checks before coding.
- [ ] **Feed learnings back.** Incidents update `spec.md`/ADRs, not just a hotfix.

## Tooling we could adopt

- **GitHub Spec Kit CLI** — our pipeline already mirrors it; `specify init` would give us templates + the `/clarify`, `/analyze`, `/checklist` commands we currently do by hand. Lowest-friction adoption, model-agnostic, plain markdown (no lock-in).
- **`/speckit.analyze` equivalent** — even without the CLI, a script that checks every `REQ-ID` is cited by ≥1 task and ≥1 test would automate our traceability rule.
- **Kiro-style dependency waves** — encode task dependencies in `roadmap.md` so independent slices can be built in parallel.
- **RFC 2119 keywords** (`MUST/SHOULD/MAY`) as a complement to EARS where priority/obligation level matters.
- **Watch (don't adopt yet):** spec-as-source tools (Tessl) — promising bidirectional sync, but private beta and carries Model-Driven-Development risks (inflexibility + non-determinism). Revisit later.
