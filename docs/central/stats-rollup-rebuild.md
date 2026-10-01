# Rebuilding Central statistics rollups

Central now attributes newly pushed attempts from their delivery session and can also attribute older attempts from the roster section and its snapshot. After deploying this change, run a rebuild to populate statistics for attempts that Central received before session blocks were included in push payloads.

Send an authenticated administrator request to:

```http
POST /api/admin/stats/rebuild
```

The endpoint clears and recomputes `ExamRollups` and `ExamRollupBlocks` from stored, graded `ReceivedStudentAttempts` and their Central grade results. It returns the number of attempts rebuilt. Running it again produces the same aggregate values. Attempts without a valid roster attribution or recoverable exam version are logged and skipped.
