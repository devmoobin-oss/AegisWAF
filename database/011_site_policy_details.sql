-- v0.9: preserve per-site health and rate-limit policies across restart.
ALTER TABLE waf_sites ADD COLUMN IF NOT EXISTS health_path text NOT NULL DEFAULT '/';
ALTER TABLE waf_sites ADD COLUMN IF NOT EXISTS health_interval_seconds integer NOT NULL DEFAULT 10;
ALTER TABLE waf_sites ADD COLUMN IF NOT EXISTS health_timeout_seconds integer NOT NULL DEFAULT 3;
ALTER TABLE waf_sites ADD COLUMN IF NOT EXISTS health_passive_reactivation_seconds integer NOT NULL DEFAULT 30;
ALTER TABLE waf_sites ADD COLUMN IF NOT EXISTS rate_requests integer NOT NULL DEFAULT 300;
ALTER TABLE waf_sites ADD COLUMN IF NOT EXISTS rate_window_seconds integer NOT NULL DEFAULT 60;
ALTER TABLE waf_sites ADD COLUMN IF NOT EXISTS rate_global_requests integer NOT NULL DEFAULT 10000;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_waf_sites_health_path') THEN
        ALTER TABLE waf_sites ADD CONSTRAINT ck_waf_sites_health_path
            CHECK (health_path LIKE '/%' AND health_path NOT LIKE '%?%' AND health_path NOT LIKE '%#%');
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_waf_sites_health_bounds') THEN
        ALTER TABLE waf_sites ADD CONSTRAINT ck_waf_sites_health_bounds
            CHECK (health_interval_seconds BETWEEN 5 AND 300
               AND health_timeout_seconds BETWEEN 1 AND 30
               AND health_timeout_seconds < health_interval_seconds
               AND health_passive_reactivation_seconds BETWEEN 5 AND 3600);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_waf_sites_rate_bounds') THEN
        ALTER TABLE waf_sites ADD CONSTRAINT ck_waf_sites_rate_bounds
            CHECK (rate_requests BETWEEN 1 AND 1000000
               AND rate_window_seconds BETWEEN 1 AND 3600
               AND rate_global_requests BETWEEN 1 AND 10000000);
    END IF;
END $$;
