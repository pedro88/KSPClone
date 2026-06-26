# Orbital Mechanics — Implementation Reference (Patched Conics)

Curated reference for implementing the patched-conics orbital model. Sources are authoritative (René Schwarz memos, Wikipedia formulas, Vallado/Curtis-derived material, KSP wiki, poliastro/Orekit). Every formula below is in *our* terms (see `CONTEXT.md`: *orbit*, *SOI*, *patched conics*, *POI*).

## Why this matters for us

- **ORBIT-2** demands a closed-form `position(game-time)` for every *on-rails vessel* — no step integration. That property is exactly the two-body Kepler solution: `mean anomaly = n·Δt`, solve Kepler's equation, recover `r, v`. This whole doc exists to nail that path down.
- **ORBIT-1 / ADR-0004** fix us on *patched conics*: one body's gravity at a time (the current *SOI*). So we never need n-body integration — only conic propagation plus SOI-boundary patching (**ORBIT-3**).
- **TIME-4** (warp *auto-limit*) and the living-while-empty universe need to find the *next POI* (SOI crossing, maneuver node, atmosphere interface) cheaply — which means evaluating and root-finding on closed-form conics, not simulating forward.

---

## 1. Keplerian orbital elements & state-vector conversion

### Concept (our terms)
An *orbit* is fully described by 6 classical elements relative to its current *SOI* body. We store the orbit as elements (compact, stable to serialize per **PERSIST**) and derive Cartesian state `(r, v)` on demand. The physics engine works in Cartesian; on-rails propagation works in elements. We need both directions of conversion.

### The six classical elements
| Symbol | Name | Range | Notes |
|---|---|---|---|
| `a` | semi-major axis | `>0` ellipse, `<0` hyperbola, `1/a=0` parabola | orbit size/energy |
| `e` | eccentricity | `0` circle, `0<e<1` ellipse, `=1` parabola, `>1` hyperbola | shape |
| `i` | inclination | `0..π` | tilt vs reference plane |
| `Ω` | longitude of ascending node (LAN/RAAN) | `0..2π` | **undefined when i=0 or π (equatorial)** |
| `ω` | argument of periapsis | `0..2π` | **undefined when e=0 (circular)** |
| `ν` / `M` / `E` | true / mean / eccentric anomaly | `0..2π` | position-along-orbit; `M` is what advances linearly in time |

### Elements → state vector (René Schwarz M001 — authoritative, copy this)
1. Advance mean anomaly: `M(t) = M0 + Δt·√(μ/a³)`, normalize to `[0,2π)`.
2. Solve Kepler `M = E − e·sin E` for `E` (see §2).
3. True anomaly: `ν = 2·atan2( √(1+e)·sin(E/2), √(1−e)·cos(E/2) )`.
4. Distance: `r_c = a(1 − e·cos E)`.
5. Position/velocity in **orbital (perifocal) frame** (x→periapsis, z→orbit normal):
   - `o  = r_c · (cos ν, sin ν, 0)`
   - `ȯ = (√(μa)/r_c) · (−sin E, √(1−e²)·cos E, 0)`
6. Rotate to the bodycentric inertial frame: `r = Rz(−Ω)·Rx(−i)·Rz(−ω)·o` (same rotation for `ȯ→ṙ`). Expanded closed form is in M001 eqs. (9)/(10).

### State vector → elements (René Schwarz M002 — authoritative, copy this)
1. Angular momentum: `h = r × ṙ`.
2. Eccentricity vector: `e = (ṙ × h)/μ − r/‖r‖`; then `e = ‖e‖`.
3. Node vector: `n = (0,0,1) × h = (−h_y, h_x, 0)`.
4. True anomaly: `ν = arccos(⟨e,r⟩ / (‖e‖‖r‖))`, but `ν = 2π − ν` if `⟨r,ṙ⟩ < 0` (descending).
5. Inclination: `i = arccos(h_z/‖h‖)`.
6. `E = 2·arctan( tan(ν/2) / √((1+e)/(1−e)) )`.
7. `Ω = arccos(n_x/‖n‖)`, with `Ω = 2π − Ω` if `n_y < 0`.
8. `ω = arccos(⟨n,e⟩/(‖n‖‖e‖))`, with `ω = 2π − ω` if `e_z < 0`.
9. `M = E − e·sin E`.
10. Semi-major axis from energy: `a = 1 / (2/‖r‖ − ‖ṙ‖²/μ)`.

