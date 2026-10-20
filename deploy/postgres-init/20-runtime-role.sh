#!/bin/sh
set -eu
DB_RUNTIME_PASSWORD="$(cat /run/secrets/db_runtime_password)"
export DB_RUNTIME_PASSWORD
psql --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" --set=ON_ERROR_STOP=1 <<'SQL'
\getenv runtime_password DB_RUNTIME_PASSWORD
SELECT format('CREATE ROLE omnidesk_runtime LOGIN PASSWORD %L', :'runtime_password') \gexec
GRANT CONNECT ON DATABASE omnidesk TO omnidesk_runtime;
GRANT USAGE ON SCHEMA public TO omnidesk_runtime;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO omnidesk_runtime;
GRANT USAGE, SELECT, UPDATE ON ALL SEQUENCES IN SCHEMA public TO omnidesk_runtime;
ALTER DEFAULT PRIVILEGES FOR ROLE omnidesk IN SCHEMA public
  GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO omnidesk_runtime;
ALTER DEFAULT PRIVILEGES FOR ROLE omnidesk IN SCHEMA public
  GRANT USAGE, SELECT, UPDATE ON SEQUENCES TO omnidesk_runtime;
SQL

