create extension if not exists pgcrypto;

create table if not exists users (
    id uuid primary key default gen_random_uuid(),
    email text not null unique,
    display_name text not null,
    role text not null check (role in (
        'employee',
        'unitLead',
        'divisionalHead',
        'director',
        'secretariat',
        'deputyDirectorGeneral',
        'systemAdmin'
    )),
    division text,
    unit text,
    password_hash bytea not null,
    password_salt bytea not null,
    password_iterations integer not null,
    is_active boolean not null default true,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now()
);

create table if not exists dashboards (
    user_id uuid primary key references users(id) on delete cascade,
    data jsonb not null default '{"objectivesData":[]}'::jsonb,
    updated_at timestamptz not null default now()
);

create table if not exists units (
    id uuid primary key default gen_random_uuid(),
    name text not null,
    division text not null,
    active boolean not null default true,
    created_by uuid references users(id),
    created_at timestamptz not null default now(),
    unique (division, name)
);

create table if not exists attachments (
    id uuid primary key default gen_random_uuid(),
    owner_user_id uuid not null references users(id) on delete cascade,
    objective_id text not null,
    task_type text not null check (task_type in ('action', 'kpi')),
    task_index integer not null,
    original_file_name text not null,
    content_type text not null,
    size_bytes bigint not null check (size_bytes > 0 and size_bytes <= 10485760),
    storage_path text not null,
    created_by uuid references users(id),
    created_at timestamptz not null default now()
);

create table if not exists audit_logs (
    id bigserial primary key,
    user_id uuid references users(id),
    action text not null,
    entity_type text not null,
    entity_id text not null,
    ip_address text,
    created_at timestamptz not null default now()
);

create index if not exists ix_users_division_unit on users (division, unit);
create index if not exists ix_users_role on users (role);
create index if not exists ix_dashboards_updated_at on dashboards (updated_at desc);
create index if not exists ix_attachments_owner on attachments (owner_user_id);
create index if not exists ix_audit_logs_created_at on audit_logs (created_at desc);
