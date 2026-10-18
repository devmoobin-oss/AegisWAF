CREATE TABLE IF NOT EXISTS waf_ip_policies (
 ip text PRIMARY KEY,
 action text NOT NULL CHECK(action IN ('allow','block')),
 reason text NOT NULL DEFAULT '',
 updated_at timestamptz NOT NULL DEFAULT now()
);
CREATE TABLE IF NOT EXISTS waf_audit (
 id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
 occurred_at timestamptz NOT NULL DEFAULT now(),
 action text NOT NULL,
 target text NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_waf_audit_time ON waf_audit(occurred_at DESC);
