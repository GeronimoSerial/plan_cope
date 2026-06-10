CREATE TABLE IF NOT EXISTS "answer_keys" (
	"id" uuid PRIMARY KEY DEFAULT gen_random_uuid() NOT NULL,
	"exam_block_id" uuid NOT NULL,
	"correct_answer_json" jsonb NOT NULL,
	"score_value" numeric(10, 2),
	"metadata_json" jsonb,
	"created_at" timestamp with time zone DEFAULT now() NOT NULL,
	"updated_at" timestamp with time zone DEFAULT now() NOT NULL
);
--> statement-breakpoint
CREATE TABLE IF NOT EXISTS "asset_usages" (
	"id" uuid PRIMARY KEY DEFAULT gen_random_uuid() NOT NULL,
	"exam_block_id" uuid NOT NULL,
	"exam_asset_id" uuid NOT NULL,
	"usage_type" text NOT NULL,
	"created_at" timestamp with time zone DEFAULT now() NOT NULL,
	"updated_at" timestamp with time zone DEFAULT now() NOT NULL,
	CONSTRAINT "CK_asset_usages_usage_type" CHECK ("asset_usages"."usage_type" IN ('prompt', 'option_image', 'reference', 'attachment'))
);
--> statement-breakpoint
CREATE TABLE IF NOT EXISTS "audit_logs" (
	"id" uuid PRIMARY KEY DEFAULT gen_random_uuid() NOT NULL,
	"actor_id" uuid,
	"entity_type" text NOT NULL,
	"entity_id" text NOT NULL,
	"action" text NOT NULL,
	"payload_json" jsonb,
	"ip_address" text,
	"created_at" timestamp with time zone DEFAULT now() NOT NULL
);
--> statement-breakpoint
CREATE TABLE IF NOT EXISTS "central_delivery_sessions" (
	"id" uuid PRIMARY KEY DEFAULT gen_random_uuid() NOT NULL,
	"remote_local_id" text NOT NULL,
	"school_id" uuid,
	"exam_version_id" uuid,
	"classroom_code" text,
	"commission_code" text,
	"status" text NOT NULL,
	"started_at" timestamp with time zone,
	"ended_at" timestamp with time zone,
	"synced_at" timestamp with time zone,
	"created_at" timestamp with time zone DEFAULT now() NOT NULL,
	CONSTRAINT "CK_central_delivery_sessions_status" CHECK ("central_delivery_sessions"."status" IN ('pending', 'active', 'completed', 'cancelled'))
);
--> statement-breakpoint
CREATE TABLE IF NOT EXISTS "classrooms" (
	"id" uuid PRIMARY KEY DEFAULT gen_random_uuid() NOT NULL,
	"school_id" uuid NOT NULL,
	"code" text NOT NULL,
	"name" text NOT NULL,
	"shift" text,
	"metadata_json" jsonb,
	"deleted_at" timestamp with time zone,
	"created_at" timestamp with time zone DEFAULT now() NOT NULL,
	"updated_at" timestamp with time zone DEFAULT now() NOT NULL,
	CONSTRAINT "CK_classrooms_shift" CHECK ("classrooms"."shift" IS NULL OR "classrooms"."shift" IN ('morning', 'afternoon', 'night', 'full_day'))
);
--> statement-breakpoint
CREATE TABLE IF NOT EXISTS "departments" (
	"id" uuid PRIMARY KEY DEFAULT gen_random_uuid() NOT NULL,
	"code" text NOT NULL,
	"name" text NOT NULL,
	"province_id" uuid NOT NULL,
	"created_at" timestamp with time zone DEFAULT now() NOT NULL
);
--> statement-breakpoint
CREATE TABLE IF NOT EXISTS "exam_assets" (
	"id" uuid PRIMARY KEY DEFAULT gen_random_uuid() NOT NULL,
	"exam_version_id" uuid NOT NULL,
	"file_name" text NOT NULL,
	"mime_type" text NOT NULL,
	"size_bytes" bigint NOT NULL,
	"checksum" text NOT NULL,
	"storage_path" text NOT NULL,
	"created_at" timestamp with time zone DEFAULT now() NOT NULL,
	CONSTRAINT "CK_exam_assets_size_positive" CHECK ("exam_assets"."size_bytes" > 0)
);
--> statement-breakpoint
CREATE TABLE IF NOT EXISTS "exam_block_options" (
	"id" uuid PRIMARY KEY DEFAULT gen_random_uuid() NOT NULL,
	"exam_block_id" uuid NOT NULL,
	"value" text NOT NULL,
	"label" text NOT NULL,
	"order_index" integer DEFAULT 0 NOT NULL,
	"metadata_json" jsonb,
	"created_at" timestamp with time zone DEFAULT now() NOT NULL,
	"updated_at" timestamp with time zone DEFAULT now() NOT NULL,
	CONSTRAINT "CK_exam_block_options_order" CHECK ("exam_block_options"."order_index" >= 0)
);
--> statement-breakpoint
CREATE TABLE IF NOT EXISTS "exam_blocks" (
	"id" uuid PRIMARY KEY DEFAULT gen_random_uuid() NOT NULL,
	"exam_version_id" uuid NOT NULL,
	"order_index" integer NOT NULL,
	"block_type" text NOT NULL,
	"title" text,
	"description" text,
	"config_json" jsonb NOT NULL,
	"validation_json" jsonb,
	"created_at" timestamp with time zone DEFAULT now() NOT NULL,
	"updated_at" timestamp with time zone DEFAULT now() NOT NULL,
	CONSTRAINT "CK_exam_blocks_block_type" CHECK ("exam_blocks"."block_type" IN ('text', 'image', 'multiple_choice', 'true_false', 'short_answer'))
);
--> statement-breakpoint
CREATE TABLE IF NOT EXISTS "exam_versions" (
	"id" uuid PRIMARY KEY DEFAULT gen_random_uuid() NOT NULL,
	"exam_id" uuid NOT NULL,
	"version_number" integer NOT NULL,
	"schema_version" integer DEFAULT 1 NOT NULL,
	"status" text DEFAULT 'draft' NOT NULL,
	"metadata_json" jsonb,
	"created_by" uuid,
	"reviewed_by" uuid,
	"approved_by" uuid,
	"published_by" uuid,
	"published_at" timestamp with time zone,
	"created_at" timestamp with time zone DEFAULT now() NOT NULL,
	"updated_at" timestamp with time zone DEFAULT now() NOT NULL,
	CONSTRAINT "CK_exam_versions_status" CHECK ("exam_versions"."status" IN ('draft', 'review', 'approved', 'published', 'archived')),
	CONSTRAINT "CK_exam_versions_version_positive" CHECK ("exam_versions"."version_number" > 0)
);
--> statement-breakpoint
CREATE TABLE IF NOT EXISTS "exams" (
	"id" uuid PRIMARY KEY DEFAULT gen_random_uuid() NOT NULL,
	"code" text NOT NULL,
	"title" text NOT NULL,
	"description" text,
	"level" text,
	"area" text,
	"subject" text,
	"status" text DEFAULT 'active' NOT NULL,
	"deleted_at" timestamp with time zone,
	"created_at" timestamp with time zone DEFAULT now() NOT NULL,
	"updated_at" timestamp with time zone DEFAULT now() NOT NULL,
	CONSTRAINT "CK_exams_status" CHECK ("exams"."status" IN ('active', 'inactive', 'archived'))
);
--> statement-breakpoint
CREATE TABLE IF NOT EXISTS "localities" (
	"id" uuid PRIMARY KEY DEFAULT gen_random_uuid() NOT NULL,
	"department_id" uuid NOT NULL,
	"code" text NOT NULL,
	"postal_code" text,
	"name" text NOT NULL,
	"created_at" timestamp with time zone DEFAULT now() NOT NULL
);
--> statement-breakpoint
CREATE TABLE IF NOT EXISTS "provinces" (
	"id" uuid PRIMARY KEY DEFAULT gen_random_uuid() NOT NULL,
	"code" text NOT NULL,
	"name" text NOT NULL,
	"created_at" timestamp with time zone DEFAULT now() NOT NULL
);
--> statement-breakpoint
CREATE TABLE IF NOT EXISTS "publication_packages" (
	"id" uuid PRIMARY KEY DEFAULT gen_random_uuid() NOT NULL,
	"exam_version_id" uuid NOT NULL,
	"package_version" integer NOT NULL,
	"checksum" text NOT NULL,
	"manifest_json" jsonb NOT NULL,
	"status" text DEFAULT 'published' NOT NULL,
	"created_at" timestamp with time zone DEFAULT now() NOT NULL,
	"published_at" timestamp with time zone,
	CONSTRAINT "CK_publication_packages_status" CHECK ("publication_packages"."status" IN ('draft', 'published', 'revoked')),
	CONSTRAINT "CK_publication_packages_version_positive" CHECK ("publication_packages"."package_version" > 0)
);
--> statement-breakpoint
CREATE TABLE IF NOT EXISTS "publication_targets" (
	"id" uuid PRIMARY KEY DEFAULT gen_random_uuid() NOT NULL,
	"publication_package_id" uuid NOT NULL,
	"target_type" text NOT NULL,
	"target_id" text NOT NULL,
	"created_at" timestamp with time zone DEFAULT now() NOT NULL,
	"updated_at" timestamp with time zone DEFAULT now() NOT NULL,
	CONSTRAINT "CK_publication_targets_type" CHECK ("publication_targets"."target_type" IN ('school', 'department', 'province', 'node', 'global'))
);
--> statement-breakpoint
CREATE TABLE IF NOT EXISTS "received_student_attempts" (
	"id" uuid PRIMARY KEY DEFAULT gen_random_uuid() NOT NULL,
	"remote_local_id" text NOT NULL,
	"delivery_session_id" uuid,
	"student_code" text NOT NULL,
	"status" text NOT NULL,
	"started_at" timestamp with time zone,
	"submitted_at" timestamp with time zone,
	"received_at" timestamp with time zone DEFAULT now() NOT NULL,
	"idempotency_key" text NOT NULL,
	"created_at" timestamp with time zone DEFAULT now() NOT NULL,
	CONSTRAINT "CK_received_student_attempts_status" CHECK ("received_student_attempts"."status" IN ('pending', 'in_progress', 'submitted', 'graded'))
);
--> statement-breakpoint
CREATE TABLE IF NOT EXISTS "received_submission_answers" (
	"id" uuid PRIMARY KEY DEFAULT gen_random_uuid() NOT NULL,
	"student_attempt_id" uuid NOT NULL,
	"block_id" uuid NOT NULL,
	"answer_json" jsonb NOT NULL,
	"created_at" timestamp with time zone DEFAULT now() NOT NULL
);
--> statement-breakpoint
CREATE TABLE IF NOT EXISTS "registered_nodes" (
	"id" uuid PRIMARY KEY DEFAULT gen_random_uuid() NOT NULL,
	"school_id" uuid,
	"node_code" text NOT NULL,
	"device_name" text,
	"status" text DEFAULT 'active' NOT NULL,
	"last_seen_at" timestamp with time zone,
	"created_at" timestamp with time zone DEFAULT now() NOT NULL,
	"updated_at" timestamp with time zone DEFAULT now() NOT NULL,
	CONSTRAINT "CK_registered_nodes_status" CHECK ("registered_nodes"."status" IN ('active', 'inactive', 'blocked'))
);
--> statement-breakpoint
CREATE TABLE IF NOT EXISTS "roles" (
	"id" uuid PRIMARY KEY DEFAULT gen_random_uuid() NOT NULL,
	"code" text NOT NULL,
	"name" text NOT NULL,
	"description" text,
	"created_at" timestamp with time zone DEFAULT now() NOT NULL
);
--> statement-breakpoint
CREATE TABLE IF NOT EXISTS "schools" (
	"id" uuid PRIMARY KEY DEFAULT gen_random_uuid() NOT NULL,
	"code" text NOT NULL,
	"cue" bigint NOT NULL,
	"annex" integer,
	"name" text NOT NULL,
	"locality_id" uuid NOT NULL,
	"status" text DEFAULT 'active' NOT NULL,
	"deleted_at" timestamp with time zone,
	"created_at" timestamp with time zone DEFAULT now() NOT NULL,
	"updated_at" timestamp with time zone DEFAULT now() NOT NULL,
	CONSTRAINT "CK_schools_status" CHECK ("schools"."status" IN ('active', 'inactive', 'closed'))
);
--> statement-breakpoint
CREATE TABLE IF NOT EXISTS "settings" (
	"id" uuid PRIMARY KEY DEFAULT gen_random_uuid() NOT NULL,
	"key" text NOT NULL,
	"value_json" jsonb NOT NULL,
	"updated_by" uuid,
	"created_at" timestamp with time zone DEFAULT now() NOT NULL,
	"updated_at" timestamp with time zone DEFAULT now() NOT NULL
);
--> statement-breakpoint
CREATE TABLE IF NOT EXISTS "sync_attempts" (
	"id" uuid PRIMARY KEY DEFAULT gen_random_uuid() NOT NULL,
	"node_id" uuid,
	"direction" text NOT NULL,
	"status" text NOT NULL,
	"started_at" timestamp with time zone DEFAULT now() NOT NULL,
	"finished_at" timestamp with time zone,
	"summary_json" jsonb,
	"error_json" jsonb,
	CONSTRAINT "CK_sync_attempts_direction" CHECK ("sync_attempts"."direction" IN ('inbound', 'outbound')),
	CONSTRAINT "CK_sync_attempts_status" CHECK ("sync_attempts"."status" IN ('running', 'completed', 'failed'))
);
--> statement-breakpoint
CREATE TABLE IF NOT EXISTS "sync_cursors" (
	"id" uuid PRIMARY KEY DEFAULT gen_random_uuid() NOT NULL,
	"node_id" uuid NOT NULL,
	"cursor_key" text NOT NULL,
	"cursor_value" text NOT NULL,
	"updated_at" timestamp with time zone DEFAULT now() NOT NULL
);
--> statement-breakpoint
CREATE TABLE IF NOT EXISTS "sync_inbox" (
	"id" uuid PRIMARY KEY DEFAULT gen_random_uuid() NOT NULL,
	"source_node_id" uuid,
	"event_type" text NOT NULL,
	"aggregate_type" text NOT NULL,
	"aggregate_id" text NOT NULL,
	"idempotency_key" text NOT NULL,
	"payload_json" jsonb NOT NULL,
	"status" text DEFAULT 'pending' NOT NULL,
	"created_at" timestamp with time zone DEFAULT now() NOT NULL,
	"processed_at" timestamp with time zone,
	CONSTRAINT "CK_sync_inbox_status" CHECK ("sync_inbox"."status" IN ('pending', 'processing', 'completed', 'failed', 'discarded'))
);
--> statement-breakpoint
CREATE TABLE IF NOT EXISTS "user_roles" (
	"user_id" uuid NOT NULL,
	"role_id" uuid NOT NULL,
	"created_at" timestamp with time zone DEFAULT now() NOT NULL,
	CONSTRAINT "user_roles_user_id_role_id_pk" PRIMARY KEY("user_id","role_id")
);
--> statement-breakpoint
CREATE TABLE IF NOT EXISTS "users" (
	"id" uuid PRIMARY KEY DEFAULT gen_random_uuid() NOT NULL,
	"email" text NOT NULL,
	"password_hash" text NOT NULL,
	"full_name" text NOT NULL,
	"status" text DEFAULT 'active' NOT NULL,
	"last_login_at" timestamp with time zone,
	"deleted_at" timestamp with time zone,
	"created_at" timestamp with time zone DEFAULT now() NOT NULL,
	"updated_at" timestamp with time zone DEFAULT now() NOT NULL,
	CONSTRAINT "CK_users_status" CHECK ("users"."status" IN ('active', 'inactive', 'suspended'))
);
--> statement-breakpoint
DO $$ BEGIN
 ALTER TABLE "answer_keys" ADD CONSTRAINT "answer_keys_exam_block_id_exam_blocks_id_fk" FOREIGN KEY ("exam_block_id") REFERENCES "public"."exam_blocks"("id") ON DELETE cascade ON UPDATE no action;
