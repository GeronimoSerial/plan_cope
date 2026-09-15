# Low-end reference profile and performance budgets

**Status: budgets, not measurements.** No reference hardware was available when this
document was written, and none of the procedures below have been run. They are specified
precisely enough for someone with the actual hardware (or an explicit emulation of it) to
execute them without guessing. Results must be recorded back into the B8 batch report and
used to confirm or revise the numbers in §3.

## 1. Scope

PlanCope is a desktop exam-delivery application. Its relevant shape for performance:

- A **WinForms host** that embeds a **WebView2** operator console and runs the
  **ASP.NET Core API in-process** (Kestrel) with **SQLite**.
- A **React SPA** (`ClientApp`) served two ways:
  - the operator console is mapped into the embedded WebView2 via the virtual host
    `https://host.plancope.local/` from `ClientApp/dist`;
  - the student exam is the same SPA, served by the local API at
    `http://127.0.0.1:<port>/examen/<sessionId>` and opened in the default browser.

This document defines the low-end reference profile and the budgets the project commits to
on it:

1. cold start to a usable host console (§3.1);
2. idle memory footprint with no active exam session (§3.2);
3. time-to-interactive and frame budget for a 100-question exam session (§3.3) — the
   contract the virtualization work (B8 task 3) is held to.

It also specifies, step by step, how each budget is verified (§4). **I do not have this
hardware and have not run these verifications.** The budget numbers are targets derived from
the software's structure and published platform characteristics; they must be measured.

## 2. Reference hardware profile

This is the project's stated target. Do not weaken it for convenience; a development machine
is not a proxy.

| Component | Reference specification |
|---|---|
| CPU | 2 physical cores, ~1.6–2.4 GHz (representative: Intel Celeron N4020/N4500, AMD A6/Athlon, or an entry dual-core i3). No reliance on high boost or many cores. |
| RAM | 4 GB total (~3.4–3.9 GB usable after Windows). |
| Storage | 5400 rpm SATA HDD, ~100–150 MB/s sequential read, ~10 ms random seek. **Not SSD.** |
| GPU | Integrated (composition is CPU-assisted; there is no discrete GPU). |
| Display | 1366×768 typical. |
| OS | Windows 10/11 64-bit; WebView2 Evergreen runtime installed (standard deployment path). |
| Network | Offline operation; the only traffic is localhost HTTP to the local API. |

Two consequences drive everything below:

- **Random I/O is ~10 ms and paging is catastrophic.** Memory pressure must be bounded so
  the machine never swaps to the HDD during normal use.
- **The CPU has 2 cores.** Long main-thread work is directly visible as jank.

## 3. Performance budgets

### 3.1 Cold start to usable host console

| | Budget |
|---|---|
| Target | **≤ 15 s** |
| Ceiling | **≤ 25 s** |
| First-ever run (excluded) | ≤ 60 s |

**Definition.** From process launch (the operator double-clicks the host exe) until the
operator console is interactive: the `Iniciando Plan Cope Local...` loading label has been
replaced by the rendered AppShell and the console accepts input (CUE entry / activation
screen). In code this is the path `Program.Main` → `LegacyDatabaseMigrator.MigrateIfNeeded()`
→ `MainForm_Shown` → `StartLocalApiAsync()` (`LocalApiApplication.Build` + Kestrel
`StartAsync`, which initializes SQLite and seeds local data) → `StartWebViewAsync()`
(`EnsureCoreWebView2Async`, virtual-host mapping, navigation to `index.html`) →
`NavigationCompleted` + `PostHostContext` → console interactive.

The "first-ever run" budget covers one-time WebView2 setup (creation of its user-data folder
on the HDD and any runtime initialization). It is measured separately and excluded from the
daily-use budget.

**Rationale.** Each cold-start stage costs a few seconds on this profile: .NET/CLR and
assembly load from the HDD, SQLite migration/seed on the HDD, Kestrel startup, WebView2
initialization (its user-data folder lives on the HDD), then React bundle load/parse and
first paint. 15 s is an achievable aggregate target with ReadyToRun (B8 task 5) and a
reasonable bundle size; 25 s is the ceiling that still keeps a once-a-day operator boot
tolerable. Sub-5 s is not a goal on this hardware.

