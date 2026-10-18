ALTER TABLE security_events ADD COLUMN IF NOT EXISTS http_method text NOT NULL DEFAULT 'GET';
CREATE INDEX IF NOT EXISTS ix_security_events_ip_time ON security_events(client_ip, occurred_at DESC);