### Gotchas
- **Quadrant resolution**: every `arccos` above needs the sign test (`atan2`-style) shown — getting these wrong silently flips orbits. M002 spells out each test; follow it exactly.
- **Degenerate orbits**: circular (`e≈0`) leaves `ω` undefined; equatorial (`i≈0`) leaves `Ω` undefined. Decide a convention (e.g. set the undefined angle to 0 and fold its meaning into the next). For our game most orbits are non-degenerate, but circular-equatorial parking orbits hit *both* — handle or you'll get NaNs.
- M001/M002 default `μ` to the Sun and use AU/day; for us **always pass the current SOI body's `μ`** and work in metres/seconds.

---

## 2. Solving Kepler's equation (M → E), elliptic AND hyperbolic

### Concept (our terms)
Time enters the *orbit* only through `M` (mean anomaly), which grows linearly: `M = n·Δt`, `n = √(μ/a³)` (mean motion). To get a real position we must invert Kepler's transcendental equation for the eccentric anomaly. This is the single numerical kernel of our closed-form `position(t)`; everything else is algebra. Must be robust for elliptic, hyperbolic, and near-parabolic *orbits* (escape/capture trajectories are hyperbolic).

### Equations
- **Elliptic** (`0≤e<1`): `M = E − e·sin E`. Newton: `E ← E − (E − e·sin E − M)/(1 − e·cos E)`.
- **Hyperbolic** (`e>1`): `M = e·sinh H − H`. Newton: `H ← H − (e·sinh H − H − M)/(e·cosh H − 1)`. Here `M = √(μ/(−a)³)·Δt` and recover `ν` via `tanh(H/2) = √((e−1)/(e+1))·tan(ν/2)`.
- **Parabolic** (`e=1`): Barker's equation has a closed-form (no iteration); `M_p = D + D³/3` with `D = tan(ν/2)`, solvable by Cardano.

### Initial guess (the part that makes or breaks stability)
- Elliptic: `E0 = M` works for low `e`; use `E0 = π` (or `M + e·sign(sin M)`) for `e > 0.8`. A good seed is the difference between 4 iterations and 30.
- Hyperbolic: `H0 = asinh(M/e)` or `H0 = ln(2|M|/e + 1.8)·sign(M)`.

### Gotchas
- **Newton-Raphson stability**: it can overshoot/oscillate for high `e` with a poor seed (the "singular corner" `M≪1, e→1`). Mitigations: bound each step, fall back to bisection if a step leaves the bracket, cap iterations (~50) with a residual tolerance (~1e-10 rad). Convergence is quadratic once near the root — most cases converge in <8 iterations.
- **Near-parabolic (`e≈1`)**: both elliptic and hyperbolic forms get ill-conditioned (denominators → 0). This is the strongest argument for the **universal-variable formulation (§3)** which has no special case at `e=1`.
- Always **normalize `M` to `[0,2π)`** (elliptic) before solving; don't feed it unbounded accumulated time or you lose float precision over long warps.

---

## 3. Position/velocity at arbitrary time — the closed-form `position(t)` (ORBIT-2)

### Concept (our terms)
This is the function **ORBIT-2** is about: given an *orbit* (elements + epoch) and any future *game-time*, return `(r, v)` directly. Two viable implementations:

**(A) Classical (per orbit type)** — M001's pipeline: `M(t) = M0 + √(μ/a³)·Δt` → solve Kepler (§2) → `ν, r_c` → perifocal `o, ȯ` → rotate. Branch on elliptic vs hyperbolic. Simple, fast, what KSP effectively does.

**(B) Universal variables (one path for all conics)** — recommended for robustness. Uses the universal anomaly `χ` and Stumpff functions `C(z), S(z)` (`z = αχ²`, `α = 1/a`); a single Newton solve handles ellipse/parabola/hyperbola with no branching, and **no blow-up at `e=1`**.