EXCEPTION
 WHEN duplicate_object THEN null;
END $$;
--> statement-breakpoint
DO $$ BEGIN
 ALTER TABLE "asset_usages" ADD CONSTRAINT "asset_usages_exam_block_id_exam_blocks_id_fk" FOREIGN KEY ("exam_block_id") REFERENCES "public"."exam_blocks"("id") ON DELETE cascade ON UPDATE no action;
EXCEPTION
 WHEN duplicate_object THEN null;
END $$;
--> statement-breakpoint
DO $$ BEGIN
 ALTER TABLE "asset_usages" ADD CONSTRAINT "asset_usages_exam_asset_id_exam_assets_id_fk" FOREIGN KEY ("exam_asset_id") REFERENCES "public"."exam_assets"("id") ON DELETE cascade ON UPDATE no action;
EXCEPTION
 WHEN duplicate_object THEN null;
END $$;
--> statement-breakpoint
DO $$ BEGIN
 ALTER TABLE "audit_logs" ADD CONSTRAINT "audit_logs_actor_id_users_id_fk" FOREIGN KEY ("actor_id") REFERENCES "public"."users"("id") ON DELETE set null ON UPDATE no action;
EXCEPTION
 WHEN duplicate_object THEN null;
END $$;
--> statement-breakpoint
DO $$ BEGIN
 ALTER TABLE "central_delivery_sessions" ADD CONSTRAINT "central_delivery_sessions_school_id_schools_id_fk" FOREIGN KEY ("school_id") REFERENCES "public"."schools"("id") ON DELETE no action ON UPDATE no action;
