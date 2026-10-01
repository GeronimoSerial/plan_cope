# Rebuilding Central statistics rollups

Central attributes attempts from the roster section and snapshot when available, and falls back to the CUE, school year, and course captured on the delivery session. If a session has no school year or course, its attempt is grouped under `sin_asignar`; this keeps a valid, known session CUE visible while making the missing classification explicit.

Run the rebuild during a quiet window, such as after node pushes have paused. It clears the current rollup tables and repopulates them in batches, so statistics can be incomplete while a long rebuild is running. Duration depends on the number of received attempts; large installations may take several minutes. A PostgreSQL advisory lock prevents a second rebuild from starting and makes attempt pushes wait until rebuilding finishes.

The Central Web **Sincronización recibida** page is the recovery path. Its **Reprocesar** action re-runs grading for received attempts without successful attribution or with an ungradable result, then rebuilds all rollups from the latest stored grade for each attempt. Repeating the action is safe: rollups are cleared and reconstructed, and the current grading result is updated rather than counted twice.

The lower-level rebuild endpoint remains available with an active administrator access token:

```http
POST /api/admin/stats/rebuild
Authorization: Bearer <admin-access-token>
```

The endpoint recomputes `ExamRollups` and `ExamRollupBlocks` from stored, graded `ReceivedStudentAttempts` and their latest Central grade results. It returns the number of attempts rebuilt. Attempts without a valid known CUE or recoverable grade are logged with an attempt ID and persisted reason. If another rebuild is already running, it returns `409 Conflict`; retry after the active rebuild completes.
