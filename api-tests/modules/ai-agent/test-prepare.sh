#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
bash_executable=$(command -v bash)
fixture=$(mktemp -d)
cleanup() {
    local result=$? pid file
    trap '' INT TERM
    trap - EXIT
    for file in "$fixture"/case-*/*.pid; do
        [[ -f "$file" ]] || continue
        read -r pid <"$file"
        if [[ "$pid" =~ ^[1-9][0-9]*$ ]] && kill -0 "$pid" 2>/dev/null; then
            kill -KILL "$pid" 2>/dev/null || true
            wait "$pid" 2>/dev/null || true
        fi
    done
    rm -rf "$fixture"
    exit "$result"
}
trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM

fail() { echo "FAIL: $*" >&2; exit 1; }
assert_not_contains() {
    local pattern=$1 file=$2 result
    if [[ ! -f "$file" || ! -r "$file" ]]; then
        fail "cannot inspect file: $file"
    fi
    if grep -F -- "$pattern" "$file" >/dev/null; then
        fail "forbidden content found in $file"
    else
        result=$?
        if [[ "$result" != 1 ]]; then
            fail "grep could not inspect $file (exit $result)"
        fi
    fi
    return 0
}
assert_process_absent() {
    local pid=$1 file recorded owned=false
    if [[ ! "$pid" =~ ^[1-9][0-9]*$ ]]; then
        fail "invalid controlled process ID"
    fi
    if kill -0 "$pid" 2>/dev/null; then
        for file in "$fixture"/case-*/*.pid; do
            [[ -f "$file" ]] || continue
            read -r recorded <"$file"
            if [[ "$recorded" == "$pid" ]]; then owned=true; break; fi
        done
        if [[ "$owned" == true ]]; then
            kill -KILL "$pid" 2>/dev/null || true
            wait "$pid" 2>/dev/null || true
        fi
        fail "controlled process $pid is still alive"
    fi
    return 0
}
assert_temporaries_absent() {
    local directory=$1 remaining result
    if [[ ! -d "$directory" || ! -r "$directory" || ! -x "$directory" ]]; then
        fail "cannot inspect temporary directory: $directory"
    fi
    if remaining=$(find "$directory" -mindepth 1 -print -quit); then
        if [[ -n "$remaining" ]]; then
            fail "controlled temporaries remain in $directory"
        fi
    else
        result=$?
        fail "find could not inspect temporary directory (exit $result)"
    fi
    return 0
}

mkdir -p "$fixture/bin"
cat >"$fixture/bin/docker" <<'DOUBLE'
#!/usr/bin/env bash
set -euo pipefail
printf '%s\n' "$*" >>"$AI_AGENT_TEST_DIRECTORY/calls"
for file in "$TMPDIR"/*/leader.pid "$TMPDIR"/*/watchdog.pid; do
    [[ -f "$file" ]] || continue
    read -r pid <"$file"
    printf '%s\n' "$pid" >"$AI_AGENT_TEST_DIRECTORY/${file##*/}"
done
IFS= read -r stat <"/proc/$BASHPID/stat"
read -r -a fields <<<"${stat##*) }"
printf '%s %s %s\n' "$BASHPID" "${fields[2]}" "${fields[3]}" >"$AI_AGENT_TEST_DIRECTORY/command.identity"
stage=sql
[[ "$*" != *printenv* ]] || stage=environment
if [[ "$AI_AGENT_TEST_DOCKER_MODE" == "error-$stage" ]]; then
    echo "controlled ordinary error" >&2
    exit "$AI_AGENT_TEST_EXIT_CODE"
fi
if [[ "$AI_AGENT_TEST_DOCKER_MODE" == "kill-$stage" ]]; then
    echo "$BASHPID" >"$AI_AGENT_TEST_DIRECTORY/docker.pid"
    trap '' TERM
    exec 9<>"$AI_AGENT_TEST_DIRECTORY/fifo"
    read -r -u 9