### Universal-variable formulas (Curtis/Vallado form)
- Universal Kepler equation, solved for `χ`:
  `√μ·Δt = (r0·v_r0/√μ)·χ²·C(z) + (1 − α·r0)·χ³·S(z) + r0·χ`,  `z = αχ²`.
- Newton derivative:
  `f'(χ) = (r0·v_r0/√μ)·χ·(1 − αχ²·S) + (1 − α·r0)·χ²·C + r0`.
- Initial guess (Curtis): `χ0 = √μ·|α|·Δt`.
- **Stumpff functions** (auto-select by sign of `z`):
  - `z>0` (ellipse): `C = (1−cos√z)/z`, `S = (√z − sin√z)/(√z)³`
  - `z<0` (hyperbola): `C = (cosh√−z − 1)/(−z)`, `S = (sinh√−z − √−z)/(√−z)³`
  - `z=0` (parabola): `C = 1/2`, `S = 1/6`
- **Lagrange coefficients** recover the state directly:
  - `f = 1 − (χ²/r0)·C`,  `g = Δt − (χ³/√μ)·S`
  - `r = f·r0 + g·v0`
  - `ḟ = (√μ/(r·r0))·(αχ³·S − χ)`,  `ġ = 1 − (χ²/r)·C`
  - `v = ḟ·r0 + ġ·v0`

### Gotchas
- For elliptic orbits add `k·2π·a/√μ·... ` — simpler: reduce `Δt` modulo the period `T = 2π√(a³/μ)` before solving, so `χ` stays small and the Newton solve stays well-conditioned over multi-year warps.
- Universal vars need a starting state `(r0, v0)` at epoch, not raw elements — convert once (M001) then propagate with Lagrange coefficients. This is also the cheapest way to step an on-rails vessel repeatedly.
- `v_r0 = ⟨r0,v0⟩/‖r0‖` (radial speed) is a required input — don't forget it.

### Useful scalars
- Vis-viva (speed at radius `r`): `v² = μ(2/r − 1/a)`.
- Period (elliptic only): `T = 2π√(a³/μ)`; mean motion `n = √(μ/a³)`.
- Escape speed at `r`: `v_esc = √(2μ/r)` (= `√2 ×` circular speed).

---

## 4. Sphere of Influence (SOI) & patch detection (ORBIT-1, ORBIT-3)

### Concept (our terms)
An *SOI* is the region where one body dominates gravity; in *patched conics* it's the hard boundary where we re-parent a vessel's *orbit* from one body to another (**ORBIT-3**), recording the crossing as a *POI* (**TIME-4** auto-limit must stop warp here). Each body has a fixed SOI radius computed once from its orbit around its parent.

### Formula (Laplace SOI — what KSP uses)
`r_SOI = a · (m / M)^(2/5)`
- `a` = semi-major axis of the *body's* orbit around its parent
- `m` = mass of the body (the smaller), `M` = mass of the parent (the larger)
- Equivalently with `μ`: `r_SOI = a·(μ_body/μ_parent)^(2/5)`.

KSP treats SOI as a true sphere of this radius (real SOIs are a slightly oblate spheroid — the angle-dependent form is `r_SOI(θ) = a(m/M)^(2/5) / (1+3cos²θ)^(1/10)`, averaging to `0.9431·a(m/M)^(2/5)`; **we should match KSP and use the simple spherical form**).

### Patch detection (how ORBIT-3 fires)
A vessel orbiting body P leaves P's SOI when its distance from P first exceeds `r_SOI(P)`; it enters a child body C's SOI when its distance from C first drops below `r_SOI(C)`. Because the *orbit* is closed-form, detect the crossing as a **root of `‖r_vessel(t) − r_body(t)‖ − r_SOI = 0`** over the warp interval (bracket + Brent/bisection), not by stepping. At the patch:
1. Compute vessel state `(r, v)` in the old frame at crossing time `t*`.
2. Subtract/add the boundary body's state at `t*` to re-express `(r, v)` relative to the new SOI body (continuity of position & velocity: `v_new = v_old ± v_body`).
3. Convert the new relative `(r, v)` → elements (M002) = the new *orbit*. Record `t*` as a *POI*.

