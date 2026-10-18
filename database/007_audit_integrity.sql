-- v0.12: tamper-evident audit chain. Existing rows remain readable; the first new row starts a new chain.
ALTER TABLE waf_audit ADD COLUMN IF NOT EXISTS prev_hash bytea;
ALTER TABLE waf_audit ADD COLUMN IF NOT EXISTS entry_hash bytea;
CREATE INDEX IF NOT EXISTS ix_waf_audit_entry_hash ON waf_audit(entry_hash);
