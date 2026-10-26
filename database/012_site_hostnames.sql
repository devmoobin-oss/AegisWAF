-- v1.0: normalized active hostname ownership for race-safe multi-site routing.
CREATE TABLE IF NOT EXISTS waf_site_hostnames (
    site_id varchar(64) NOT NULL REFERENCES waf_sites(site_id) ON DELETE CASCADE,
    hostname text NOT NULL,
    active boolean NOT NULL DEFAULT true,
    PRIMARY KEY (site_id, hostname)
);

INSERT INTO waf_site_hostnames(site_id, hostname, active)
SELECT s.site_id, lower(trim(trailing '.' from h.hostname)), s.enabled
FROM waf_sites s
CROSS JOIN LATERAL unnest(s.hostnames) AS h(hostname)
WHERE trim(h.hostname) <> ''
ON CONFLICT (site_id, hostname) DO UPDATE SET active = EXCLUDED.active;

CREATE UNIQUE INDEX IF NOT EXISTS ux_waf_site_hostnames_active_hostname
    ON waf_site_hostnames (lower(hostname))
    WHERE active;

CREATE INDEX IF NOT EXISTS ix_waf_site_hostnames_site_id
    ON waf_site_hostnames(site_id);
