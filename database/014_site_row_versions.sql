-- v1.2: monotonic row versions for optimistic concurrency on Site edits.
ALTER TABLE waf_sites
    ADD COLUMN IF NOT EXISTS row_version bigint NOT NULL DEFAULT 1;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_waf_sites_row_version') THEN
        ALTER TABLE waf_sites ADD CONSTRAINT ck_waf_sites_row_version CHECK (row_version >= 1);
    END IF;
END $$;
