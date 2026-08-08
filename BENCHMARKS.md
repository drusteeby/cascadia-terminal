# Benchmarks

Honest numbers, honestly labeled. Run on Ubuntu Studio 26.04, Wayland/KDE,
under real desktop load (loadavg ≈ 4). Method: the workload runs *inside*
each terminal and `time` (bash) reports wall time — PTY flow control makes
the terminal's own read/parse/render rate the gate, so the same script is
apples-to-apples across terminals. Script: `tools`' bench harness /
`packaging`-independent shell script with three workloads.

| Workload | Cascadia Terminal | Konsole | Alacritty |
|---|---|---|---|
| `seq 1 1000000` (6.9 MB plain flood) | 1.6–2.9 s | 0.28 s | 0.21 s |
| 3000 colored printf lines (escape-heavy) | 0.05–0.14 s | 0.017 s | 0.018 s |
| `cat` 13.3 MB base64 file | 1.6–2.8 s | 0.19 s | 0.10 s |
| Startup → live shell (headless, cold) | ~2.8 s | — | — |

Prior to the drain-priority fix below, the flood rows measured 3.5–7.1 s
and 2.6–4.9 s — a 2.5–4x improvement landed during this benchmarking pass.

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
to the screen model and rendered via Skia in bounded UI batches.

What was found and fixed: UI drain slices were scheduled at
`DispatcherPriority.Background`, which only runs when the dispatcher is
idle — under floods that meant roughly one small slice per frame, capping
throughput at ~1–3 MB/s regardless of budget sizes. Moving the drain to
`Input` priority (fair FIFO with user input, still preempted by render)
delivered the 2.5–4x gain above. Two further experiments did *not* help
and were reverted: raising per-slice budgets 16x (larger finalize batches
cost more than they save; also violates upstream flood-control test
invariants) and looping the background parser without scheduler hops
(regressed throughput ~3x — the hop is load-bearing for fairness).

Remaining work to close the rest of the gap (est. 3–7x vs Konsole):

1. Damage-scoped row invalidation during flood finalize (today closer to
   full-viewport parity refresh per dispatch).
2. Frame-collapsed finalize: during floods, snapshot the screen model at
   most once per output frame instead of once per batch window.
3. Startup: ReadyToRun/NativeAOT publish and deferred subsystem init
   (today ~2.8 s cold to a live shell).
4. A published vtebench run once 1–2 land.

Measurements to reproduce: `tools/CascadiaTerminal.Bench` (headless) or
run the three commands above inside any terminal.
