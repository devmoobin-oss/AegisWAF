ALTER TABLE security_events ADD COLUMN IF NOT EXISTS site_id text;
ALTER TABLE security_events ADD COLUMN IF NOT EXISTS hostname text;
ALTER TABLE security_events ADD COLUMN IF NOT EXISTS upstream_id text;
CREATE INDEX IF NOT EXISTS ix_security_events_site_time ON security_events(site_id, occurred_at DESC);
