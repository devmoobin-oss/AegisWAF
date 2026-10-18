-- Create database separately: CREATE DATABASE aegiswaf;
CREATE TABLE IF NOT EXISTS security_events (
 id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
 occurred_at timestamptz NOT NULL DEFAULT now(),
 client_ip text NOT NULL,
 request_path text NOT NULL,
 rule_name text NOT NULL,
 status_code integer NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_security_events_occurred ON security_events (occurred_at DESC);
CREATE INDEX IF NOT EXISTS ix_security_events_rule ON security_events (rule_name, occurred_at DESC);
