-- v1.3: persist bounded active-probe observations for incident review across restart.
CREATE TABLE IF NOT EXISTS waf_site_health_observations (
    observation_id bigserial PRIMARY KEY,
    site_id varchar(64) NOT NULL REFERENCES waf_sites(site_id) ON DELETE CASCADE,
    upstream_id varchar(64) NOT NULL,
    health varchar(16) NOT NULL CHECK (health IN ('healthy', 'unhealthy', 'unknown')),
    reason text,
    checked_at timestamptz NOT NULL,
    CHECK (reason IS NULL OR length(reason) <= 128)
);

CREATE INDEX IF NOT EXISTS ix_waf_site_health_observations_site_time
    ON waf_site_health_observations(site_id, checked_at DESC, observation_id DESC);