EXCEPTION
 WHEN duplicate_object THEN null;
END $$;
--> statement-breakpoint
DO $$ BEGIN
 ALTER TABLE "central_delivery_sessions" ADD CONSTRAINT "central_delivery_sessions_exam_version_id_exam_versions_id_fk" FOREIGN KEY ("exam_version_id") REFERENCES "public"."exam_versions"("id") ON DELETE no action ON UPDATE no action;
EXCEPTION
 WHEN duplicate_object THEN null;
END $$;
--> statement-breakpoint
DO $$ BEGIN
 ALTER TABLE "classrooms" ADD CONSTRAINT "classrooms_school_id_schools_id_fk" FOREIGN KEY ("school_id") REFERENCES "public"."schools"("id") ON DELETE cascade ON UPDATE no action;
EXCEPTION
 WHEN duplicate_object THEN null;
END $$;
--> statement-breakpoint
DO $$ BEGIN
 ALTER TABLE "departments" ADD CONSTRAINT "departments_province_id_provinces_id_fk" FOREIGN KEY ("province_id") REFERENCES "public"."provinces"("id") ON DELETE cascade ON UPDATE no action;
EXCEPTION
 WHEN duplicate_object THEN null;
END $$;
--> statement-breakpoint
DO $$ BEGIN
 ALTER TABLE "exam_assets" ADD CONSTRAINT "exam_assets_exam_version_id_exam_versions_id_fk" FOREIGN KEY ("exam_version_id") REFERENCES "public"."exam_versions"("id") ON DELETE cascade ON UPDATE no action;
