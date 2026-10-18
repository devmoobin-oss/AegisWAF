CREATE TABLE IF NOT EXISTS waf_custom_rules (
 id varchar(64) PRIMARY KEY,
 name varchar(120) NOT NULL,
 pattern varchar(512) NOT NULL,
 priority integer NOT NULL CHECK (priority BETWEEN 1 AND 10000),
 enabled boolean NOT NULL DEFAULT true,
 updated_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ix_waf_custom_rules_priority ON waf_custom_rules(priority);
