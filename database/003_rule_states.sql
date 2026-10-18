CREATE TABLE IF NOT EXISTS waf_rule_states (
 rule_id text PRIMARY KEY,
 enabled boolean NOT NULL,
 updated_at timestamptz NOT NULL DEFAULT now()
);
