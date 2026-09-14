# Change log

## 2026-09-14 — Source review fixes (not yet packaged)

The review identified six defects. This revision addresses them without adding
third-party dependencies. Runtime performance and crash stability have not been
remeasured; the installed executable remains the earlier build.

1. **Uninstall ownership:** replaced recursive enumeration/deletion with three
   explicit program filenames and known settings/log filenames. Resolve paths,
   reject relative/root targets and files resolving outside the target, preserve
   other files/subdirectories, and remove only empty directories. Self-removal
   is restricted to the installed frozen uninstaller, waits for the wizard to
   exit, and uses an encoded PowerShell command with literal paths. A source
   Python interpreter or an external setup executable is never deleted.
   Source-mode installs now require the real setup EXE instead of copying the
   application as an uninstaller. Shortcut paths escape apostrophes and the
   PowerShell helper checks exit status.
2. **Disk temperature:** corrected the first sensor offset from 18 to 26,
   accounting for the descriptor's reserved array, and validate complete entries
   against returned length and declared size/count. The synthetic 55 C case now
   returns 55 rather than 0. Layout references:
   [Microsoft descriptor](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/ntddstor/ns-ntddstor-_storage_temperature_data_descriptor)
   and [sensor entry](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/ntddstor/ns-ntddstor-_storage_temperature_info).
3. **Retained drawing:** `Painter.end()`/`clear()` hide items instead of deleting
   them. Retain separate primitives for a shared key, restore explicit visibility
   and stacking order, and reuse existing items across CPU/Memory/Disk rotation.
   Two simulated rotation cycles allocate no new items on the second cycle and
   delete none. Cache size follows the distinct keys/primitives seen, not frames.
4. **UI responsiveness:** the default `ProcessSampler` uses a persistent helper
   with synchronous probes and no Tk root. The UI polls responses, allows one
   request at a time, and sends the latest settings on the next request. A sample
   exceeding 20 seconds terminates the helper, clears stale readings, and retries
   after five minutes; process reaping never waits on the UI thread. Optional
   WMI runs in that helper. The legacy thread option remains. This intentionally
   adds a process (and potentially onefile bootloader overhead) rather than
   claiming a synchronous probe backoff can prevent blocking.
5. **I/O rates:** baseline timestamps belong to each successful drive read.
   A skipped interval no longer divides accumulated bytes by the last global
   tick: the synthetic 300 MB over 300 seconds case reports 1 MB/s, not 300.
   Remove cached I/O, space, temperature and identity for disconnected letters.
6. **Error evidence:** connect Tk callback reporting to diagnostics, log caught
   sampling/probe/IPC errors with tracebacks, rate-limit each context to one
   report per minute, and reschedule the UI tick after recoverable errors.

The prior attribution of `alloc: invalid block` solely to threading has been
removed. [CPython #66999](https://github.com/python/cpython/issues/66999) reports
a similar allocator message after a file dialog; it does not prove the cause
of this application's crash. Passive long-run observation is still required.

### Validation

`python -B -m unittest discover -s tests -v` passed **20 tests**, including one
real spawned worker with synthetic hardware, timeout/restart and IPC-failure
cases, canvas reuse/visibility/order, temperature parsing, skipped I/O probes,
exception logging, and safe uninstall target selection.

All deletion, registry, and device operations in regression tests are mocked.
No stress tests, hardware polling, UI windows, installation, config modifications
or dependency installation were performed. Build artifacts were not regenerated;
frozen worker startup, interactive layout and long-run memory/crash behavior are
not covered by these checks. The README's old benchmarks are explicitly historical.

## 2026-09-14 — Follow-up validation and standalone build support

- All 21 tests passed with `SYSMONITOR_TK_TEST=1`. The extra integration check
  renders real Tk canvases through mini/expanded views, display modes, languages,
  themes, opacity and resizing. Repeated layouts keep the same canvas item IDs
  and callback counts. User settings saves are mocked.
- A normal source launch used isolated config/logs under `build/ui-smoke` and
  logged `sampler=process` without an error. Native window automation could not
  target the frameless widget, so no manual-input/visual QA claim is made.
- Added `build.cmd --app-only`, which reuses the existing icon and builds only
  `dist/SysMonitor.exe`. The installer is explicitly excluded from this request.
- Log the first completed worker sample with core/drive counts and worker PID,
  allowing source and frozen startup to be checked without a ten-minute wait.
- Initialized local Git on `main`. The user supplied
  `https://github.com/epinephrinerx/SystemMonitor` as `origin`; the initial remote
  check found no existing refs to preserve or merge.
