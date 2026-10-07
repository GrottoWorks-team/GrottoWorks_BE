-- GrottoWorks: create one database per service on first container start.
-- Scripts in /docker-entrypoint-initdb.d run only when the data volume is empty.
-- Roles use LOGIN (not SUPERUSER) so services do not run as cluster superuser.
DO $$
BEGIN
    IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'grottoworks_app') THEN
        CREATE ROLE grottoworks_app LOGIN PASSWORD 'grottoworks_app';
    END IF;
END
$$;

SELECT 'CREATE DATABASE identity_db OWNER grottoworks_app'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'identity_db')\gexec

SELECT 'CREATE DATABASE parish_db OWNER grottoworks_app'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'parish_db')\gexec

SELECT 'CREATE DATABASE task_db OWNER grottoworks_app'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'task_db')\gexec

SELECT 'CREATE DATABASE resource_db OWNER grottoworks_app'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'resource_db')\gexec

SELECT 'CREATE DATABASE readiness_db OWNER grottoworks_app'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'readiness_db')\gexec

SELECT 'CREATE DATABASE notify_db OWNER grottoworks_app'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'notify_db')\gexec

SELECT 'CREATE DATABASE reporting_db OWNER grottoworks_app'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'reporting_db')\gexec

GRANT ALL PRIVILEGES ON DATABASE identity_db, parish_db, task_db, resource_db,
    readiness_db, notify_db, reporting_db TO grottoworks_app;
