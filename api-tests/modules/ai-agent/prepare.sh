#!/usr/bin/env bash
set -euo pipefail

if ! command -v setsid >/dev/null 2>&1; then
    echo "ERROR: setsid with exclusive session/process-group support is required" >&2
    exit 127
fi

# Reject obsolete task-prefixed configuration rather than silently ignoring it.
for variable in "${!AI@}"; do
    if [[ "$variable" =~ ^AI[0-9]+[A-Z]_(PREPARE_TIMEOUT_SECONDS|PREPARE_KILL_AFTER_SECONDS|PGCONNECT_TIMEOUT_SECONDS|STATEMENT_TIMEOUT_MS|LOCK_TIMEOUT_MS)$ ]]; then
        echo "ERROR: obsolete task-prefixed preparation variables are unsupported; use AI_AGENT_*" >&2
        exit 2
    fi
done

AI_AGENT_PREPARE_TIMEOUT_SECONDS=${AI_AGENT_PREPARE_TIMEOUT_SECONDS-90}
AI_AGENT_PREPARE_KILL_AFTER_SECONDS=${AI_AGENT_PREPARE_KILL_AFTER_SECONDS-5}
AI_AGENT_DB_CONNECT_TIMEOUT_SECONDS=${AI_AGENT_DB_CONNECT_TIMEOUT_SECONDS-15}
AI_AGENT_DB_STATEMENT_TIMEOUT_MILLISECONDS=${AI_AGENT_DB_STATEMENT_TIMEOUT_MILLISECONDS-30000}
AI_AGENT_DB_LOCK_TIMEOUT_MILLISECONDS=${AI_AGENT_DB_LOCK_TIMEOUT_MILLISECONDS-10000}

