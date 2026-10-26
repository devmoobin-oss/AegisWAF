-- v1.4: bounded, auditable acknowledgement state for actionable health alerts.
CREATE TABLE IF NOT EXISTS waf_site_health_alerts (
    site_id varchar(64) NOT NULL REFERENCES waf_sites(site_id) ON DELETE CASCADE,
    alert_state varchar(16) NOT NULL CHECK (alert_state IN ('degraded', 'critical')),
    acknowledged_at timestamptz NOT NULL,
    acknowledged_until timestamptz NOT NULL,
    acknowledged_by varchar(64) NOT NULL,
    updated_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (site_id, alert_state),
    CHECK (acknowledged_until > acknowledged_at)
);

CREATE INDEX IF NOT EXISTS ix_waf_site_health_alerts_until
    ON waf_site_health_alerts(acknowledged_until DESC);
