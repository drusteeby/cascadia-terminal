# Benchmarks

Honest numbers, honestly labeled. Run on Ubuntu Studio 26.04, Wayland/KDE,
under real desktop load (loadavg ≈ 4). Method: the workload runs *inside*
each terminal and `time` (bash) reports wall time — PTY flow control makes
the terminal's own read/parse/render rate the gate, so the same script is
apples-to-apples across terminals. Script: `tools`' bench harness /
`packaging`-independent shell script with three workloads.

Numbers below were captured under heavy background load (loadavg ~7), so
the absolute values are inflated versus a quiet machine; the before/after
columns were measured back-to-back under the same load, so the *ratio* is
what to read.

| Workload | Before | After (frame-collapse) | Konsole |
|---|---|---|---|
| `seq 1 1000000` (6.9 MB plain flood) | ~5.0 s | **~2.6 s** | 0.28 s |
| `cat` 13.3 MB base64 file | ~7.0 s | **~4.4 s** | 0.19 s |
| 3000 colored printf lines (escape-heavy) | ~0.2 s | **~0.1 s** | 0.017 s |
| Startup → live shell (headless, cold) | ~2.8 s | ~2.8 s | — |

Two optimization passes have landed:
1. **Drain-priority fix** — the managed output drain ran at Background
   priority (one small slice per frame); Input priority gave 2.5–4x.
2. **Frame-collapse + parity move** (this pass) — a further ~1.6–2x, below.

## Reading these numbers

- **Interactive workloads are fine.** The escape-heavy workload — closest
  to what TUIs (vim, htop, fzf) actually emit — completes in well under
  half a second. Keyboard input, pane operations, and rendering stay
  responsive during floods because output draining is budgeted per UI
  slice by design.
- **Raw dump throughput is the real gap**: ~1–3 MB/s sustained vs tens of
  MB/s for Konsole/Alacritty on `cat`-a-huge-file workloads. The terminal
  never freezes or drops data (PTY flow control throttles the producer),
  but a `cat` of a 100 MB log will visibly take longer than in a
  GPU-native terminal.
- Numbers vary ±2× with desktop load; ranges above span observed runs.

## Why, and the roadmap

Output is parsed off the UI thread (Ghostty `libghostty-vt`), then applied
to the screen model and rendered via Skia composition.

### Pass 1 — drain priority

UI drain slices were scheduled at `DispatcherPriority.Background`, which
only runs when the dispatcher is idle — under floods that meant roughly one
small slice per frame, capping throughput regardless of budget sizes.
`Input` priority delivered 2.5–4x. (Two experiments were reverted: raising
per-slice budgets 16x, and a hop-free parser loop — both measured slower.)

### Pass 2 — frame-collapse + parity move

Profiling the flood pipeline (an env-gated `FloodProfiler`) showed the real
cost centers were **not** the VT parser but the UI-thread work run *per
output batch*:

- `FinalizeOutputBatchOnUiThread` (scroll sync, cursor, invalidate) ran
  ~9,400 times per flood, ~14 s total — co-equal with parsing.
- Each finalize's `Invalidate` triggered a composition render that took the
  screen lock, so render and parse burned ~8 s fighting over that lock.

Fixes:
- **Frame-collapse finalize** — coalesce finalization to at most once per
  ~16 ms frame, driven by the drain dispatches themselves (a render/idle
  timer is starved by the high-priority drain during a flood), with a
  guaranteed flush when the backlog empties so the final frame always lands.
  Finalize calls dropped ~9,400 → ~2,300 and render lock-wait ~5.2 s → ~1 s.
- **Move renderer parity out of the parse lock** — highlight spans, hovered
  link, selection, and opacity are render state; they were recomputed on
  every parse batch under the screen lock. They now refresh once per
  throttled finalize, shortening the parse lock hold.

Net: another ~1.6–2x. Row-damage rendering was already in place (the draw
handler keeps a cached surface and redraws only dirty rows via
`ClearDirtyViewportRows`); it can't help plain scroll-floods where every
row changes each frame, but it already covers partial updates (typing,
TUI).

### Remaining

1. Shorten the render's screen-lock hold (snapshot cells under lock, draw
   outside) to further cut parse/render contention.
2. Startup: ReadyToRun/NativeAOT publish and deferred subsystem init.
3. A published vtebench run on a quiet machine.

Measurements to reproduce: `tools/CascadiaTerminal.Bench` (headless) or
run the three commands above inside any terminal.
