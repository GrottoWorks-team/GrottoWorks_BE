-- Run explicitly as the PostgreSQL administrator AFTER all service migrations.
-- Passwords come from this administrator process environment, never command-line arguments.
-- Do not enable query echo or put passwords in committed files.
\set ON_ERROR_STOP on
\getenv backup_password BACKUP_DB_PASSWORD
\getenv job_password BACKUP_JOB_DB_PASSWORD
SELECT 'CREATE ROLE grottoworks_backup LOGIN' WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname='grottoworks_backup') \gexec
SELECT 'CREATE ROLE grottoworks_backup_jobs LOGIN' WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname='grottoworks_backup_jobs') \gexec
ALTER ROLE grottoworks_backup PASSWORD :'backup_password';
ALTER ROLE grottoworks_backup_jobs PASSWORD :'job_password';
ALTER ROLE grottoworks_backup SET default_transaction_read_only=on;
\connect identity_db
GRANT CONNECT ON DATABASE identity_db TO grottoworks_backup;
GRANT USAGE ON SCHEMA public TO grottoworks_backup;
GRANT SELECT ON ALL TABLES IN SCHEMA public TO grottoworks_backup;
GRANT SELECT ON ALL SEQUENCES IN SCHEMA public TO grottoworks_backup;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT ON TABLES TO grottoworks_backup;
\connect parish_db
GRANT CONNECT ON DATABASE parish_db TO grottoworks_backup;
GRANT USAGE ON SCHEMA public TO grottoworks_backup;
GRANT SELECT ON ALL TABLES IN SCHEMA public TO grottoworks_backup;
GRANT SELECT ON ALL SEQUENCES IN SCHEMA public TO grottoworks_backup;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT ON TABLES TO grottoworks_backup;
\connect task_db
GRANT CONNECT ON DATABASE task_db TO grottoworks_backup;
GRANT USAGE ON SCHEMA public TO grottoworks_backup;
GRANT SELECT ON ALL TABLES IN SCHEMA public TO grottoworks_backup;
GRANT SELECT ON ALL SEQUENCES IN SCHEMA public TO grottoworks_backup;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT ON TABLES TO grottoworks_backup;
\connect resource_db
GRANT CONNECT ON DATABASE resource_db TO grottoworks_backup;
GRANT USAGE ON SCHEMA public TO grottoworks_backup;
GRANT SELECT ON ALL TABLES IN SCHEMA public TO grottoworks_backup;
GRANT SELECT ON ALL SEQUENCES IN SCHEMA public TO grottoworks_backup;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT ON TABLES TO grottoworks_backup;
\connect readiness_db
GRANT CONNECT ON DATABASE readiness_db TO grottoworks_backup;
GRANT USAGE ON SCHEMA public TO grottoworks_backup;
GRANT SELECT ON ALL TABLES IN SCHEMA public TO grottoworks_backup;
GRANT SELECT ON ALL SEQUENCES IN SCHEMA public TO grottoworks_backup;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT ON TABLES TO grottoworks_backup;
\connect reporting_db
GRANT CONNECT ON DATABASE reporting_db TO grottoworks_backup;
GRANT USAGE ON SCHEMA public TO grottoworks_backup;
GRANT SELECT ON ALL TABLES IN SCHEMA public TO grottoworks_backup;
GRANT SELECT ON ALL SEQUENCES IN SCHEMA public TO grottoworks_backup;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT ON TABLES TO grottoworks_backup;
\connect notify_db
GRANT CONNECT ON DATABASE notify_db TO grottoworks_backup,grottoworks_backup_jobs;
GRANT USAGE ON SCHEMA public TO grottoworks_backup,grottoworks_backup_jobs;
GRANT SELECT ON ALL TABLES IN SCHEMA public TO grottoworks_backup;
GRANT SELECT ON ALL SEQUENCES IN SCHEMA public TO grottoworks_backup;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT ON TABLES TO grottoworks_backup;
GRANT SELECT,INSERT,UPDATE ON system_job TO grottoworks_backup_jobs;
GRANT SELECT ON file_object TO grottoworks_backup_jobs;
GRANT INSERT ON file_object,notification,activity_log TO grottoworks_backup_jobs;
