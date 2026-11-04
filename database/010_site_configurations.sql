-- v0.8: PostgreSQL-backed multi-site gateway configuration.
CREATE TABLE IF NOT EXISTS waf_sites (
    site_id varchar(64) PRIMARY KEY CHECK (site_id ~ '^[a-z0-9][a-z0-9_-]{0,63}$'),
    enabled boolean NOT NULL DEFAULT true,
    hostnames text[] NOT NULL,
    path_prefixes text[] NOT NULL,
    upstream_ids text[] NOT NULL,
    upstream_addresses text[] NOT NULL,
    upstream_weights integer[] NOT NULL,
    waf_policy varchar(120) NOT NULL DEFAULT 'default',
    egress_allowed_hosts text[] NOT NULL,
    egress_allowed_ports integer[] NOT NULL,
    egress_allowed_schemes text[] NOT NULL,
    updated_at timestamptz NOT NULL DEFAULT now(),
    updated_by text
);
CREATE INDEX IF NOT EXISTS ix_waf_sites_enabled ON waf_sites(enabled);