### 3.2 Idle memory footprint (no active session)

| | Budget (committed private bytes, whole process tree) |
|---|---|
| Target | **≤ 1.0 GB** |
| Ceiling | **≤ 1.5 GB** |

**Definition.** After activation, on the host console / school gate, with no exam session
running, no student view open, and no sync in progress; measured after the machine and the
app have been idle for 2 minutes (caches and WebView2 subprocesses settled). "Whole process
tree" means the host process — which runs the ASP.NET Core API in-process and SQLite —
**plus every WebView2 process the app owns** (`msedgewebview2.exe` browser, renderer, GPU,
utility). WebView2 is multi-process; counting only the host process understates the
footprint by hundreds of MB.

**Rationale.** A 4 GB machine running Windows 10 sits around 1.5–2.5 GB committed at idle,
leaving ~1.5–2.5 GB for the app and anything else the school runs. The app must stay under
1.5 GB so the OS and the operator remain responsive; on a 5400 rpm HDD, paging is the worst
possible experience and must be avoided — which is why the ceiling is a **committed-bytes**
budget, not a working-set one. 1.0 GB is the design target: an in-process ASP.NET Core +
SQLite host plus a React SPA in WebView2 should fit comfortably under it. (The same
measurement during an active session is recorded in the batch report for context; only the
idle number is budgeted.)

### 3.3 100-question exam session: time-to-interactive and frame budget

Applies to the student exam rendered by the SPA at `/examen/<sessionId>` in the default
browser. **This is the contract the virtualization work (B8 task 3) is held to.**

| Metric | Budget |
|---|---|
| First paint (session data ready → first question visible) | **≤ 300 ms** |
| Scroll through all 100 questions | sustained 60 fps (frame budget **≤ 16.7 ms**) |
| Dropped frames (frames > 50 ms) over a full 10 s scroll pass | **0** |
| QuestionNav jump (click question N → scroll lands) | starts within **≤ 150 ms**, no dropped frame during the scroll |

Contract in plain words: **on the reference profile, a 100-question exam must show its first
question within 300 ms of the session data being available, and a full scroll of the question
list must not drop a single frame (> 50 ms).** When real hardware is unavailable, the
verifier applies a 6× CPU throttle in DevTools (§4.2, §4.6).

**Rationale.**

- Without virtualization, 100 eagerly-rendered `ExamBlock`s plus a 100-item `QuestionNav`
  mean tens of thousands of DOM nodes; layout + paint of that tree on a 2-core CPU routinely
  exceeds the 16.7 ms frame budget while scrolling. This is the jank B8 task 3 exists to
  remove.
- With virtualization (viewport + overscan, ~10–25 blocks in the DOM at once), layout and
  paint become O(viewport) instead of O(100), so a 16.7 ms frame budget is realistic even
  with the CPU-assisted composition of this profile.
- First paint ≤ 300 ms is dominated by JSON parse + React commit + first layout of the small
  visible set. 300 ms is comfortable headroom on 2 cores and small enough to feel instant to
  a student.
- The 50 ms dropped-frame threshold is the standard definition DevTools uses; the 10 s
  window covers a scripted full-length scroll at human speed.

## 4. How to verify each budget

**Status warning — restate in every report:** these procedures have not been executed and no
reference hardware was available. Do not present any number in this document as a
measurement.

### 4.1 Machine preparation (real hardware)

1. Freshly boot the reference machine and wait **3 minutes** for Windows to settle. On HDD
   machines, first-boot background work (Defender scan, search indexer, telemetry) strongly
   skews cold-start numbers.
2. Ensure the WebView2 Evergreen runtime is installed and the app has been launched at least
   once before, so "first-ever run" costs are not in the sample.
3. Close all other applications.
4. Run each measurement **3 times**; report median, min, max. HDD variance is high — a
   single run proves nothing.
