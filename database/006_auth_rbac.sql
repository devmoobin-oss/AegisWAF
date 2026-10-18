-- v0.7: users, roles, sessions and audit actor attribution.
CREATE TABLE IF NOT EXISTS roles (
 id smallint PRIMARY KEY,
 name text NOT NULL UNIQUE,
 level smallint NOT NULL UNIQUE,
 description text NOT NULL DEFAULT ''
);
INSERT INTO roles(id,name,level,description) VALUES
 (1,'VIEWER',1,'Read-only dashboard access'),
 (2,'SECURITY_ANALYST',2,'Investigate events, use rule sandbox, read audit logs'),
 (3,'SECURITY_OPERATOR',3,'Manage IP access policies'),
 (4,'SECURITY_ADMIN',4,'Manage WAF rules and policies'),
 (5,'SUPER_ADMIN',5,'Full control including user management')
ON CONFLICT (id) DO NOTHING;

CREATE TABLE IF NOT EXISTS users (
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
 username text NOT NULL UNIQUE CHECK (username ~ '^[a-z0-9._-]{3,64}$'),
 display_name text NOT NULL DEFAULT '' CHECK (length(display_name) <= 120),
 password_hash text NOT NULL,
 enabled boolean NOT NULL DEFAULT true,
 failed_attempts integer NOT NULL DEFAULT 0,
 locked_until timestamptz,
 must_change_password boolean NOT NULL DEFAULT false,
 mfa_secret text, -- reserved for TOTP enrolment (MFA-ready); not used yet
 created_at timestamptz NOT NULL DEFAULT now(),
 updated_at timestamptz NOT NULL DEFAULT now(),
 last_login_at timestamptz
);

CREATE TABLE IF NOT EXISTS user_roles (
 user_id uuid NOT NULL REFERENCES users(id) ON DELETE CASCADE,
 role_id smallint NOT NULL REFERENCES roles(id),
 PRIMARY KEY (user_id, role_id)
);

CREATE TABLE IF NOT EXISTS sessions (
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
 user_id uuid NOT NULL REFERENCES users(id) ON DELETE CASCADE,
 token_hash bytea NOT NULL UNIQUE, -- SHA-256 of the cookie token; the raw token is never stored
 csrf_token text NOT NULL,
 created_at timestamptz NOT NULL DEFAULT now(),
 last_seen_at timestamptz NOT NULL DEFAULT now(),
 expires_at timestamptz NOT NULL,
 revoked_at timestamptz,
 client_ip text NOT NULL DEFAULT '',
 user_agent text NOT NULL DEFAULT ''
);
CREATE INDEX IF NOT EXISTS ix_sessions_user ON sessions(user_id) WHERE revoked_at IS NULL;

ALTER TABLE waf_audit ADD COLUMN IF NOT EXISTS actor text;
ALTER TABLE waf_audit ADD COLUMN IF NOT EXISTS client_ip text;
ALTER TABLE waf_audit ADD COLUMN IF NOT EXISTS outcome text NOT NULL DEFAULT 'success';
