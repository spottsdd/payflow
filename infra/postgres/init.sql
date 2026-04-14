-- payflow database schema + seed data

CREATE EXTENSION IF NOT EXISTS "pgcrypto";

-- Owned by payment-orchestrator
CREATE TABLE IF NOT EXISTS payments (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    from_account_id UUID NOT NULL,
    to_account_id   UUID NOT NULL,
    amount          NUMERIC(12, 2) NOT NULL,
    currency        CHAR(3) NOT NULL,
    status          VARCHAR(20) NOT NULL DEFAULT 'PENDING',
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_payments_from_account ON payments (from_account_id);
CREATE INDEX IF NOT EXISTS idx_payments_status ON payments (status);

-- Owned by settlement-service
CREATE TABLE IF NOT EXISTS accounts (
    id         UUID PRIMARY KEY,
    owner_name VARCHAR(100) NOT NULL,
    balance    NUMERIC(12, 2) NOT NULL DEFAULT 0.00,
    currency   CHAR(3) NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS settlements (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    payment_id      UUID NOT NULL,
    from_account_id UUID NOT NULL,
    to_account_id   UUID NOT NULL,
    amount          NUMERIC(12, 2) NOT NULL,
    settled_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_settlements_payment ON settlements (payment_id);

-- Owned by notification-service
CREATE TABLE IF NOT EXISTS notifications (
    id         UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    payment_id UUID NOT NULL,
    type       VARCHAR(30) NOT NULL,
    payload    JSONB,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_notifications_payment ON notifications (payment_id);

-- Seed accounts: 10 accounts each with $50,000 balance.
-- Payments are revolving transfers between accounts so balances circulate
-- indefinitely at any traffic volume.
INSERT INTO accounts (id, owner_name, balance, currency) VALUES
    ('a1000000-0000-0000-0000-000000000001', 'Alice Hartman',    50000.00, 'USD'),
    ('a1000000-0000-0000-0000-000000000002', 'Bob Nguyen',       50000.00, 'USD'),
    ('a1000000-0000-0000-0000-000000000003', 'Carol Osei',       50000.00, 'USD'),
    ('a1000000-0000-0000-0000-000000000004', 'David Reyes',      50000.00, 'USD'),
    ('a1000000-0000-0000-0000-000000000005', 'Elena Kowalski',   50000.00, 'USD'),
    ('a1000000-0000-0000-0000-000000000006', 'Faisal Al-Amin',   50000.00, 'USD'),
    ('a1000000-0000-0000-0000-000000000007', 'Grace Tanaka',     50000.00, 'USD'),
    ('a1000000-0000-0000-0000-000000000008', 'Hiro Matsuda',     50000.00, 'USD'),
    ('a1000000-0000-0000-0000-000000000009', 'Isla Ferreira',    50000.00, 'USD'),
    ('a1000000-0000-0000-0000-000000000010', 'James Okonkwo',    50000.00, 'USD')
ON CONFLICT (id) DO NOTHING;