5. Record whether each run is "cold boot" (machine rebooted since the last app run) or
   "warm" (app exited and relaunched). The budgets in §3 apply to warm runs on an
   already-settled machine.

### 4.2 Emulating the profile when real hardware is unavailable

Emulation is indicative, not authoritative — say so in the report:

- **CPU.** DevTools (Edge or Chrome — the same Chromium engine as WebView2): Performance
  panel → CPU throttling **6× slowdown** as the baseline for this 2-core profile; optionally
  re-run at 20× for the worst case. The Performance-insights device presets (e.g.
  "Low-end mobile") additionally apply memory throttling — a useful heuristic, still a
  heuristic.
- **Network.** Not applicable to the embedded bundle (served from disk via virtual-host
  mapping) and **does not apply to localhost** for the student SPA/API traffic. There is no
  WAN path to throttle. State this in the report rather than silently skipping it.
- **Storage (HDD).** Cannot be emulated in DevTools. Cold-start and bundle-load figures are
  the ones most affected and must be confirmed on real hardware (§4.3–4.4). Do not claim
  HDD-equivalent results from an SSD development box.

### 4.3 Cold start — wall-clock timing

**Authoritative method** (no guessing about when the console is "usable"): add a temporary
stopwatch trace to the host (remove it before release):

- **T0** at the top of `Program.Main`.
- **T1** when `LegacyDatabaseMigrator.MigrateIfNeeded()` returns.
- **T2** when `StartLocalApiAsync()` returns (API up, SQLite initialized/seeded).
- **T3** in `OnNavigationCompleted` when `e.IsSuccess` (page loaded).
- **T4** in `PostHostContext` (console interactive — the AppShell has received host context).

Write each mark to a temp file (`Debug.WriteLine`/trace may not be visible in a shipped
WinForms app). **Cold start = T4 − T0**; the segment deltas attribute the time to migration,
API/SQLite init, and WebView2 init + navigation.

**Cross-check without code changes** (external stopwatch): launch the exe and have a human
observer time from click until the console is usable (loading label gone, cursor in the CUE
field). Report both; the in-process trace is authoritative because "usable" is unambiguous
there.

PowerShell launcher (warm run):

```powershell
$sw = [System.Diagnostics.Stopwatch]::StartNew()
$p = Start-Process -FilePath "<path>\PlanCope.Local.Host.exe" -PassThru
# … human observer notes the moment the console is usable …
$sw.Stop()
"cold_start=$($sw.Elapsed.TotalSeconds)s pid=$($p.Id)"
```

### 4.4 Cold start — attribution (WPR / dotnet-trace)

Use these to explain *where* the wall-clock time goes, not to replace the stopwatch.

**Windows Performance Recorder** (no code change):

```powershell
wpr -start CPU -filemode
# launch the app, wait until the console is usable
wpr -stop plancope-coldstart.etl
# open the ETL in Windows Performance Analyzer (WPA):
#   Generic Events / CPU sampling -> startup cost by process and module
#   File I/O                       -> HDD wait during SQLite init and WebView2 setup
```

**dotnet-trace** for the managed side (top methods):

```powershell
# launch the app, then attach to its PID:
dotnet-trace collect -p <pid> --profile cpu-sampling --duration 00:00:45
# or launch under the trace directly:
dotnet-trace collect -- "<path>\PlanCope.Local.Host.exe" --duration 00:00:45
dotnet-trace report plancope-trace.nettrace topN
```

Cross-reference the wall-clock segments (T0–T4) with the trace; the report should say how
many of the cold-start seconds are JIT/startup, SQLite I/O, and WebView2 initialization.

### 4.5 Idle memory footprint

1. Reach the host console, activate, and stop. Close the student view if one is open.
2. Do nothing for **2 minutes**.
3. Sum committed private bytes **and** working set across the whole process tree — the host
   plus its WebView2 processes:

