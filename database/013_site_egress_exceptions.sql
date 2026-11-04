-- v1.1: preserve the explicitly reviewed non-production egress exception across restart.
ALTER TABLE waf_sites
    ADD COLUMN IF NOT EXISTS egress_allow_private_exceptions boolean NOT NULL DEFAULT false;