EXCEPTION
 WHEN duplicate_object THEN null;
END $$;
--> statement-breakpoint
DO $$ BEGIN
 ALTER TABLE "exam_block_options" ADD CONSTRAINT "exam_block_options_exam_block_id_exam_blocks_id_fk" FOREIGN KEY ("exam_block_id") REFERENCES "public"."exam_blocks"("id") ON DELETE cascade ON UPDATE no action;
EXCEPTION
 WHEN duplicate_object THEN null;
END $$;
--> statement-breakpoint
DO $$ BEGIN
 ALTER TABLE "exam_blocks" ADD CONSTRAINT "exam_blocks_exam_version_id_exam_versions_id_fk" FOREIGN KEY ("exam_version_id") REFERENCES "public"."exam_versions"("id") ON DELETE cascade ON UPDATE no action;
EXCEPTION
 WHEN duplicate_object THEN null;
END $$;
--> statement-breakpoint
DO $$ BEGIN
 ALTER TABLE "exam_versions" ADD CONSTRAINT "exam_versions_exam_id_exams_id_fk" FOREIGN KEY ("exam_id") REFERENCES "public"."exams"("id") ON DELETE cascade ON UPDATE no action;
EXCEPTION
 WHEN duplicate_object THEN null;
END $$;
--> statement-breakpoint
DO $$ BEGIN
 ALTER TABLE "exam_versions" ADD CONSTRAINT "exam_versions_created_by_users_id_fk" FOREIGN KEY ("created_by") REFERENCES "public"."users"("id") ON DELETE no action ON UPDATE no action;
