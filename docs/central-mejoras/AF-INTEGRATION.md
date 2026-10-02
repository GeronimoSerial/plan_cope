# Central A–F integration record

Date: 2026-10-02

## Scope and delivery

This integration brings the completed Central batches A–F and the AD review fixes together. A was merged in PR [#91](https://github.com/GeronimoSerial/plan_cope/pull/91). B–F and the D API slices were submitted as the dependent PR stack below; the final report is intended to be the stack's last documentation layer.

| PR | Batch | Change |
| --- | --- | --- |
| [#92](https://github.com/GeronimoSerial/plan_cope/pull/92) | B | Create exams without user-entered codes |
| [#93](https://github.com/GeronimoSerial/plan_cope/pull/93) | C | Shared question preview |
| [#94](https://github.com/GeronimoSerial/plan_cope/pull/94) | D1 | Scoped summary and shared contracts |
| [#95](https://github.com/GeronimoSerial/plan_cope/pull/95) | D2 | Paged statistics catalogs |
| [#96](https://github.com/GeronimoSerial/plan_cope/pull/96) | D3 | SQL grouped aggregates |
| [#97](https://github.com/GeronimoSerial/plan_cope/pull/97) | D4 + AD | Scoped school list, geography and CUE query fix |
| [#98](https://github.com/GeronimoSerial/plan_cope/pull/98) | D5 | Typed server API wrappers |
| [#99](https://github.com/GeronimoSerial/plan_cope/pull/99) | E1 | Dashboard operational metrics |
| [#100](https://github.com/GeronimoSerial/plan_cope/pull/100) | E2 | Paged school directory |
| [#101](https://github.com/GeronimoSerial/plan_cope/pull/101) | E3 | Aggregated school detail |
| [#103](https://github.com/GeronimoSerial/plan_cope/pull/103) | F | Multidimensional statistics explorer |
| #104 | Docs | This integration record |

The stack is built from reviewable, dependent commits. The final stack merge must use GitHub's asynchronous stack merge API and waits for the required checks across the stack.

## Review findings addressed

- Statistics catalogs and aggregates filter, group, order and page in SQL.
- School geography is loaded in bulk for the selected page; active authorized schools remain visible without rollups.
- Historical subject fallback is limited to published package targets.
- The legacy school detail endpoint filters normalized CUE in SQL before reading one school and its locality/department.
- Suppression, unavailable and zero values remain distinct through the contracts and UI.
- No statistics models, migrations, schema snapshot, Local code or sharing implementation was included.

## Local validation

- Root `npm ci` passed in the isolated integration worktree and installed the declared TypeScript 6.0.3.
- Full Central Web Vitest suite: 256/256 passed. The new code-free exam creation test also passed by itself.
- TypeScript 6.0.3 `tsc --noEmit` passed. ESLint reported no errors and 8 warnings in unrelated existing files.
- Production Next build passed.
- Focused Central API tests: 33 passed, 2 PostgreSQL Testcontainers tests skipped because this machine had no running Docker daemon. SyncCompat `ExamsContractTests`: 18/18 passed.
- Mobile preview at 320 px and desktop at 1440 px had no horizontal document overflow; header and footer spanned the viewport. The statistics route required a local test session cookie.
- `git diff --check` passed on the isolated implementation before PR publication.

The available local PostgreSQL checks did not execute a real fixture. Npgsql `ToQueryString` coverage verifies SQL translation for grouped/paged dimensions and catalogs, not server execution, query plans, concurrency or production data. The PR CI remains the merge gate for Docker-backed checks.

## Workspace boundaries

All builds and integration changes were made in the isolated Orca integration worktree based on `origin/main`; no `git add -A`, global stash or destructive reset was used. The active G worktree, its partial StatsShare files, migrations, models, UI/BFF and tests were excluded. `estadisticas/page.tsx` was kept without a link to the unfinished G route.

During transfer preparation, one script briefly wrote generated controller content into the shared G checkout. I reconstructed its untracked `StatsQueryController.cs` from that checkout's own partial controller files, removed only the three generated files, and notified the coordinator for reconciliation. No other G files were intentionally changed; the coordinator was asked to compare that recovered file before G resumes.

No deployment or release was run; only normal pull request CI was used.