### Gotchas
- The two-body assumption is exactly valid only at the boundary; patched conics is C0 (position/velocity continuous) but not energy-conservative across patches — small discontinuities are expected and acceptable (this is the KSP behaviour we're cloning, per ADR-0004).
- Nested SOIs: a vessel can be inside several candidate child SOIs' radii in principle; pick the innermost/closest dominating body. KSP's tree is strictly hierarchical (Sun ⊃ planet ⊃ moon), so re-parent only one level at a time.
- For **TIME-4** you need the *earliest* future crossing across all vessels — compute each vessel's next-patch time analytically and take the min; this is the POI scan.

---

## 5. How KSP specifically does patched conics & time

### Concept (our terms)
KSP1 is our reference behaviour (ADR-0004). It is pure patched conics: every vessel is in exactly one *SOI*, orbits are analytic conics, and *on-rails warp* evaluates `position(t)` instantly rather than integrating — precisely our **ORBIT-2 / on-rails warp** model. On-rails vessels in KSP store the conic and are propagated by the same Kepler solve described above.

### Specifics worth copying
- SOI radius: the Laplace `a(m/M)^(2/5)` form (§4); exposed in-game/kOS as `soiRadius`.
- "On rails": when not in the physics bubble, KSP advances the orbit by mean anomaly and re-parents at SOI edges — identical to our *on-rails vessel* + *promotion/demotion* design.
- Maneuver nodes are planned as instantaneous Δv (§6) and the *next* node / SOI change / atmosphere interface are exactly KSP's warp-stop points — our *POI* set (**TIME-4**).
- Cheat sheet / dV maps give the design-level Δv budgets (vis-viva, Hohmann, escape) we'll reuse for mission planning and tests.

### Gotchas
- The official KSP wiki sits behind an anti-bot wall (Anubis) and could not be machine-fetched for this doc — open the pages in a browser. URLs are listed below and are correct.
- KSP uses its own (scaled, 1/10-ish) solar system and a custom `G`; if we mirror KSP numbers, take their `μ`/SOI values directly rather than recomputing from real masses.
- KSP's reference frame conventions (left-handed Unity, y-up) differ from textbook (right-handed, z-up). Pick one internal convention (textbook math, convert at the Unity boundary) and document it — this is a classic source of mirrored orbits.

---

## 6. Maneuver nodes — applying a burn as instantaneous Δv

### Concept (our terms)
A maneuver node is a planned *POI*: at node time `t_n` we apply an instantaneous Δv to the vessel's velocity, producing a new *orbit* from that instant. Because it's impulsive, the math is "evaluate state at `t_n`, add a vector, re-derive elements."

### Procedure
1. Propagate the current *orbit* to node time: `(r_n, v_n) = position(t_n)` (§3).
2. Build the node's local burn frame (prograde / radial-out / normal) at `(r_n, v_n)`:
   - `p̂ = v_n/‖v_n‖` (prograde)
   - `n̂ = (r_n × v_n)/‖r_n × v_n‖` (orbit normal)
   - `r̂ = n̂ × p̂` (radial, completing right-handed triad)
3. Apply the impulse: `v_n⁺ = v_n + Δv_pro·p̂ + Δv_rad·r̂ + Δv_nrm·n̂`. Position is unchanged (`r_n⁺ = r_n`).
4. Convert `(r_n, v_n⁺)` → new elements (M002). That's the post-burn *orbit*, valid from `t_n`. The old orbit is valid up to `t_n`; store both as a patch.

### Design-level Δv formulas (for budgets/tests, not the burn application)
- Vis-viva gives speed before/after at any `r`: `v = √(μ(2/r − 1/a))`.
- Hohmann transfer between circular radii `r1→r2`:
  - `Δv1 = √(μ/r1)·(√(2r2/(r1+r2)) − 1)`
  - `Δv2 = √(μ/r2)·(1 − √(2r1/(r1+r2)))`
  - transfer time `t = π√((r1+r2)³/(8μ))`
- Pure plane change of angle `Δi` at speed `v`: `Δv = 2v·sin(Δi/2)`.

### Gotchas
- The burn frame must be rebuilt at `t_n` (it rotates along the orbit) — don't reuse the frame from "now."
- For a real (finite) burn KSP/users center the impulse on the node and approximate; our server applies it as a single Δv at one instant (consistent with on-rails). If a node is executed under physics, the physics integration supersedes this analytic patch.
- Edge case: a Δv that exactly cancels velocity, or pushes `e→1`, lands in the near-parabolic singularity — another reason to standardize on universal variables (§3).

---

## 7. Open-source references to study

- **poliastro** (Python, MIT) — readable reference for elements↔state, Kepler/universal-variable propagation, Lambert, and a dedicated patched-conics / SOI module. Best "read the code to learn the algorithm" source for us.
- **Orekit** (Java, mature, agency-backed) — production-grade; good for cross-checking correctness and edge-case conventions (frames, time scales). Heavier than we need but the canonical validation oracle.
- **Principia** (KSP mod, C++) — *contrast only*: it replaces KSP's patched conics with real n-body integration. ADR-0004 explicitly rejects this path; study it to understand precisely what we are *not* doing and why (no closed form, drift over warps).

---

## Recommended implementation path

1. **Build the math core first, frame-agnostic and unit-tested**: M001 (elements→state) and M002 (state→elements), with the quadrant tests verified against poliastro on random orbits (round-trip `elements→state→elements` must be identity).
2. **Implement the Kepler kernel as universal variables (§3)**, not per-type branches — one code path for elliptic/hyperbolic/near-parabolic kills the worst stability bugs and gives us `position(t)` for **ORBIT-2** directly via Lagrange coefficients.
3. **Wrap propagation as `Orbit.stateAt(gameTime)`** that reduces `Δt` modulo period first; this is the single function on-rails warp, persistence, and POI scanning all call.
4. **Add SOI as fixed per-body radii (§4)** and implement patch detection as a root-find of `‖r_rel(t)‖ − r_SOI` over the warp window; on crossing, re-frame `(r,v)` and re-derive elements (**ORBIT-3**), emitting a *POI*.
5. **Maneuver nodes (§6)** = evaluate state at node time, add Δv in the prograde/radial/normal triad, re-derive elements; expose Hohmann/plane-change/vis-viva helpers for planning and tests.
6. **POI scan for TIME-4**: per vessel compute next SOI crossing / node / atmosphere-interface time analytically; the global min is the warp auto-limit. Validate end-to-end against KSP behaviour on a few known transfers (Kerbin→Mun).

## Open questions for our case

- **Frame & handedness convention**: textbook right-handed z-up vs Unity left-handed y-up — fix one internal representation and convert only at the Unity/render boundary. Decide and document before any code.
- **Match KSP numbers or real physics?** Do we clone KSP's scaled system (`μ`, SOI values, custom `G`) for familiar Δv budgets, or use real constants? Affects every formula's inputs.
- **Patch discontinuity tolerance**: patched conics is not energy-continuous across SOIs. What jump magnitude is acceptable before we flag it (and do clients need to be told a re-frame happened for smooth rendering, cf. NET-3 reconciliation)?
- **Degenerate-orbit policy**: canonical handling for circular and/or equatorial orbits where `ω`/`Ω` are undefined — pick conventions so serialization (PERSIST) is deterministic and round-trips.
- **Anomaly storage**: store `M0` at a fixed epoch and advance, or store full `(r,v)` and propagate with Lagrange coefficients? The latter avoids re-solving elements but needs care over very long empty-server warps (SUSP-1). Probably store elements + epoch for persistence, cache state for stepping.
- **POI root-finding robustness**: guaranteeing we never *miss* a fast SOI grazing crossing during a huge warp jump — bracketing strategy and step granularity for the root-find need specifying.

---

## Annotated link list (verified)

Conversions & core formulas (René Schwarz Memorandum Series — primary, copy the equations):
- https://downloads.rene-schwarz.com/download/M001-Keplerian_Orbit_Elements_to_Cartesian_State_Vectors.pdf — M001: full elements→state algorithm (Kepler solve, perifocal, rotation matrices). **Use directly for §1/§3.**
- https://downloads.rene-schwarz.com/download/M002-Cartesian_State_Vectors_to_Keplerian_Orbit_Elements.pdf — M002: full state→elements with all quadrant tests. **Use directly for §1, §4 re-parenting, §6 post-burn.**

Kepler's equation & anomalies:
- https://en.wikipedia.org/wiki/Kepler%27s_equation — elliptic/hyperbolic/parabolic forms, Newton iteration, seed guidance, e=1 instability note.
- https://orbital-mechanics.space/time-since-periapsis-and-keplers-equation/universal-variables.html — universal-variable Kepler eq, Stumpff `C/S`, Lagrange `f,g`, Newton setup. **Primary for §3.**
- https://orbital-mechanics.space/time-since-periapsis-and-keplers-equation/universal-variables-example.html — worked numeric example for the above.
- https://en.wikipedia.org/wiki/Universal_variable_formulation — concise statement of the universal formulation across conic types.

Elements & state-vector theory / edge cases:
- https://orbital-mechanics.space/classical-orbital-elements/classical-orbital-elements.html — the six elements, ranges, meaning.
- https://orbital-mechanics.space/classical-orbital-elements/orbital-elements-and-the-state-vector.html — conversion both ways + degenerate (circular/equatorial) cases. **Read for §1 gotchas.**

SOI & patched conics:
- https://en.wikipedia.org/wiki/Patched_conic_approximation — method, SOI as boundary, continuity-of-state patching, limitations.
- https://en.wikipedia.org/wiki/Sphere_of_influence_(astrodynamics) — `r=a(m/M)^(2/5)`, oblate-spheroid angle form, averaged radius.
- https://orbital-mechanics.space/interplanetary-maneuvers/sphere-of-influence.html — clean derivation of the SOI radius and its patched-conic role. **Primary for §4.**
- https://archive.aoe.vt.edu/lutze/AOE4134/patchedconiceqs.pdf — Virginia Tech AOE4134 "Summary of Patched Conic Approximations" (compact equation sheet).

Maneuvers & Δv:
- https://en.wikipedia.org/wiki/Orbital_maneuver — impulsive-maneuver model, prograde/radial/normal directions.
- https://en.wikipedia.org/wiki/Hohmann_transfer_orbit — Δv1/Δv2/transfer-time formulas (§6).
- https://en.wikipedia.org/wiki/Vis-viva_equation — `v²=μ(2/r−1/a)`, period, mean motion, escape speed.

KSP-specific (open in a browser — host is behind anti-bot Anubis, not machine-fetchable):
- https://wiki.kerbalspaceprogram.com/wiki/Orbit — KSP orbit / on-rails model.
- https://wiki.kerbalspaceprogram.com/wiki/Sphere_of_influence — KSP's SOI definition & radius.
- https://wiki.kerbalspaceprogram.com/wiki/Cheat_sheet — Δv equations, vis-viva, transfer maths, dV budget guidance.
- https://ksp-kos.readthedocs.io/en/latest/structures/vessels/node.html — kOS maneuver node (prograde/radial/normal Δv components) — concrete model of node math.

Open-source libraries to study:
- https://github.com/poliastro/poliastro — Python astrodynamics (elements↔state, Kepler/universal vars, patched conics). **Best learn-by-reading source.**
- https://github.com/poliastro/poliastro/wiki/Patched-conics-computations — poliastro's patched-conics design notes.
- https://www.orekit.org/ — mature Java library; correctness oracle for cross-validation.
- https://github.com/mockingbirdnest/Principia — n-body KSP mod; **contrast only** (the approach ADR-0004 rejects).

Reference texts (not free online but cited throughout the above):
- Vallado, *Fundamentals of Astrodynamics and Applications* — the standard reference (universal variables, Kepler solvers, frames).
- Curtis, *Orbital Mechanics for Engineering Students* — source of the §3 universal-variable + Lagrange-coefficient form.
- Braeunig, http://www.braeunig.us/space/orbmech.htm — popular free formula reference (note: the site currently serves a TLS certificate mismatch; reachable but browsers/curl may warn).