```powershell
$tree = Get-CimInstance Win32_Process | Where-Object {
  $_.Name -eq 'PlanCope.Local.Host.exe' -or (
    $_.Name -eq 'msedgewebview2.exe' -and
    $_.CommandLine -like '*plancope*'
  )
}
$procs = $tree.ProcessId | ForEach-Object { Get-Process -Id $_ -ErrorAction SilentlyContinue }
$ws   = ($procs | Measure-Object WorkingSet64         -Sum).Sum / 1GB
$priv = ($procs | Measure-Object PrivateMemorySize64  -Sum).Sum / 1GB
"working_set={0:N2}GB private_committed={1:N2}GB procs=$($procs.Count)" -f $ws, $priv
```

(The `*plancope*` command-line filter keeps unrelated Edge/WebView2 instances out of the
count; tighten the match to the app's WebView2 user-data folder if needed.)

4. The budget is on **committed private bytes ≤ 1.5 GB (ceiling) / ≤ 1.0 GB (target)**.
   Working set is reported alongside for context. Task Manager's "Commit size" view is an
   acceptable GUI cross-check.

### 4.6 100-question render and scroll (DevTools)

The student exam runs in the default browser at `http://127.0.0.1:<port>/examen/<sessionId>`;
open that URL in Edge/Chrome (same engine as WebView2).

**Setup.** Use a real 100-question payload (see `docs/local-exam-format.md` and
`docs/local-exam-extensive-sample.json`). Open DevTools → Performance → CPU throttling
**6×**. Network throttling is not applicable (localhost) — note it in the report instead of
skipping it silently.

**First paint.** Start a Performance recording, open the session, and measure from the moment
the session-data fetch response arrives (visible in the Network panel) to the first painted
question. That interval is bounded by the JSON parse task + React commit + first layout.
Budget: **≤ 300 ms**.

**Scroll.** Record a Performance trace while scrolling the `.student-questions` container
from top to bottom at human speed (10 s pass). In the recording's "Frames" section: average
frame time must be **≤ 16.7 ms** and **dropped frames (> 50 ms) must be 0**. For an
automated, reproducible pass, run this in the page console during the recording:

```js
const container = document.querySelector('.student-questions');
const frames = [];
let last = performance.now();
const tick = t => { frames.push(t - last); last = t; requestAnimationFrame(tick); };
requestAnimationFrame(tick);
(async () => {
  let y = 0;
  while (y < container.scrollHeight) {
    container.scrollTop = y;
    y += container.clientHeight * 0.8;
    await new Promise(r => setTimeout(r, 16));
  }
  const avg = frames.reduce((a, b) => a + b, 0) / frames.length;
  console.log(JSON.stringify({
    avgFrameMs: avg,
    over16_7: frames.filter(f => f > 16.7).length,
    droppedFramesOver50: frames.filter(f => f > 50).length,
    totalFrames: frames.length
  }));
})();
```

Budgets: `droppedFramesOver50 === 0` and `avgFrameMs <= 16.7`.

**QuestionNav jump.** Record a trace, click a far-away question (e.g. #1 → #90), and confirm
the smooth scroll starts within 150 ms and produces no dropped frame.

**Structural expectation for the virtualization work** (not a measurement): during the pass
the rendered question DOM must stay O(viewport) — the list may not be eagerly building all
100 blocks. This is what makes the frame budget achievable on 2 cores.

### 4.7 Recording results

Publish into the B8 batch report, per budget: median/min/max over 3 runs, machine
identification (or "emulation: CPU 6×, no HDD emulation"), the tool used (stopwatch trace /
WPR / dotnet-trace / DevTools), and any segment attribution. A green CI build is not evidence
for any of this; these are manual/emulated benchmarks on the reference profile.

## 5. What has NOT been verified

Explicit list — do not shrink it when copying into a report; add what you measured:

- No cold-start measurement on reference hardware (or any hardware) exists yet.
- No idle-memory measurement exists yet.
- No 100-question render/frame measurement exists yet; the virtualization work has not been
  benchmarked against the §3.3 contract.
- WebView2 and the embedded bundle have not been profiled on an HDD.
- DevTools CPU-throttle numbers are a proxy, not a substitute for the real hardware.

The numbers in §3 are the project's committed targets. They stand until a measurement — on
the reference profile or an explicitly-labelled emulation — either confirms them or revises
them with evidence.