fi
if [[ "$AI_AGENT_TEST_DOCKER_MODE" == "tree-$stage" || "$AI_AGENT_TEST_DOCKER_MODE" == "fail-$stage" || "$AI_AGENT_TEST_DOCKER_MODE" == "repeat-$stage" ]]; then
    echo "$BASHPID" >"$AI_AGENT_TEST_DIRECTORY/worker.pid"
    printf '%s %s %s\n' "$BASHPID" "${fields[2]}" "${fields[3]}" >"$AI_AGENT_TEST_DIRECTORY/worker.identity"
    trap '' TERM
    if [[ "$AI_AGENT_TEST_DOCKER_MODE" == "repeat-$stage" ]]; then
        # Observe group TERM without terminating: descendants still ignore TERM.
        trap 'printf "TERM\n" >"$AI_AGENT_TEST_DIRECTORY/cleanup-term"' TERM
    fi
    bash -c '
        echo "$BASHPID" >"$AI_AGENT_TEST_DIRECTORY/child.pid"
        read_identity "$BASHPID"
        printf "%s %s %s\n" "$BASHPID" "$identity_pgid" "$identity_sid" >"$AI_AGENT_TEST_DIRECTORY/child.identity"
        trap "" TERM
        bash -c '\''
            echo "$BASHPID" >"$AI_AGENT_TEST_DIRECTORY/grandchild.pid"
            read_identity "$BASHPID"
            printf "%s %s %s\n" "$BASHPID" "$identity_pgid" "$identity_sid" >"$AI_AGENT_TEST_DIRECTORY/grandchild.identity"
            trap "" TERM
            exec 9<>"$AI_AGENT_TEST_DIRECTORY/fifo"
            read -r -u 9
        '\'' &
        wait "$!"
    ' &
    exec 9<>"$AI_AGENT_TEST_DIRECTORY/fifo"
    if [[ "$AI_AGENT_TEST_DOCKER_MODE" == "fail-$stage" ]]; then
        for (( attempt=0; attempt<100; attempt++ )); do
            [[ ! -s "$AI_AGENT_TEST_DIRECTORY/grandchild.identity" ]] || break
            read -r -t 0.025 -u 9 || true
        done
        [[ -s "$AI_AGENT_TEST_DIRECTORY/grandchild.identity" ]]
        for file in "$TMPDIR"/*/events; do
            [[ -p "$file" ]] || continue
            printf 'invalid\n' >"$file"
        done
    fi
    if [[ "$AI_AGENT_TEST_DOCKER_MODE" == "repeat-$stage" ]]; then
        while ! read -r -u 9; do :; done
    else
        read -r -u 9
    fi
fi
if [[ "$AI_AGENT_TEST_DOCKER_MODE" == "hang-$stage" ]]; then
    echo "$BASHPID" >"$AI_AGENT_TEST_DIRECTORY/docker.pid"
    # No real sleep or database: the child waits on our own open FIFO.
    bash -c '
        exec 9<>"$AI_AGENT_TEST_DIRECTORY/fifo"
        echo "$BASHPID" >"$AI_AGENT_TEST_DIRECTORY/child.pid"
        trap "exit 0" TERM
        read -r -u 9
    ' &
    child=$!
    trap 'kill "$child" 2>/dev/null || true; wait "$child" 2>/dev/null || true; exit 0' TERM
    wait "$child"
elif [[ "$stage" == environment ]]; then
    echo "${AI_AGENT_TEST_ENVIRONMENT-Testing}"
else
    cat >"$AI_AGENT_TEST_DIRECTORY/sql"
fi
DOUBLE
chmod +x "$fixture/bin/docker"
bash -n "$fixture/bin/docker"
export PATH="$fixture/bin:$PATH"

count=0
new_case() {
    count=$((count + 1))
    export AI_AGENT_TEST_DIRECTORY="$fixture/case-$count"
    mkdir -p "$AI_AGENT_TEST_DIRECTORY/tmp"
    mkfifo "$AI_AGENT_TEST_DIRECTORY/fifo"
    export TMPDIR="$AI_AGENT_TEST_DIRECTORY/tmp"
    export AI_AGENT_TEST_DOCKER_MODE=normal AI_AGENT_TEST_EXIT_CODE=0 AI_AGENT_TEST_ENVIRONMENT=Testing
    unset AI_AGENT_PREPARE_TIMEOUT_SECONDS AI_AGENT_PREPARE_KILL_AFTER_SECONDS \
        AI_AGENT_DB_CONNECT_TIMEOUT_SECONDS AI_AGENT_DB_STATEMENT_TIMEOUT_MILLISECONDS AI_AGENT_DB_LOCK_TIMEOUT_MILLISECONDS
    unset AI_AGENT_TEST_COMMAND_PATH
}
run_case() {
    local expected=$1 result=0
    PATH="${AI_AGENT_TEST_COMMAND_PATH-$PATH}" "$bash_executable" "$SCRIPT_DIR/prepare.sh" \
        >"$AI_AGENT_TEST_DIRECTORY/output" 2>&1 || result=$?
    [[ "$result" == "$expected" ]] || {
        echo "FAIL: case $count expected $expected, received $result" >&2
        cat "$AI_AGENT_TEST_DIRECTORY/output" >&2
        exit 1
    }
    assert_temporaries_absent "$AI_AGENT_TEST_DIRECTORY/tmp"
    assert_supervision_absent
}
passed() { echo "PASS: $1"; }
wait_absent() {
    local pid=$1 attempt
    exec 9<>"$AI_AGENT_TEST_DIRECTORY/fifo"
    for (( attempt=0; attempt<40; attempt++ )); do
        if ! kill -0 "$pid" 2>/dev/null; then break; fi
        read -r -t 0.025 -u 9 || true
    done
    exec 9>&-
    assert_process_absent "$pid"
}
assert_supervision_absent() {
    local file pid
    for file in "$AI_AGENT_TEST_DIRECTORY/leader.pid" "$AI_AGENT_TEST_DIRECTORY/watchdog.pid"; do
        [[ -f "$file" ]] || continue
        read -r pid <"$file"
        wait_absent "$pid"
    done
}
assert_exclusive_group() {
    local leader command pgid sid
    read -r leader <"$AI_AGENT_TEST_DIRECTORY/leader.pid"
    read -r command pgid sid <"$AI_AGENT_TEST_DIRECTORY/command.identity"
    [[ "$leader" == "$pgid" && "$leader" == "$sid" ]]
    IFS= read -r stat <"/proc/$BASHPID/stat"
    read -r -a fields <<<"${stat##*) }"
    [[ "$pgid" != "${fields[2]}" ]]
}
assert_tree_absent() {
    local role pid leader watchdog command pgid sid recorded member_pgid member_sid
    assert_exclusive_group
    read -r leader <"$AI_AGENT_TEST_DIRECTORY/leader.pid"
    for role in worker child grandchild; do
        read -r pid <"$AI_AGENT_TEST_DIRECTORY/$role.pid"
        read -r recorded member_pgid member_sid <"$AI_AGENT_TEST_DIRECTORY/$role.identity"
        [[ "$recorded" == "$pid" && "$member_pgid" == "$leader" && "$member_sid" == "$leader" ]]
        wait_absent "$pid"
    done
    read -r watchdog <"$AI_AGENT_TEST_DIRECTORY/watchdog.pid"
    read -r command pgid sid <"$AI_AGENT_TEST_DIRECTORY/command.identity"
    printf 'PROCESS_TREE case=%s leader=%s watchdog=%s worker=%s child=%s grandchild=%s pgid=%s sid=%s absent=true\n' \
        "$count" "$leader" "$watchdog" "$(cat "$AI_AGENT_TEST_DIRECTORY/worker.pid")" \
        "$(cat "$AI_AGENT_TEST_DIRECTORY/child.pid")" "$(cat "$AI_AGENT_TEST_DIRECTORY/grandchild.pid")" "$pgid" "$sid"
}
run_helper() {
    local expected=$1 result=0
    shift
    # A deliberate helper failure must not abort the parent or remove its fixture.
    if (trap - EXIT; "$@") >"$AI_AGENT_TEST_DIRECTORY/helper-output" 2>&1; then
        result=0
    else
        result=$?
    fi
    if [[ "$result" != "$expected" ]]; then
        cat "$AI_AGENT_TEST_DIRECTORY/helper-output" >&2
        fail "helper case $count expected $expected, received $result"
    fi
}

new_case
printf '%s\n' permitted >"$AI_AGENT_TEST_DIRECTORY/content"
run_helper 0 assert_not_contains forbidden "$AI_AGENT_TEST_DIRECTORY/content"
passed 'negative content helper accepts absent content'

new_case
printf '%s\n' forbidden >"$AI_AGENT_TEST_DIRECTORY/content"
run_helper 1 assert_not_contains forbidden "$AI_AGENT_TEST_DIRECTORY/content"
grep -Fq 'forbidden content found' "$AI_AGENT_TEST_DIRECTORY/helper-output"
passed 'negative content helper rejects present content'

new_case
run_helper 1 assert_not_contains forbidden "$AI_AGENT_TEST_DIRECTORY/missing"
grep -Fq 'cannot inspect file' "$AI_AGENT_TEST_DIRECTORY/helper-output"
passed 'negative content helper rejects missing file'

new_case
printf '%s\n' permitted >"$AI_AGENT_TEST_DIRECTORY/content"
mkdir -p "$AI_AGENT_TEST_DIRECTORY/grep-bin"
printf '#!/usr/bin/env bash\nexit 2\n' >"$AI_AGENT_TEST_DIRECTORY/grep-bin/grep"
chmod +x "$AI_AGENT_TEST_DIRECTORY/grep-bin/grep"
PATH="$AI_AGENT_TEST_DIRECTORY/grep-bin:$PATH" run_helper 1 \
    assert_not_contains forbidden "$AI_AGENT_TEST_DIRECTORY/content"
grep -Fq 'grep could not inspect' "$AI_AGENT_TEST_DIRECTORY/helper-output"
passed 'negative content helper rejects grep errors'

new_case
bash -c 'exit 0' &
helper_pid=$!
echo "$helper_pid" >"$AI_AGENT_TEST_DIRECTORY/helper.pid"
wait "$helper_pid"
run_helper 0 assert_process_absent "$helper_pid"
passed 'negative process helper accepts absent process'

new_case
bash -c 'exec 9<>"$AI_AGENT_TEST_DIRECTORY/fifo"; read -r -u 9' &
helper_pid=$!
echo "$helper_pid" >"$AI_AGENT_TEST_DIRECTORY/helper.pid"
run_helper 1 assert_process_absent "$helper_pid"
wait "$helper_pid" 2>/dev/null || true
grep -Fq 'is still alive' "$AI_AGENT_TEST_DIRECTORY/helper-output"
assert_process_absent "$helper_pid"
passed 'negative process helper rejects and cleans a live controlled process'

new_case
run_helper 0 assert_temporaries_absent "$AI_AGENT_TEST_DIRECTORY/tmp"
passed 'temporary helper accepts an empty owned directory'

new_case
mkfifo "$AI_AGENT_TEST_DIRECTORY/tmp/surviving-fifo"
run_helper 1 assert_temporaries_absent "$AI_AGENT_TEST_DIRECTORY/tmp"
grep -Fq 'controlled temporaries remain' "$AI_AGENT_TEST_DIRECTORY/helper-output"
rm "$AI_AGENT_TEST_DIRECTORY/tmp/surviving-fifo"
assert_temporaries_absent "$AI_AGENT_TEST_DIRECTORY/tmp"
passed 'temporary helper detects a surviving FIFO'

new_case
run_helper 1 assert_temporaries_absent "$AI_AGENT_TEST_DIRECTORY/missing"
grep -Fq 'cannot inspect temporary directory' "$AI_AGENT_TEST_DIRECTORY/helper-output"
passed 'temporary helper rejects a missing directory'

new_case
mkdir -p "$AI_AGENT_TEST_DIRECTORY/find-bin"
printf '#!/usr/bin/env bash\nexit 2\n' >"$AI_AGENT_TEST_DIRECTORY/find-bin/find"
chmod +x "$AI_AGENT_TEST_DIRECTORY/find-bin/find"
PATH="$AI_AGENT_TEST_DIRECTORY/find-bin:$PATH" run_helper 1 \
    assert_temporaries_absent "$AI_AGENT_TEST_DIRECTORY/tmp"
grep -Fq 'find could not inspect temporary directory (exit 2)' "$AI_AGENT_TEST_DIRECTORY/helper-output"
passed 'temporary helper distinguishes inspection failure from absence'

new_case
bash -c '
    bash -c '\''exec 9<>"$AI_AGENT_TEST_DIRECTORY/fifo"; read -r -u 9'\'' &
    echo "$!" >"$AI_AGENT_TEST_DIRECTORY/child.pid"
    wait "$!"
' &
helper_pid=$!
echo "$helper_pid" >"$AI_AGENT_TEST_DIRECTORY/worker.pid"
exec 9<>"$AI_AGENT_TEST_DIRECTORY/fifo"
for (( attempt=0; attempt<100; attempt++ )); do
    [[ ! -s "$AI_AGENT_TEST_DIRECTORY/child.pid" ]] || break
    read -r -t 0.025 -u 9 || true
done
exec 9>&-
read -r child <"$AI_AGENT_TEST_DIRECTORY/child.pid"
run_helper 1 assert_process_absent "$child"
grep -Fq 'is still alive' "$AI_AGENT_TEST_DIRECTORY/helper-output"
if wait "$helper_pid"; then :; fi
wait_absent "$child"
assert_process_absent "$helper_pid"
passed 'negative process helper detects a surviving descendant'

new_case
mkdir -p "$AI_AGENT_TEST_DIRECTORY/empty-bin"
export AI_AGENT_TEST_COMMAND_PATH="$AI_AGENT_TEST_DIRECTORY/empty-bin"
run_case 127
[[ ! -e "$AI_AGENT_TEST_DIRECTORY/calls" ]]
grep -Fq 'setsid with exclusive session/process-group support is required' "$AI_AGENT_TEST_DIRECTORY/output"
passed 'missing setsid fails before Docker'

new_case
mkdir -p "$AI_AGENT_TEST_DIRECTORY/setsid-bin"
printf '#!/usr/bin/env bash\nexec "$@"\n' >"$AI_AGENT_TEST_DIRECTORY/setsid-bin/setsid"
chmod +x "$AI_AGENT_TEST_DIRECTORY/setsid-bin/setsid"
export AI_AGENT_TEST_COMMAND_PATH="$AI_AGENT_TEST_DIRECTORY/setsid-bin:$PATH"
bash -c 'exec 9<>"$AI_AGENT_TEST_DIRECTORY/fifo"; read -r -u 9' &
protected=$!
echo "$protected" >"$AI_AGENT_TEST_DIRECTORY/protected.pid"
run_case 2
kill -0 "$protected"
kill -KILL "$protected"
wait "$protected" 2>/dev/null || true
assert_process_absent "$protected"
[[ ! -e "$AI_AGENT_TEST_DIRECTORY/calls" ]]
grep -Fq 'setsid did not create the expected exclusive session/process group' "$AI_AGENT_TEST_DIRECTORY/output"
passed 'setsid that leaves the supervisor group is rejected'

new_case
mkdir -p "$AI_AGENT_TEST_DIRECTORY/setsid-bin"
printf '#!/usr/bin/env bash\necho "controlled setsid error" >&2\nexit 42\n' >"$AI_AGENT_TEST_DIRECTORY/setsid-bin/setsid"
chmod +x "$AI_AGENT_TEST_DIRECTORY/setsid-bin/setsid"
export AI_AGENT_TEST_COMMAND_PATH="$AI_AGENT_TEST_DIRECTORY/setsid-bin:$PATH"
run_case 42
[[ ! -e "$AI_AGENT_TEST_DIRECTORY/calls" ]]
grep -Fxq 'controlled setsid error' "$AI_AGENT_TEST_DIRECTORY/output"
grep -Fxq 'ERROR: setsid failed to start the preparation session' "$AI_AGENT_TEST_DIRECTORY/output"
passed 'setsid ordinary error is preserved'

for invalid in 0 1 -1 text '' 2147483648 999999999999999999999999999; do
    new_case
    export AI_AGENT_TEST_REPORTED_PGID="$invalid"
    mkdir -p "$AI_AGENT_TEST_DIRECTORY/setsid-bin"
    cat >"$AI_AGENT_TEST_DIRECTORY/setsid-bin/setsid" <<'DOUBLE'
#!/usr/bin/env bash
set -euo pipefail
read_identity "$BASHPID"
printf 'ready %s %s %s %s\n' "$BASHPID" "$AI_AGENT_TEST_REPORTED_PGID" "$BASHPID" "$identity_start" >&"$6"
exec 9<>"$AI_AGENT_TEST_DIRECTORY/fifo"
read -r -u 9
DOUBLE
    chmod +x "$AI_AGENT_TEST_DIRECTORY/setsid-bin/setsid"
    export AI_AGENT_TEST_COMMAND_PATH="$AI_AGENT_TEST_DIRECTORY/setsid-bin:$PATH"
    run_case 2
    [[ ! -e "$AI_AGENT_TEST_DIRECTORY/calls" ]]
    grep -Fq 'setsid did not create the expected exclusive session/process group' "$AI_AGENT_TEST_DIRECTORY/output"
    passed 'invalid group identity rejected before Docker'
done
unset AI_AGENT_TEST_REPORTED_PGID

# Construct obsolete names solely to prove rejection, without preserving a task
# identifier as a versioned configuration interface.
printf -v legacy_prefix 'AI%03d%s_' 1 D
for suffix in PREPARE_TIMEOUT_SECONDS PREPARE_KILL_AFTER_SECONDS \
    PGCONNECT_TIMEOUT_SECONDS STATEMENT_TIMEOUT_MS LOCK_TIMEOUT_MS; do
    new_case
    legacy_variable="$legacy_prefix$suffix"
    printf -v "$legacy_variable" '%s' 1
    export "$legacy_variable"
    run_case 2
    unset "$legacy_variable"
    [[ ! -e "$AI_AGENT_TEST_DIRECTORY/calls" ]]
    grep -Fq 'obsolete task-prefixed preparation variables are unsupported' "$AI_AGENT_TEST_DIRECTORY/output"
    passed 'obsolete configuration name rejected before Docker'
done

new_case
run_case 0
[[ "$(wc -l <"$AI_AGENT_TEST_DIRECTORY/calls")" == 2 ]]
grep -Fxq 'BEGIN;' "$AI_AGENT_TEST_DIRECTORY/sql"
grep -Fxq 'COMMIT;' "$AI_AGENT_TEST_DIRECTORY/sql"
grep -Fq 'PGCONNECT_TIMEOUT=15' "$AI_AGENT_TEST_DIRECTORY/calls"
grep -Fq 'PGOPTIONS=-c statement_timeout=30000 -c lock_timeout=10000' "$AI_AGENT_TEST_DIRECTORY/calls"
grep -Fq 'psql -U alxarafe -d alxarafe_security_test -v ON_ERROR_STOP=1' "$AI_AGENT_TEST_DIRECTORY/calls"
assert_exclusive_group
passed 'normal execution and default PostgreSQL limits'

new_case
export AI_AGENT_PREPARE_TIMEOUT_SECONDS=009 AI_AGENT_PREPARE_KILL_AFTER_SECONDS=02 \
    AI_AGENT_DB_CONNECT_TIMEOUT_SECONDS=03 AI_AGENT_DB_STATEMENT_TIMEOUT_MILLISECONDS=004000 AI_AGENT_DB_LOCK_TIMEOUT_MILLISECONDS=00500
run_case 0
grep -Fq 'PGCONNECT_TIMEOUT=3' "$AI_AGENT_TEST_DIRECTORY/calls"
grep -Fq 'PGOPTIONS=-c statement_timeout=4000 -c lock_timeout=500' "$AI_AGENT_TEST_DIRECTORY/calls"
assert_exclusive_group
passed 'explicit overrides use positive decimal integers'

new_case
mkdir -p "$AI_AGENT_TEST_DIRECTORY/no-timeout-bin"
for tool in bash setsid mktemp mkfifo rm cat; do
    ln -s "$(command -v "$tool")" "$AI_AGENT_TEST_DIRECTORY/no-timeout-bin/$tool"
done
ln -s "$fixture/bin/docker" "$AI_AGENT_TEST_DIRECTORY/no-timeout-bin/docker"
export AI_AGENT_TEST_COMMAND_PATH="$AI_AGENT_TEST_DIRECTORY/no-timeout-bin"
[[ ! -e "$AI_AGENT_TEST_COMMAND_PATH/timeout" ]]
run_case 0
assert_exclusive_group
passed 'preparation succeeds without a timeout command'

for variable in AI_AGENT_PREPARE_TIMEOUT_SECONDS AI_AGENT_PREPARE_KILL_AFTER_SECONDS \
    AI_AGENT_DB_CONNECT_TIMEOUT_SECONDS AI_AGENT_DB_STATEMENT_TIMEOUT_MILLISECONDS AI_AGENT_DB_LOCK_TIMEOUT_MILLISECONDS; do
    for invalid in '' 0 -1 1.5 '1s' '1;echo SECRET_MARKER' 2147483648; do
        new_case
        printf -v "$variable" '%s' "$invalid"
        export "$variable"
        run_case 2
        [[ ! -e "$AI_AGENT_TEST_DIRECTORY/calls" ]]
        grep -Fq "$variable must be a positive integer" "$AI_AGENT_TEST_DIRECTORY/output"
        assert_not_contains SECRET_MARKER "$AI_AGENT_TEST_DIRECTORY/output"
        passed "invalid $variable rejected before Docker"
    done
done

new_case
export AI_AGENT_TEST_ENVIRONMENT=Production
run_case 1
[[ "$(wc -l <"$AI_AGENT_TEST_DIRECTORY/calls")" == 1 ]]
[[ ! -e "$AI_AGENT_TEST_DIRECTORY/sql" ]]
passed 'Testing guard prevents SQL execution'

for specification in environment:77 sql:42 sql:124 sql:137; do
    new_case
    export AI_AGENT_TEST_DOCKER_MODE="error-${specification%:*}" AI_AGENT_TEST_EXIT_CODE="${specification#*:}"
    run_case "$AI_AGENT_TEST_EXIT_CODE"
    grep -Fxq 'controlled ordinary error' "$AI_AGENT_TEST_DIRECTORY/output"
    assert_not_contains 'timed out' "$AI_AGENT_TEST_DIRECTORY/output"
    passed "ordinary ${specification%:*} exit $AI_AGENT_TEST_EXIT_CODE preserved"
done

for stage in environment sql; do
    new_case
    export AI_AGENT_TEST_DOCKER_MODE="hang-$stage" AI_AGENT_PREPARE_TIMEOUT_SECONDS=1 AI_AGENT_PREPARE_KILL_AFTER_SECONDS=1
    started=$SECONDS
    run_case 124
    (( SECONDS - started <= 4 ))
    grep -Fxq 'ERROR: AiAgent test preparation timed out' "$AI_AGENT_TEST_DIRECTORY/output"
    child=$(cat "$AI_AGENT_TEST_DIRECTORY/child.pid")
    docker_pid=$(cat "$AI_AGENT_TEST_DIRECTORY/docker.pid")
    # Allow the controlled TERM handler to reap its child, using only bounded
    # FIFO reads rather than sleeps. The PID must disappear, not just stop.
    exec 9<>"$AI_AGENT_TEST_DIRECTORY/fifo"
    for (( attempt=0; attempt<20; attempt++ )); do
        if ! kill -0 "$child" 2>/dev/null && ! kill -0 "$docker_pid" 2>/dev/null; then break; fi
        read -r -t 0.05 -u 9 || true
    done
    exec 9>&-
    assert_process_absent "$child"
    assert_process_absent "$docker_pid"
    passed "bounded $stage timeout returns 124 and leaves no child"
done

new_case
export AI_AGENT_TEST_DOCKER_MODE=kill-environment AI_AGENT_PREPARE_TIMEOUT_SECONDS=1 AI_AGENT_PREPARE_KILL_AFTER_SECONDS=1
started=$SECONDS
run_case 124
(( SECONDS - started <= 4 ))
grep -Fxq 'ERROR: AiAgent test preparation timed out' "$AI_AGENT_TEST_DIRECTORY/output"
docker_pid=$(cat "$AI_AGENT_TEST_DIRECTORY/docker.pid")
exec 9<>"$AI_AGENT_TEST_DIRECTORY/fifo"
for (( attempt=0; attempt<20; attempt++ )); do
    kill -0 "$docker_pid" 2>/dev/null || break
    read -r -t 0.05 -u 9 || true
done
exec 9>&-
assert_process_absent "$docker_pid"
passed 'TERM-resistant process is killed, returns 124 and leaves no process'

for stage in environment sql; do
    new_case
    export AI_AGENT_TEST_DOCKER_MODE="tree-$stage" AI_AGENT_PREPARE_TIMEOUT_SECONDS=1 AI_AGENT_PREPARE_KILL_AFTER_SECONDS=1
    started=$SECONDS
    run_case 124
    (( SECONDS - started <= 4 ))
    grep -Fxq 'ERROR: AiAgent test preparation timed out' "$AI_AGENT_TEST_DIRECTORY/output"
    assert_tree_absent
    passed "KILL reaches resistant $stage worker, child and grandchild"
done

for signal in INT TERM; do
    new_case
    export AI_AGENT_TEST_DOCKER_MODE=tree-environment AI_AGENT_PREPARE_KILL_AFTER_SECONDS=1
    # Job control keeps an asynchronously launched test subject's INT catchable.
    set -m
    bash "$SCRIPT_DIR/prepare.sh" >"$AI_AGENT_TEST_DIRECTORY/output" 2>&1 &
    supervisor=$!
    set +m
    echo "$supervisor" >"$AI_AGENT_TEST_DIRECTORY/supervisor.pid"
    exec 9<>"$AI_AGENT_TEST_DIRECTORY/fifo"
    for (( attempt=0; attempt<100; attempt++ )); do
        [[ ! -s "$AI_AGENT_TEST_DIRECTORY/grandchild.identity" ]] || break
        read -r -t 0.025 -u 9 || true
    done
    exec 9>&-
    [[ -s "$AI_AGENT_TEST_DIRECTORY/grandchild.identity" ]]
    kill -s "$signal" "$supervisor"
    if wait "$supervisor"; then result=0; else result=$?; fi
    if [[ "$signal" == INT ]]; then [[ "$result" == 130 ]]; else [[ "$result" == 143 ]]; fi
    assert_temporaries_absent "$AI_AGENT_TEST_DIRECTORY/tmp"
    assert_supervision_absent
    assert_tree_absent
    assert_process_absent "$supervisor"
    passed "$signal cleans only the controlled session and reaps watchdog"
done

run_repeated_signal_case() {
    local first=$1 second=$2 supervisor watchdog result leader worker child grandchild attempt
    new_case
    export AI_AGENT_TEST_DOCKER_MODE=repeat-environment AI_AGENT_PREPARE_KILL_AFTER_SECONDS=1
    set -m
    bash "$SCRIPT_DIR/prepare.sh" >"$AI_AGENT_TEST_DIRECTORY/output" 2>&1 &
    supervisor=$!
    set +m
    echo "$supervisor" >"$AI_AGENT_TEST_DIRECTORY/supervisor.pid"
    exec 9<>"$AI_AGENT_TEST_DIRECTORY/fifo"
    for (( attempt=0; attempt<100; attempt++ )); do
        [[ ! -s "$AI_AGENT_TEST_DIRECTORY/grandchild.identity" ]] || break
        read -r -t 0.025 -u 9 || true
    done
    [[ -s "$AI_AGENT_TEST_DIRECTORY/grandchild.identity" ]] || fail 'resistant tree did not become ready'
    read -r watchdog <"$AI_AGENT_TEST_DIRECTORY/watchdog.pid"
    read -r leader <"$AI_AGENT_TEST_DIRECTORY/leader.pid"
    read -r worker <"$AI_AGENT_TEST_DIRECTORY/worker.pid"
    read -r child <"$AI_AGENT_TEST_DIRECTORY/child.pid"
    read -r grandchild <"$AI_AGENT_TEST_DIRECTORY/grandchild.pid"
    kill -s "$first" "$supervisor"
    # The worker acknowledges group TERM only after cleanup has reaped watchdog.
    # Its live resistant tree and leader prove KILL has not happened yet.
    for (( attempt=0; attempt<100; attempt++ )); do
        [[ ! -s "$AI_AGENT_TEST_DIRECTORY/cleanup-term" ]] || break
        read -r -t 0.025 -u 9 || true
    done
    exec 9>&-
    [[ -s "$AI_AGENT_TEST_DIRECTORY/cleanup-term" ]] || fail 'cleanup TERM was not observed'
    grep -Fxq TERM "$AI_AGENT_TEST_DIRECTORY/cleanup-term"
    assert_process_absent "$watchdog"
    kill -0 "$supervisor"
    kill -0 "$leader"
    kill -0 "$worker"
    kill -0 "$child"
    kill -0 "$grandchild"
    kill -s "$second" "$supervisor"
    if wait "$supervisor"; then result=0; else result=$?; fi
    if [[ "$first" == INT ]]; then
        [[ "$result" == 130 ]] || fail 'repeated signal changed first INT status'
    else
        [[ "$result" == 143 ]] || fail 'repeated signal changed first TERM status'
    fi
    assert_temporaries_absent "$AI_AGENT_TEST_DIRECTORY/tmp"
    assert_supervision_absent
    assert_tree_absent
    assert_process_absent "$supervisor"
    printf 'REPEATED_SIGNAL case=%s first=%s second=%s exit=%s supervisor=%s leader=%s watchdog=%s worker=%s child=%s grandchild=%s grace_observed=true all_absent=true temporaries_absent=true\n' \
        "$count" "$first" "$second" "$result" "$supervisor" "$leader" "$watchdog" "$worker" "$child" "$grandchild"
}
for (( repetition=1; repetition<=20; repetition++ )); do
    run_repeated_signal_case TERM TERM
    passed "double TERM during cleanup repetition $repetition/20 preserves 143 and removes tree and temporaries"
done
run_repeated_signal_case INT TERM
passed 'TERM during INT cleanup preserves 130 and removes tree and temporaries'
run_repeated_signal_case TERM INT
passed 'INT during TERM cleanup preserves 143 and removes tree and temporaries'

new_case
export AI_AGENT_TEST_DOCKER_MODE=fail-environment AI_AGENT_PREPARE_KILL_AFTER_SECONDS=1
run_case 1
grep -Fxq 'ERROR: preparation supervision failed' "$AI_AGENT_TEST_DIRECTORY/output"
assert_tree_absent
passed 'unexpected protocol failure cleans session, tree, watchdog and temporaries'

for file in "$fixture"/case-*/*.pid; do
    [[ -f "$file" ]] || continue
    read -r pid <"$file"
    assert_process_absent "$pid"
done
echo "PREPARE SCRIPT TESTS PASS: $count/$count"
