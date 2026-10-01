DELETE FROM submission_answers
WHERE block_id IN (
    SELECT id FROM local_exam_blocks
    WHERE block_type IN ('text', 'image', 'short_answer', 'Text', 'Image', 'ShortAnswer')
);

DELETE FROM attempt_results
WHERE student_attempt_id IN (
    SELECT attempt.id
    FROM student_attempts AS attempt
    JOIN delivery_sessions AS session ON session.id = attempt.delivery_session_id
    WHERE session.exam_version_id IN (
        SELECT DISTINCT local_exam_version_id FROM local_exam_blocks
        WHERE block_type IN ('text', 'image', 'short_answer', 'Text', 'Image', 'ShortAnswer')
    )
);

DELETE FROM stats_rollup_blocks
WHERE rollup_id IN (
    SELECT id FROM stats_rollups
    WHERE exam_version_id IN (
        SELECT DISTINCT local_exam_version_id FROM local_exam_blocks
        WHERE block_type IN ('text', 'image', 'short_answer', 'Text', 'Image', 'ShortAnswer')
    )
);

DELETE FROM stats_rollups
WHERE exam_version_id IN (
    SELECT DISTINCT local_exam_version_id FROM local_exam_blocks
    WHERE block_type IN ('text', 'image', 'short_answer', 'Text', 'Image', 'ShortAnswer')
);

DELETE FROM local_answer_keys
WHERE remote_block_id IN (
    SELECT remote_block_id FROM local_exam_blocks
    WHERE block_type IN ('text', 'image', 'short_answer', 'Text', 'Image', 'ShortAnswer')
);

DELETE FROM local_exam_blocks
WHERE block_type IN ('text', 'image', 'short_answer', 'Text', 'Image', 'ShortAnswer');
