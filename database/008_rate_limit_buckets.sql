CREATE TABLE IF NOT EXISTS waf_rate_limit_buckets (
    bucket_key text PRIMARY KEY,
    window_started timestamptz NOT NULL,
    request_count integer NOT NULL CHECK (request_count >= 0),
    updated_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ix_waf_rate_limit_buckets_updated_at ON waf_rate_limit_buckets(updated_at);