for variable in AI_AGENT_PREPARE_TIMEOUT_SECONDS AI_AGENT_PREPARE_KILL_AFTER_SECONDS \
    AI_AGENT_DB_CONNECT_TIMEOUT_SECONDS AI_AGENT_DB_STATEMENT_TIMEOUT_MILLISECONDS AI_AGENT_DB_LOCK_TIMEOUT_MILLISECONDS; do
    value=${!variable}
    if [[ ! "$value" =~ ^[0-9]+$ || "$value" =~ ^0+$ ]]; then
        echo "ERROR: $variable must be a positive integer up to 2147483647" >&2
        exit 2
    fi
    value=${value#"${value%%[!0]*}"}
    if [[ ${#value} -gt 10 ]] || (( value > 2147483647 )); then
        echo "ERROR: $variable must be a positive integer up to 2147483647" >&2
        exit 2
    fi
    printf -v "$variable" '%s' "$value"
    export "$variable"
done

prepare() {
# Test-only identity. Never add this fixture to production/module seed permissions.
environment=$(docker compose exec -T validation-app printenv ASPNETCORE_ENVIRONMENT)
[[ "$environment" == Testing ]] || {
    echo "ERROR: write-only fixture requires the Testing Host" >&2
    exit 1
}
docker compose exec -T \
    -e "PGCONNECT_TIMEOUT=$AI_AGENT_DB_CONNECT_TIMEOUT_SECONDS" \
    -e "PGOPTIONS=-c statement_timeout=$AI_AGENT_DB_STATEMENT_TIMEOUT_MILLISECONDS -c lock_timeout=$AI_AGENT_DB_LOCK_TIMEOUT_MILLISECONDS" \
    postgres psql -U alxarafe -d alxarafe_security_test -v ON_ERROR_STOP=1 <<'SQL'
BEGIN;
DO $$
DECLARE
    fixture_id uuid;
BEGIN
    IF current_database() <> 'alxarafe_security_test' THEN
        RAISE EXCEPTION 'Write-only fixture requires the isolated security database';
    END IF;
    SELECT "Id" INTO fixture_id FROM "AspNetUsers" WHERE "NormalizedUserName" = 'AI-WRITE-ONLY@EXAMPLE.TEST';
    IF fixture_id IS NULL THEN
        -- The standard Identity password hash is user-independent. Reuse only the
        -- seeded test credential hash; identity, stamps and claims remain distinct.
        INSERT INTO "AspNetUsers" ("Id", "UserName", "NormalizedUserName", "Email", "NormalizedEmail",
            "EmailConfirmed", "PasswordHash", "SecurityStamp", "ConcurrencyStamp", "PhoneNumberConfirmed",
            "TwoFactorEnabled", "LockoutEnabled", "AccessFailedCount")
        SELECT gen_random_uuid(), 'ai-write-only@example.test', 'AI-WRITE-ONLY@EXAMPLE.TEST',
            'ai-write-only@example.test', 'AI-WRITE-ONLY@EXAMPLE.TEST', true, "PasswordHash",
            gen_random_uuid()::text, gen_random_uuid()::text, false, false, true, 0
        FROM "AspNetUsers" WHERE "NormalizedUserName" = 'AI-WRITER@EXAMPLE.TEST' AND "PasswordHash" IS NOT NULL
        RETURNING "Id" INTO fixture_id;
        IF fixture_id IS NULL THEN RAISE EXCEPTION 'Seeded writer fixture is missing'; END IF;
        INSERT INTO "AspNetUserClaims" ("UserId", "ClaimType", "ClaimValue")
            VALUES (fixture_id, 'permission', 'ai.knowledge.write');
    END IF;
    IF (SELECT count(*) FROM "AspNetUserClaims" WHERE "UserId" = fixture_id) <> 1
        OR NOT EXISTS (SELECT 1 FROM "AspNetUserClaims" WHERE "UserId" = fixture_id
            AND "ClaimType" = 'permission' AND "ClaimValue" = 'ai.knowledge.write')
        OR EXISTS (SELECT 1 FROM "AspNetUserRoles" WHERE "UserId" = fixture_id) THEN
        RAISE EXCEPTION 'Write-only fixture has unexpected claims or roles';
    END IF;
END $$;
COMMIT;
SQL
}
export -f prepare

# This supervisor is local to preparation. Linux /proc supplies an identity that
# remains stable across exec and detects PID reuse; no process-name searches.
valid_pid() {
    [[ "$1" =~ ^[1-9][0-9]{0,9}$ ]] && (( 10#$1 > 1 && 10#$1 <= 2147483647 ))
}
read_identity() {
    local line tail
    local -a fields
    valid_pid "$1" || return 1
    IFS= read -r line 2>/dev/null <"/proc/$1/stat" || return 1
    tail=${line##*) }
    read -r -a fields <<<"$tail"
    [[ ${#fields[@]} -ge 20 && "${fields[19]}" =~ ^[0-9]+$ ]] || return 1
    identity_ppid=${fields[1]}
    identity_pgid=${fields[2]}
    identity_sid=${fields[3]}
    identity_start=${fields[19]}
}
export -f valid_pid read_identity

supervisor_pid=$BASHPID
read_identity "$supervisor_pid" || {
    echo "ERROR: cannot verify the supervisor process identity" >&2
    exit 2
}
supervisor_pgid=$identity_pgid
state=$(mktemp -d)
leader_pid= leader_start= watchdog_pid=
group_verified=false

owned_group() {
    [[ "$group_verified" == true ]] && valid_pid "$leader_pid" &&
        [[ "$leader_pid" != "$supervisor_pid" && "$leader_pid" != "$supervisor_pgid" ]] &&
        read_identity "$leader_pid" &&
        [[ "$identity_ppid" == "$supervisor_pid" &&
            "$identity_pgid" == "$leader_pid" && "$identity_sid" == "$leader_pid" &&
            "$identity_start" == "$leader_start" ]]
}
signal_group() {
    if ! owned_group; then
        echo "ERROR: refusing to signal a process group whose identity cannot be verified" >&2
        return 1
    fi
    kill -s "$1" -- "-$leader_pid"
}
cancel_watchdog() {
    if [[ -n "$watchdog_pid" ]]; then
        printf 'cancel\n' >&"$cancel_fd"
        if wait "$watchdog_pid"; then :; else
            echo "ERROR: preparation watchdog failed" >&2
            watchdog_pid=
            return 1
        fi
        watchdog_pid=
    fi
}
cleanup() {
    # Keep the first exit status; later signals cannot reenter or abort cleanup.
    trap '' INT TERM
    trap - EXIT
    local result=$1 cleanup_failed=false
    if ! cancel_watchdog; then cleanup_failed=true; fi
    if [[ -n "$leader_pid" ]]; then
        if [[ "$group_verified" == true ]]; then
            if owned_group; then
                if ! signal_group TERM; then cleanup_failed=true; fi
                # The private FIFO has no writer: read provides the grace timer.
                if read -r -t "$AI_AGENT_PREPARE_KILL_AFTER_SECONDS" <&"$timer_fd"; then :; fi
                if ! signal_group KILL; then cleanup_failed=true; fi
            elif [[ -e "/proc/$leader_pid/stat" ]]; then
                echo "ERROR: process identity changed; refusing group cleanup" >&2
                cleanup_failed=true
            fi
        elif read_identity "$leader_pid" && [[ "$identity_ppid" == "$supervisor_pid" &&
            "$identity_start" == "$leader_start" ]]; then
            # An unvalidated launch has received no go-ahead and cannot run SQL.
            # Only our direct child is signalled; never its unverified group.
            kill -KILL "$leader_pid" 2>/dev/null || cleanup_failed=true
        fi
        if wait "$leader_pid" 2>/dev/null; then :; fi
        leader_pid=
    fi
    rm -rf "$state" || cleanup_failed=true
    if [[ "$cleanup_failed" == true ]]; then result=1; fi
    exit "$result"
}
trap 'cleanup "$?"' EXIT
trap 'trap "" INT TERM; exit 130' INT
trap 'trap "" INT TERM; exit 143' TERM

mkfifo "$state/events" "$state/go" "$state/release" "$state/cancel" "$state/timer"
exec {events_fd}<>"$state/events"
exec {go_fd}<>"$state/go"
exec {release_fd}<>"$state/release"
exec {cancel_fd}<>"$state/cancel"
exec {timer_fd}<>"$state/timer"
# Non-job-control launch keeps setsid's PID equal to the actual session leader.
set +m
setsid bash -c '
    set -euo pipefail
    state=$1 events_fd=$2 go_fd=$3 release_fd=$4
    wait_interrupted=false
    trap "wait_interrupted=true" INT TERM
    read_identity "$BASHPID"
    printf "ready %s %s %s %s\n" "$BASHPID" "$identity_pgid" "$identity_sid" "$identity_start" >&"$events_fd"
    if ! read -r -t "$AI_AGENT_PREPARE_TIMEOUT_SECONDS" <&"$go_fd"; then exit 2; fi
    (
        trap - INT TERM
        set -euo pipefail
        prepare
    ) &
    command_pid=$!
    while :; do
        wait_interrupted=false
        if wait "$command_pid"; then result=0; else result=$?; fi
        # A caught group signal can interrupt wait without reaping the command.
        if [[ "$wait_interrupted" == false ]] || ! kill -0 "$command_pid" 2>/dev/null; then break; fi
    done
    printf "%s\n" "$result" >"$state/result"
    printf "done %s\n" "$result" >&"$events_fd"
    # Hold the PID/PGID until the parent has cancelled and reaped the watchdog.
    while ! read -r <&"$release_fd"; do :; done
    exit 0
' bash "$state" "$events_fd" "$go_fd" "$release_fd" &
leader_pid=$!
if read_identity "$leader_pid"; then leader_start=$identity_start; fi
started=$SECONDS
while ! read -r -t 0.05 tag reported_pid reported_pgid reported_sid reported_start <&"$events_fd"; do
    if ! kill -0 "$leader_pid" 2>/dev/null; then
        if wait "$leader_pid"; then result=2; else result=$?; fi
        leader_pid=
        echo "ERROR: setsid failed to start the preparation session" >&2
        exit "$result"
    fi
    if (( SECONDS - started >= AI_AGENT_PREPARE_TIMEOUT_SECONDS )); then
        echo "ERROR: AiAgent test preparation timed out during session startup" >&2
        exit 124
    fi
done
if [[ "$tag" != ready ]] || ! valid_pid "$reported_pid" || ! valid_pid "$reported_pgid" ||
    [[ "$reported_pid" != "$leader_pid" || "$reported_pgid" != "$leader_pid" ||
        "$reported_sid" != "$leader_pid" || "$reported_pgid" == "$supervisor_pgid" ]] ||
    ! read_identity "$leader_pid" || [[ "$identity_ppid" != "$supervisor_pid" ||
        "$identity_pgid" != "$leader_pid" || "$identity_sid" != "$leader_pid" ||
        "$identity_start" != "$reported_start" ||
        ( -n "$leader_start" && "$identity_start" != "$leader_start" ) ]]; then
    echo "ERROR: setsid did not create the expected exclusive session/process group" >&2
    exit 2
fi
leader_start=$identity_start
group_verified=true
printf '%s\n' "$leader_pid" >"$state/leader.pid"

watchdog() {
    trap 'result=$?; printf "watchdog %s\n" "$result" >&"$events_fd"' EXIT
    if read -r -t "$AI_AGENT_PREPARE_TIMEOUT_SECONDS" <&"$cancel_fd"; then return 0; fi
    # Completion before the deadline wins even if the parent is not scheduled.
    if [[ -f "$state/result" ]]; then return 0; fi
    printf 'deadline\n' >"$state/timed-out"
    printf 'deadline\n' >&"$events_fd"
    signal_group TERM
    if read -r -t "$AI_AGENT_PREPARE_KILL_AFTER_SECONDS" <&"$timer_fd"; then :; fi
    signal_group KILL
}
watchdog &
watchdog_pid=$!
printf '%s\n' "$watchdog_pid" >"$state/watchdog.pid"
printf 'go\n' >&"$go_fd"
while read -r event result <&"$events_fd"; do
    if [[ "$event" == watchdog && "$result" == 0 && -f "$state/result" ]]; then continue; fi
    break
done
if [[ "$event" != done && "$event" != deadline ]]; then
    echo "ERROR: preparation supervision failed" >&2
    exit 1
fi
cancel_watchdog
if [[ -f "$state/timed-out" ]]; then
    if wait "$leader_pid" 2>/dev/null; then :; fi
    leader_pid=
    echo "ERROR: AiAgent test preparation timed out" >&2
    exit 124
fi
if [[ "$event" != done || ! "$result" =~ ^[0-9]+$ ]] || (( result > 255 )); then
    echo "ERROR: invalid preparation result" >&2
    exit 1
fi
printf 'release\n' >&"$release_fd"
if wait "$leader_pid"; then :; else
    echo "ERROR: preparation session failed to close" >&2
    leader_pid=
    exit 1
fi
leader_pid=
exit "$result"