EXCEPTION
 WHEN duplicate_object THEN null;
END $$;
--> statement-breakpoint
DO $$ BEGIN
 ALTER TABLE "exam_versions" ADD CONSTRAINT "exam_versions_reviewed_by_users_id_fk" FOREIGN KEY ("reviewed_by") REFERENCES "public"."users"("id") ON DELETE no action ON UPDATE no action;
EXCEPTION
 WHEN duplicate_object THEN null;
END $$;
--> statement-breakpoint
DO $$ BEGIN
 ALTER TABLE "exam_versions" ADD CONSTRAINT "exam_versions_approved_by_users_id_fk" FOREIGN KEY ("approved_by") REFERENCES "public"."users"("id") ON DELETE no action ON UPDATE no action;
EXCEPTION
 WHEN duplicate_object THEN null;
END $$;
--> statement-breakpoint
DO $$ BEGIN
 ALTER TABLE "exam_versions" ADD CONSTRAINT "exam_versions_published_by_users_id_fk" FOREIGN KEY ("published_by") REFERENCES "public"."users"("id") ON DELETE no action ON UPDATE no action;
EXCEPTION
 WHEN duplicate_object THEN null;
END $$;
--> statement-breakpoint
DO $$ BEGIN
 ALTER TABLE "localities" ADD CONSTRAINT "localities_department_id_departments_id_fk" FOREIGN KEY ("department_id") REFERENCES "public"."departments"("id") ON DELETE cascade ON UPDATE no action;
