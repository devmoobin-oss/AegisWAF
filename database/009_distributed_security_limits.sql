-- v0.13: shared authentication abuse counters. Values are bounded and expire by window.
CREATE TABLE IF NOT EXISTS waf_auth_throttle_buckets (
    bucket_key text PRIMARY KEY,
    window_started timestamptz NOT NULL,
    failure_count integer NOT NULL CHECK (failure_count >= 0),
    updated_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ix_waf_auth_throttle_buckets_updated_at ON waf_auth_throttle_buckets(updated_at);
