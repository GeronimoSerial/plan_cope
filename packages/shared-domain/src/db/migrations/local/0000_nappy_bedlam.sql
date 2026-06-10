CREATE TABLE `app_settings` (
	`id` text PRIMARY KEY NOT NULL,
	`key` text NOT NULL,
	`value_json` text NOT NULL,
	`updated_at` text NOT NULL
);
--> statement-breakpoint
CREATE UNIQUE INDEX `app_settings_key_unique` ON `app_settings` (`key`);--> statement-breakpoint
CREATE TABLE `delivery_sessions` (
	`id` text PRIMARY KEY NOT NULL,
	`exam_version_id` text NOT NULL,
	`school_code` text NOT NULL,
	`classroom_code` text,
	`commission_code` text,
	`started_by` text NOT NULL,
	`start_at` text NOT NULL,
	`end_at` text,
	`status` text NOT NULL,
	`config_json` text
);
--> statement-breakpoint
CREATE TABLE `local_answer_keys` (
	`id` text PRIMARY KEY NOT NULL,
	`local_exam_version_id` text NOT NULL,
	`remote_block_id` text NOT NULL,
	`correct_answer_json` text NOT NULL,
	`score_value` integer,
	FOREIGN KEY (`local_exam_version_id`) REFERENCES `local_exam_versions`(`id`) ON UPDATE no action ON DELETE cascade
);
--> statement-breakpoint
CREATE INDEX `idx_local_answer_keys_version_id` ON `local_answer_keys` (`local_exam_version_id`);--> statement-breakpoint
CREATE TABLE `local_assets` (
	`id` text PRIMARY KEY NOT NULL,
	`remote_asset_id` text NOT NULL,
	`local_exam_version_id` text NOT NULL,
	`file_name` text NOT NULL,
	`mime_type` text NOT NULL,
	`checksum` text NOT NULL,
	`local_path` text NOT NULL,
	`synced_at` text NOT NULL,
	FOREIGN KEY (`local_exam_version_id`) REFERENCES `local_exam_versions`(`id`) ON UPDATE no action ON DELETE cascade
);
--> statement-breakpoint
CREATE INDEX `idx_local_assets_version_id` ON `local_assets` (`local_exam_version_id`);--> statement-breakpoint
CREATE TABLE `local_audit_logs` (
	`id` text PRIMARY KEY NOT NULL,
	`actor_id` text,
	`action` text NOT NULL,
	`entity_type` text NOT NULL,
	`entity_id` text NOT NULL,
	`payload_json` text,
	`created_at` text NOT NULL
);
--> statement-breakpoint
CREATE INDEX `idx_local_audit_logs_entity` ON `local_audit_logs` (`entity_type`,`entity_id`);--> statement-breakpoint
CREATE TABLE `local_exam_blocks` (
	`id` text PRIMARY KEY NOT NULL,
	`local_exam_version_id` text NOT NULL,
	`remote_block_id` text NOT NULL,
	`order_index` integer NOT NULL,
	`block_type` text NOT NULL,
	`config_json` text NOT NULL,
	`validation_json` text,
	FOREIGN KEY (`local_exam_version_id`) REFERENCES `local_exam_versions`(`id`) ON UPDATE no action ON DELETE cascade,
	CONSTRAINT "CK_local_exam_blocks_block_type" CHECK("local_exam_blocks"."block_type" IN ('text', 'image', 'multiple_choice', 'true_false', 'short_answer'))
);
--> statement-breakpoint
CREATE INDEX `idx_local_exam_blocks_version_id` ON `local_exam_blocks` (`local_exam_version_id`);--> statement-breakpoint
CREATE TABLE `local_exam_versions` (
	`id` text PRIMARY KEY NOT NULL,
	`remote_exam_version_id` text NOT NULL,
	`exam_code` text NOT NULL,
	`version_number` integer NOT NULL,
	`checksum` text NOT NULL,
	`metadata_json` text,
	`schema_version` integer NOT NULL,
	`synced_at` text NOT NULL
);
--> statement-breakpoint
CREATE INDEX `idx_local_exam_versions_remote` ON `local_exam_versions` (`remote_exam_version_id`);--> statement-breakpoint
CREATE TABLE `local_users` (
	`id` text PRIMARY KEY NOT NULL,
	`username` text NOT NULL,
	`password_hash` text NOT NULL,
	`role` text NOT NULL,
	`active` integer DEFAULT 1 NOT NULL,
	`last_login_at` text,
	`created_at` text NOT NULL
);
--> statement-breakpoint
CREATE UNIQUE INDEX `local_users_username_unique` ON `local_users` (`username`);--> statement-breakpoint
CREATE TABLE `student_attempts` (
	`id` text PRIMARY KEY NOT NULL,
	`delivery_session_id` text NOT NULL,
	`student_code` text NOT NULL,
	`status` text NOT NULL,
	`started_at` text NOT NULL,
	`submitted_at` text,
	`local_sequence` integer NOT NULL,
	`confirmation_code` text,
	FOREIGN KEY (`delivery_session_id`) REFERENCES `delivery_sessions`(`id`) ON UPDATE no action ON DELETE cascade,
	CONSTRAINT "CK_student_attempts_status" CHECK("student_attempts"."status" IN ('pending', 'in_progress', 'submitted', 'graded'))
);
--> statement-breakpoint
CREATE INDEX `idx_student_attempts_delivery_id` ON `student_attempts` (`delivery_session_id`);--> statement-breakpoint
CREATE INDEX `idx_student_attempts_student_code` ON `student_attempts` (`student_code`);--> statement-breakpoint
CREATE TABLE `submission_answers` (
	`id` text PRIMARY KEY NOT NULL,
	`student_attempt_id` text NOT NULL,
	`block_id` text NOT NULL,
	`answer_json` text NOT NULL,
	`created_at` text NOT NULL,
	FOREIGN KEY (`student_attempt_id`) REFERENCES `student_attempts`(`id`) ON UPDATE no action ON DELETE cascade,
	FOREIGN KEY (`block_id`) REFERENCES `local_exam_blocks`(`id`) ON UPDATE no action ON DELETE no action
);
--> statement-breakpoint
CREATE INDEX `idx_submission_answers_attempt_id` ON `submission_answers` (`student_attempt_id`);--> statement-breakpoint
CREATE INDEX `idx_submission_answers_block_id` ON `submission_answers` (`block_id`);--> statement-breakpoint
CREATE TABLE `sync_attempts` (
	`id` text PRIMARY KEY NOT NULL,
	`direction` text NOT NULL,
	`status` text NOT NULL,
	`started_at` text NOT NULL,
	`finished_at` text,
	`summary_json` text,
	`error_json` text,
	CONSTRAINT "CK_sync_attempts_direction" CHECK("sync_attempts"."direction" IN ('inbound', 'outbound')),
	CONSTRAINT "CK_sync_attempts_status" CHECK("sync_attempts"."status" IN ('running', 'completed', 'failed'))
);
--> statement-breakpoint
CREATE INDEX `idx_sync_attempts_direction` ON `sync_attempts` (`direction`,`started_at`);--> statement-breakpoint
CREATE TABLE `sync_outbox` (
	`id` text PRIMARY KEY NOT NULL,
	`event_type` text NOT NULL,
	`aggregate_type` text NOT NULL,
	`aggregate_id` text NOT NULL,
	`idempotency_key` text NOT NULL,
	`payload_json` text NOT NULL,
	`status` text DEFAULT 'pending' NOT NULL,
	`retry_count` integer DEFAULT 0 NOT NULL,
	`next_retry_at` text,
	`last_error` text,
	`created_at` text NOT NULL,
	`processed_at` text,
	CONSTRAINT "CK_sync_outbox_status" CHECK("sync_outbox"."status" IN ('pending', 'processing', 'completed', 'failed', 'discarded'))
);
--> statement-breakpoint
CREATE UNIQUE INDEX `sync_outbox_idempotency_key_unique` ON `sync_outbox` (`idempotency_key`);--> statement-breakpoint
CREATE INDEX `idx_sync_outbox_pending` ON `sync_outbox` (`status`,`retry_count`,`next_retry_at`);--> statement-breakpoint
CREATE INDEX `idx_sync_outbox_aggregate` ON `sync_outbox` (`aggregate_type`,`aggregate_id`);--> statement-breakpoint
CREATE TABLE `sync_state` (
	`id` text PRIMARY KEY NOT NULL,
	`key` text NOT NULL,
	`value_json` text NOT NULL,
	`updated_at` text NOT NULL
);
--> statement-breakpoint
CREATE UNIQUE INDEX `sync_state_key_unique` ON `sync_state` (`key`);