EXCEPTION
 WHEN duplicate_object THEN null;
END $$;
--> statement-breakpoint
DO $$ BEGIN
 ALTER TABLE "publication_packages" ADD CONSTRAINT "publication_packages_exam_version_id_exam_versions_id_fk" FOREIGN KEY ("exam_version_id") REFERENCES "public"."exam_versions"("id") ON DELETE cascade ON UPDATE no action;
EXCEPTION
 WHEN duplicate_object THEN null;
END $$;
--> statement-breakpoint
DO $$ BEGIN
 ALTER TABLE "publication_targets" ADD CONSTRAINT "publication_targets_publication_package_id_publication_packages_id_fk" FOREIGN KEY ("publication_package_id") REFERENCES "public"."publication_packages"("id") ON DELETE cascade ON UPDATE no action;
EXCEPTION
 WHEN duplicate_object THEN null;
END $$;
--> statement-breakpoint
DO $$ BEGIN
 ALTER TABLE "received_student_attempts" ADD CONSTRAINT "received_student_attempts_delivery_session_id_central_delivery_sessions_id_fk" FOREIGN KEY ("delivery_session_id") REFERENCES "public"."central_delivery_sessions"("id") ON DELETE set null ON UPDATE no action;
EXCEPTION
 WHEN duplicate_object THEN null;
END $$;
--> statement-breakpoint
DO $$ BEGIN
 ALTER TABLE "received_submission_answers" ADD CONSTRAINT "received_submission_answers_student_attempt_id_received_student_attempts_id_fk" FOREIGN KEY ("student_attempt_id") REFERENCES "public"."received_student_attempts"("id") ON DELETE cascade ON UPDATE no action;
EXCEPTION
 WHEN duplicate_object THEN null;
END $$;
--> statement-breakpoint
DO $$ BEGIN
 ALTER TABLE "received_submission_answers" ADD CONSTRAINT "received_submission_answers_block_id_exam_blocks_id_fk" FOREIGN KEY ("block_id") REFERENCES "public"."exam_blocks"("id") ON DELETE no action ON UPDATE no action;
EXCEPTION
 WHEN duplicate_object THEN null;
END $$;
--> statement-breakpoint
DO $$ BEGIN
 ALTER TABLE "registered_nodes" ADD CONSTRAINT "registered_nodes_school_id_schools_id_fk" FOREIGN KEY ("school_id") REFERENCES "public"."schools"("id") ON DELETE no action ON UPDATE no action;
EXCEPTION
 WHEN duplicate_object THEN null;
END $$;
--> statement-breakpoint
DO $$ BEGIN
 ALTER TABLE "schools" ADD CONSTRAINT "schools_locality_id_localities_id_fk" FOREIGN KEY ("locality_id") REFERENCES "public"."localities"("id") ON DELETE cascade ON UPDATE no action;
EXCEPTION
 WHEN duplicate_object THEN null;
END $$;
--> statement-breakpoint
DO $$ BEGIN
 ALTER TABLE "settings" ADD CONSTRAINT "settings_updated_by_users_id_fk" FOREIGN KEY ("updated_by") REFERENCES "public"."users"("id") ON DELETE set null ON UPDATE no action;
EXCEPTION
 WHEN duplicate_object THEN null;
END $$;
--> statement-breakpoint
DO $$ BEGIN
 ALTER TABLE "sync_attempts" ADD CONSTRAINT "sync_attempts_node_id_registered_nodes_id_fk" FOREIGN KEY ("node_id") REFERENCES "public"."registered_nodes"("id") ON DELETE no action ON UPDATE no action;
EXCEPTION
 WHEN duplicate_object THEN null;
END $$;
--> statement-breakpoint
DO $$ BEGIN
 ALTER TABLE "sync_cursors" ADD CONSTRAINT "sync_cursors_node_id_registered_nodes_id_fk" FOREIGN KEY ("node_id") REFERENCES "public"."registered_nodes"("id") ON DELETE cascade ON UPDATE no action;
EXCEPTION
 WHEN duplicate_object THEN null;
