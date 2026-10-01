# Rebuilding Central statistics rollups

Central now attributes newly pushed attempts from their delivery session and can also attribute older attempts from the roster section and its snapshot. After deploying this change, run a rebuild to populate statistics for attempts that Central received before session blocks were included in push payloads.

Run the rebuild during a quiet window, such as after node pushes have paused. It clears the current rollup tables and repopulates them in batches, so statistics can be incomplete while a long rebuild is running. Duration depends on the number of received attempts; large installations may take several minutes. A PostgreSQL advisory lock prevents a second rebuild from starting and makes attempt pushes wait until rebuilding finishes.

Send the request with an active administrator access token:

```http
POST /api/admin/stats/rebuild
Authorization: Bearer <admin-access-token>
```

The endpoint recomputes `ExamRollups` and `ExamRollupBlocks` from stored, graded `ReceivedStudentAttempts` and their Central grade results. It returns the number of attempts rebuilt. Running it again produces the same aggregate values. Attempts without a valid roster attribution or recoverable exam version are logged and skipped. If another rebuild is already running, it returns `409 Conflict`; retry after the active rebuild completes.