END $$;
--> statement-breakpoint
DO $$ BEGIN
 ALTER TABLE "sync_inbox" ADD CONSTRAINT "sync_inbox_source_node_id_registered_nodes_id_fk" FOREIGN KEY ("source_node_id") REFERENCES "public"."registered_nodes"("id") ON DELETE set null ON UPDATE no action;
EXCEPTION
 WHEN duplicate_object THEN null;
END $$;
--> statement-breakpoint
DO $$ BEGIN
 ALTER TABLE "user_roles" ADD CONSTRAINT "user_roles_user_id_users_id_fk" FOREIGN KEY ("user_id") REFERENCES "public"."users"("id") ON DELETE cascade ON UPDATE no action;
EXCEPTION
 WHEN duplicate_object THEN null;
END $$;
--> statement-breakpoint
DO $$ BEGIN
 ALTER TABLE "user_roles" ADD CONSTRAINT "user_roles_role_id_roles_id_fk" FOREIGN KEY ("role_id") REFERENCES "public"."roles"("id") ON DELETE cascade ON UPDATE no action;
EXCEPTION
 WHEN duplicate_object THEN null;
END $$;
--> statement-breakpoint
CREATE INDEX IF NOT EXISTS "idx_answer_keys_exam_block_id" ON "answer_keys" USING btree ("exam_block_id");--> statement-breakpoint
CREATE INDEX IF NOT EXISTS "idx_asset_usages_exam_block_id" ON "asset_usages" USING btree ("exam_block_id");--> statement-breakpoint
CREATE INDEX IF NOT EXISTS "idx_asset_usages_exam_asset_id" ON "asset_usages" USING btree ("exam_asset_id");--> statement-breakpoint
CREATE INDEX IF NOT EXISTS "idx_audit_logs_entity" ON "audit_logs" USING btree ("entity_type","entity_id","created_at");--> statement-breakpoint
CREATE INDEX IF NOT EXISTS "idx_audit_logs_created_at_brin" ON "audit_logs" USING brin ("created_at");--> statement-breakpoint
CREATE INDEX IF NOT EXISTS "idx_central_delivery_school_id" ON "central_delivery_sessions" USING btree ("school_id");--> statement-breakpoint
CREATE INDEX IF NOT EXISTS "idx_central_delivery_exam_version_id" ON "central_delivery_sessions" USING btree ("exam_version_id");--> statement-breakpoint
CREATE INDEX IF NOT EXISTS "idx_central_delivery_school_active" ON "central_delivery_sessions" USING btree ("school_id","status") WHERE "central_delivery_sessions"."status" = 'active';--> statement-breakpoint
CREATE INDEX IF NOT EXISTS "idx_classrooms_school_active" ON "classrooms" USING btree ("school_id") WHERE "classrooms"."deleted_at" IS NULL;--> statement-breakpoint
CREATE UNIQUE INDEX IF NOT EXISTS "UQ_departments_province_code" ON "departments" USING btree ("province_id","code");--> statement-breakpoint
CREATE INDEX IF NOT EXISTS "idx_exam_assets_exam_version_id" ON "exam_assets" USING btree ("exam_version_id");--> statement-breakpoint
CREATE UNIQUE INDEX IF NOT EXISTS "UQ_exam_assets_version_checksum" ON "exam_assets" USING btree ("exam_version_id","checksum");--> statement-breakpoint
CREATE INDEX IF NOT EXISTS "idx_exam_block_options_block_id" ON "exam_block_options" USING btree ("exam_block_id");--> statement-breakpoint
CREATE INDEX IF NOT EXISTS "idx_exam_blocks_exam_version_id" ON "exam_blocks" USING btree ("exam_version_id");--> statement-breakpoint
CREATE INDEX IF NOT EXISTS "idx_exam_blocks_config_json" ON "exam_blocks" USING gin ("config_json");--> statement-breakpoint
CREATE UNIQUE INDEX IF NOT EXISTS "UQ_exam_versions_exam_version" ON "exam_versions" USING btree ("exam_id","version_number");--> statement-breakpoint
CREATE INDEX IF NOT EXISTS "idx_exam_versions_exam_id" ON "exam_versions" USING btree ("exam_id");--> statement-breakpoint
CREATE UNIQUE INDEX IF NOT EXISTS "UQ_exams_code" ON "exams" USING btree ("code");--> statement-breakpoint
CREATE INDEX IF NOT EXISTS "idx_exams_status_active" ON "exams" USING btree ("status") WHERE "exams"."deleted_at" IS NULL AND "exams"."status" = 'active';--> statement-breakpoint
CREATE UNIQUE INDEX IF NOT EXISTS "UQ_localities_department_code" ON "localities" USING btree ("department_id","code");--> statement-breakpoint
CREATE UNIQUE INDEX IF NOT EXISTS "UQ_provinces_code" ON "provinces" USING btree ("code");--> statement-breakpoint
CREATE INDEX IF NOT EXISTS "idx_publication_packages_exam_version_id" ON "publication_packages" USING btree ("exam_version_id");--> statement-breakpoint
CREATE UNIQUE INDEX IF NOT EXISTS "UQ_publication_packages_version" ON "publication_packages" USING btree ("exam_version_id","package_version");--> statement-breakpoint
CREATE INDEX IF NOT EXISTS "idx_publication_packages_published" ON "publication_packages" USING btree ("exam_version_id","status") WHERE "publication_packages"."status" = 'published';--> statement-breakpoint
CREATE INDEX IF NOT EXISTS "idx_publication_targets_package_id" ON "publication_targets" USING btree ("publication_package_id");--> statement-breakpoint
CREATE UNIQUE INDEX IF NOT EXISTS "UQ_received_student_attempts_idempotency" ON "received_student_attempts" USING btree ("idempotency_key");--> statement-breakpoint
CREATE INDEX IF NOT EXISTS "idx_received_attempts_delivery_id" ON "received_student_attempts" USING btree ("delivery_session_id");--> statement-breakpoint
CREATE INDEX IF NOT EXISTS "idx_received_attempts_student_code" ON "received_student_attempts" USING btree ("student_code");--> statement-breakpoint
CREATE INDEX IF NOT EXISTS "idx_received_answers_attempt_id" ON "received_submission_answers" USING btree ("student_attempt_id");--> statement-breakpoint
CREATE INDEX IF NOT EXISTS "idx_received_answers_block_id" ON "received_submission_answers" USING btree ("block_id");--> statement-breakpoint
CREATE UNIQUE INDEX IF NOT EXISTS "UQ_registered_nodes_node_code" ON "registered_nodes" USING btree ("node_code");--> statement-breakpoint
CREATE INDEX IF NOT EXISTS "idx_registered_nodes_school_id" ON "registered_nodes" USING btree ("school_id");--> statement-breakpoint
CREATE UNIQUE INDEX IF NOT EXISTS "UQ_roles_code" ON "roles" USING btree ("code");--> statement-breakpoint
CREATE UNIQUE INDEX IF NOT EXISTS "UQ_schools_locality_code" ON "schools" USING btree ("locality_id","code");--> statement-breakpoint
CREATE INDEX IF NOT EXISTS "idx_schools_status_active" ON "schools" USING btree ("status") WHERE "schools"."deleted_at" IS NULL AND "schools"."status" = 'active';--> statement-breakpoint
CREATE UNIQUE INDEX IF NOT EXISTS "UQ_settings_key" ON "settings" USING btree ("key");--> statement-breakpoint
CREATE INDEX IF NOT EXISTS "idx_sync_attempts_node_id" ON "sync_attempts" USING btree ("node_id");--> statement-breakpoint
CREATE UNIQUE INDEX IF NOT EXISTS "UQ_sync_cursors_node_cursor" ON "sync_cursors" USING btree ("node_id","cursor_key");--> statement-breakpoint
CREATE INDEX IF NOT EXISTS "idx_sync_cursors_node_id" ON "sync_cursors" USING btree ("node_id");--> statement-breakpoint
CREATE UNIQUE INDEX IF NOT EXISTS "UQ_sync_inbox_idempotency" ON "sync_inbox" USING btree ("idempotency_key");--> statement-breakpoint
CREATE INDEX IF NOT EXISTS "idx_sync_inbox_source_node_id" ON "sync_inbox" USING btree ("source_node_id");--> statement-breakpoint
CREATE INDEX IF NOT EXISTS "idx_sync_inbox_pending" ON "sync_inbox" USING btree ("status","created_at") WHERE "sync_inbox"."status" = 'pending';--> statement-breakpoint
CREATE INDEX IF NOT EXISTS "idx_sync_inbox_aggregate" ON "sync_inbox" USING btree ("aggregate_type","aggregate_id");--> statement-breakpoint
CREATE UNIQUE INDEX IF NOT EXISTS "UQ_users_email" ON "users" USING btree ("email");--> statement-breakpoint
CREATE INDEX IF NOT EXISTS "idx_users_email_active" ON "users" USING btree ("email") WHERE "users"."deleted_at" IS NULL AND "users"."status" = 'active';