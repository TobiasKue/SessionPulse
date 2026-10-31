#!/usr/bin/env bash
# Tests for claude-plugin/hooks/scripts/status.sh.
#
#   tests/hook/run.sh            run all tests
#   tests/hook/run.sh --update   rewrite expected files from the current output
#                                (review the diff before committing!)
#
# Case layout (tests/hook/cases/<name>/):
#   NN-<anything>.json   hook payloads, fed to the hook in file-name order;
#                        step i runs at time 1700000000 + 10*i
#   sessionpulse.conf    optional config placed in the status directory
#   env                  optional KEY=VALUE lines set for the hook (e.g. CLAUDE_PID)
#   expected/<id>.json   expected status files; no expected/ = no files at all
#
# Payload paths such as transcript_path are relative to tests/hook/.
#
# Environment: HOOK_MAX_AVG_MS (default 150) limits the average hook run time.

set -u
cd "$(dirname "$0")" || exit 1
HOOK="$(cd ../../claude-plugin/hooks/scripts && pwd)/status.sh"
UPDATE=0
[[ ${1:-} == --update ]] && UPDATE=1

WORK=$(mktemp -d)
trap 'rm -rf "$WORK"' EXIT
FAILED=0 PASSED=0

pass() { PASSED=$((PASSED + 1)); }
fail() { FAILED=$((FAILED + 1)); printf 'FAIL %s\n' "$*"; }

# Nothing from the environment the tests run in (e.g. inside Claude Code) may leak into the hook.
CLEAN=(env -u LOCALAPPDATA -u USERPROFILE -u WSL_DISTRO_NAME -u CLAUDE_CODE_ENTRYPOINT -u CLAUDE_PID)
EXTRA=()   # per-case variables from cases/<name>/env

# run_hook <status dir> <now> <payload file>; sets OUT (stdout+stderr) and RC
run_hook() {
    OUT=$("${CLEAN[@]}" "${EXTRA[@]}" SESSIONPULSE_DIR="$1" SESSIONPULSE_NOW="$2" bash "$HOOK" < "$3" 2>&1)
    RC=$?
}

check_silent() { # <label>
    if [[ $RC -ne 0 ]]; then fail "$1: exit code $RC"; return 1; fi
    if [[ -n $OUT ]]; then fail "$1: produced output: ${OUT:0:200}"; return 1; fi
    return 0
}

# --- golden cases ------------------------------------------------------------

for case_dir in cases/*/; do
    name=$(basename "$case_dir")
    dir="$WORK/$name/not-yet-created"
    mkdir -p "$WORK/$name"
    if [[ -f $case_dir/sessionpulse.conf ]]; then
        mkdir -p "$dir"
        cp "$case_dir/sessionpulse.conf" "$dir/"
    fi
    EXTRA=()
    if [[ -f $case_dir/env ]]; then
        while IFS= read -r line || [[ -n $line ]]; do [[ -n $line ]] && EXTRA+=("$line"); done < "$case_dir/env"
    fi
    ok=1 i=0
    for step in "$case_dir"/[0-9][0-9]-*.json; do
        i=$((i + 1))
        run_hook "$dir" $((1700000000 + 10 * i)) "$step"
        check_silent "$name/$(basename "$step")" || ok=0
    done

    actual=()
    [[ -d $dir/sessions ]] && for f in "$dir"/sessions/* "$dir"/sessions/.*; do
        [[ -f $f ]] && actual+=("$(basename "$f")")
    done

    if (( UPDATE )); then
        rm -rf "$case_dir/expected"
        if (( ${#actual[@]} )); then
            mkdir -p "$case_dir/expected"
            cp "$dir"/sessions/*.json "$case_dir/expected/"
        fi
        continue
    fi

    expected=()
    [[ -d $case_dir/expected ]] && for f in "$case_dir"/expected/*.json; do expected+=("$(basename "$f")"); done
    if [[ "${actual[*]}" != "${expected[*]}" ]]; then
        fail "$name: files [${actual[*]}], expected [${expected[*]}]"
        ok=0
    else
        for f in "${expected[@]}"; do
            if ! diff -u "$case_dir/expected/$f" "$dir/sessions/$f" > "$WORK/diff"; then
                fail "$name/$f differs:"; cat "$WORK/diff"; ok=0
            fi
        done
    fi
    (( ok )) && pass
done

EXTRA=()

if (( UPDATE )); then echo "expected files updated; review with git diff"; exit 0; fi

# --- robustness --------------------------------------------------------------

payload() { # <file> <json>
    printf '%s' "$2" > "$1"
}

robust() { # <label> <stdin file> [status dir]
    local d=${3:-$WORK/robust}
    run_hook "$d" 1700000000 "$2"
    check_silent "$1" && pass
}

printf '' > "$WORK/empty"
robust "empty stdin" "$WORK/empty"
payload "$WORK/garbage" 'this is { not json "session_id": '
robust "garbage stdin" "$WORK/garbage"
head -c 3000 /dev/urandom > "$WORK/binary" 2>/dev/null || printf '\x01\x02\xff' > "$WORK/binary"
robust "binary stdin" "$WORK/binary"
payload "$WORK/noevent" '{"session_id":"abc"}'
robust "missing event name" "$WORK/noevent"
if [[ -d $WORK/robust/sessions ]] && compgen -G "$WORK/robust/sessions/*" > /dev/null; then
    fail "invalid input created files: $(ls -A "$WORK/robust/sessions")"
else
    pass
fi

payload "$WORK/valid" '{"session_id":"s1","cwd":"/tmp/x","hook_event_name":"Stop"}'
robust "unwritable status dir" "$WORK/valid" "/dev/null/sessionpulse"
touch "$WORK/afile"
robust "status dir is a file" "$WORK/valid" "$WORK/afile"

# Large payload: the hook must drain stdin (no SIGPIPE for the writer) and
# still parse the fields in front of tool_input.
big=$(printf '%*s' 1000000 '' | tr ' ' 'x')
payload "$WORK/big" '{"session_id":"big-1","cwd":"/home/user/projects/demo","hook_event_name":"PreToolUse","tool_name":"Write","tool_input":{"file_path":"/tmp/f","content":"'"$big"'"}}'
d="$WORK/bigdir"
OUT=$( (cat "$WORK/big"; echo "writer-status=$?" >&2) 2>"$WORK/writer" | "${CLEAN[@]}" SESSIONPULSE_DIR="$d" bash "$HOOK" 2>&1)
RC=$?
if check_silent "1 MB payload"; then
    if ! grep -q 'writer-status=0' "$WORK/writer"; then
        fail "1 MB payload: writer did not finish cleanly ($(cat "$WORK/writer")); stdin not drained?"
    elif ! grep -q '"tool":"Write"' "$d/sessions/big-1.json" 2>/dev/null; then
        fail "1 MB payload: unexpected status file: $(cat "$d/sessions/big-1.json" 2>&1)"
    else
        pass
    fi
fi

# The task list comes after the final message, which can exceed the read limit.
long=$(printf '%*s' 20000 '' | tr ' ' 'x')
payload "$WORK/longstop" '{"session_id":"long-1","cwd":"/tmp/x","hook_event_name":"Stop","last_assistant_message":"'"$long"'","background_tasks":[{"id":"a","type":"subagent","status":"running"}],"session_crons":[]}'
d="$WORK/longstopdir"
run_hook "$d" 1700000000 "$WORK/longstop"
if check_silent "long final message"; then
    if grep -q '"state":"working","detail":"subagent".*"agents":1' "$d/sessions/long-1.json" 2>/dev/null; then pass
    else fail "long final message: $(cat "$d/sessions/long-1.json" 2>&1)"; fi
fi

# --- keypad approvals (APPROVALS=1) ------------------------------------------
# The hook publishes approvals/<id>.json, waits for <id>.decision and answers
# Claude on stdout. A fake "keypad" in the background plays the plugin's part.

approval_payload() { # <tool> <command>
    printf '%s' '{"session_id":"appr-1","cwd":"/home/user/projects/demo","hook_event_name":"PermissionRequest","tool_name":"'"$1"'","tool_input":{"command":"'"$2"'","description":"x"},"permission_suggestions":[{"type":"addRules","rules":[{"toolName":"Bash","ruleContent":"rm -rf build"}],"behavior":"allow","destination":"localSettings"}]}'
}

# approval_case <label> <decision or "-"> <tool> <command> <expected stdout regex> [request regex]
approval_case() {
    local label=$1 decision=$2 d="$WORK/appr-$1"
    mkdir -p "$d"
    printf 'APPROVALS=1\nAPPROVAL_TIMEOUT=5\n' > "$d/sessionpulse.conf"
    approval_payload "$3" "$4" > "$d/payload"
    (
        for _ in $(seq 1 100); do
            reqs=("$d"/approvals/*.json)
            req=${reqs[0]}
            if [[ -f $req ]]; then
                cp "$req" "$d/request.seen"
                [[ $decision != - ]] && printf '%s\n' "$decision" > "${req%.json}.decision"
                exit 0
            fi
            sleep 0.05
        done
    ) &
    local keypad=$!
    local out
    out=$("${CLEAN[@]}" SESSIONPULSE_DIR="$d" SESSIONPULSE_NOW=1700000000 bash "$HOOK" < "$d/payload" 2>/dev/null)
    RC=$?
    wait "$keypad"
    if [[ $RC -ne 0 ]]; then fail "approval $label: exit $RC"; return; fi
    if [[ ! $out =~ $5 ]]; then fail "approval $label: stdout '$out' does not match /$5/"; return; fi
    if [[ -n ${6:-} ]] && ! grep -Eq "$6" "$d/request.seen" 2>/dev/null; then
        fail "approval $label: request $(cat "$d/request.seen" 2>/dev/null) does not match /$6/"; return
    fi
    if compgen -G "$d/approvals/*" > /dev/null; then fail "approval $label: leftover files $(ls "$d/approvals")"; return; fi
    pass
}

decision_json='^\{"hookSpecificOutput":\{"hookEventName":"PermissionRequest","decision":'
approval_case allow allow Bash "ls -la" "${decision_json}"'\{"behavior":"allow"\}\}\}$' '"risk":"normal".*"remember":true'
approval_case session session Bash "rm -rf build" "${decision_json}"'\{"behavior":"allow","updatedPermissions":\[\{"type":"addRules","rules":\[\{"toolName":"Bash","ruleContent":"rm -rf build"\}\],"behavior":"allow","destination":"session"\}\]\}\}\}$' '"preview":"rm -rf build","risk":"high"'
approval_case always always Bash "rm -rf build" '"destination":"localSettings"'
approval_case deny deny Bash "git push --force" '"behavior":"deny","message":"Denied on the SessionPulse keypad."' '"risk":"high"'
approval_case ask ask Bash "ls" '^$'
start=$EPOCHREALTIME
approval_case timeout - Bash "ls" '^$'
elapsed=$(( (${EPOCHREALTIME//[.,]/} - ${start//[.,]/}) / 1000000 ))
if (( elapsed < 4 || elapsed > 9 )); then fail "approval timeout took ${elapsed}s, expected about 5s"; else pass; fi

# Questions and plan approval are never held.
d="$WORK/appr-question"; mkdir -p "$d"; printf 'APPROVALS=1\n' > "$d/sessionpulse.conf"
payload "$d/payload" '{"session_id":"appr-2","cwd":"/tmp","hook_event_name":"PermissionRequest","tool_name":"AskUserQuestion","tool_input":{}}'
run_hook "$d" 1700000000 "$d/payload"
if check_silent "approval skips AskUserQuestion"; then
    if [[ -d $d/approvals ]]; then fail "AskUserQuestion created an approval request"; else pass; fi
fi

# --- status line helper (usage limits) -----------------------------------------

STATUSLINE="$(cd ../../claude-plugin/statusline && pwd)/statusline.sh"
d="$WORK/statusline"; mkdir -p "$d"
payload "$d/full" '{"session_id":"s","model":{"display_name":"Opus"},"rate_limits":{"five_hour":{"used_percentage":23.5,"resets_at":1738425600},"seven_day":{"used_percentage":41,"resets_at":1738857600}},"context_window":{"used_percentage":12}}'
out=$("${CLEAN[@]}" SESSIONPULSE_DIR="$d" SESSIONPULSE_NOW=1700000000 bash "$STATUSLINE" < "$d/full")
usage_expected='{"v":1,"updated":1700000000,"fiveHour":{"pct":23.5,"resetsAt":1738425600},"sevenDay":{"pct":41,"resetsAt":1738857600}}'
if [[ $(cat "$d/usage.json" 2>/dev/null) != "$usage_expected" ]]; then fail "statusline usage.json: $(cat "$d/usage.json" 2>&1)"
elif [[ $out != "5h 23% · 7d 41%" ]]; then fail "statusline output: '$out'"
else pass; fi

# Without rate limits (API key, or before the first response) the last values stay.
payload "$d/none" '{"session_id":"s","model":{"display_name":"Opus"}}'
out=$("${CLEAN[@]}" SESSIONPULSE_DIR="$d" SESSIONPULSE_NOW=1700000100 bash "$STATUSLINE" < "$d/none")
if [[ -n $out || $(cat "$d/usage.json") != "$usage_expected" ]]; then fail "statusline without limits changed state: '$out'"; else pass; fi

# STATUSLINE_CHAIN passes the same input on to an existing status line.
# shellcheck disable=SC2016 # expanded later by the helper, not here
printf 'STATUSLINE_CHAIN=read -r -d "" x; echo "chained ${#x}"\r\n' > "$d/sessionpulse.conf"
out=$("${CLEAN[@]}" SESSIONPULSE_DIR="$d" bash "$STATUSLINE" < "$d/full")
if [[ $out != "chained $(wc -c < "$d/full" | tr -d ' ')" ]]; then fail "statusline chain: '$out'"; else pass; fi

# The hook keeps a copy of the helper at <status dir>/statusline.sh.
d="$WORK/helper-copy"
payload "$WORK/start" '{"session_id":"h1","cwd":"/tmp","hook_event_name":"SessionStart","source":"startup"}'
run_hook "$d" 1700000000 "$WORK/start"
if check_silent "helper copy"; then
    if cmp -s "$d/statusline.sh" "$STATUSLINE"; then pass; else fail "status line helper not copied"; fi
fi

# --- default status directory ------------------------------------------------
# Windows (LOCALAPPDATA set): USERPROFILE/.sessionpulse, never below AppData, which
# the Store version of Claude Desktop redirects. Must match StatusPaths.cs.

w="$WORK/winprofile"
win_env=(env -u SESSIONPULSE_DIR LOCALAPPDATA="$w/AppData/Local" USERPROFILE="$w" HOME="$WORK/winhome")
payload "$WORK/default" '{"session_id":"def-1","cwd":"/tmp","hook_event_name":"Stop"}'
OUT=$("${win_env[@]}" bash "$HOOK" < "$WORK/default" 2>&1)
RC=$?
if check_silent "windows default dir"; then
    if [[ ! -f $w/.sessionpulse/sessions/def-1.json ]]; then fail "windows default dir: no status file below $w/.sessionpulse"
    elif [[ -e $w/AppData ]]; then fail "windows default dir: wrote below AppData"
    else pass; fi
fi
"${win_env[@]}" SESSIONPULSE_NOW=1700000000 bash "$STATUSLINE" < "$WORK/statusline/full" > /dev/null
if [[ -f $w/.sessionpulse/usage.json ]]; then pass; else fail "status line helper: no usage.json below $w/.sessionpulse"; fi

# WSL (WSL_DISTRO_NAME set): the Windows profile, asked once from cmd.exe and cached. A fake cmd.exe
# answers with drive Q:, so the computed /mnt/q/... path never exists and nothing is written there.
fake="$WORK/fakebin"; mkdir -p "$fake"
cat > "$fake/cmd.exe" <<EOF
#!/usr/bin/env bash
echo x >> "$WORK/cmd-calls"
printf 'Q:\\\\Users\\\\winuser\\r\\n'
EOF
chmod +x "$fake/cmd.exe"
wh="$WORK/wslhome"; mkdir -p "$wh"
wsl_env=(env -u SESSIONPULSE_DIR -u LOCALAPPDATA -u USERPROFILE -u XDG_CACHE_HOME -u CLAUDE_PID WSL_DISTRO_NAME=Ubuntu HOME="$wh" PATH="$fake:$PATH")
OUT=$("${wsl_env[@]}" bash "$HOOK" < "$WORK/default" 2>&1)
RC=$?
if check_silent "wsl windows dir"; then
    cached=$(cat "$wh/.cache/sessionpulse/windows-dir" 2>/dev/null)
    if [[ $cached != /mnt/q/Users/winuser/.sessionpulse ]]; then fail "wsl windows dir: cached '$cached'"
    elif [[ -e $wh/.local/state/sessionpulse ]]; then fail "wsl windows dir: wrote to the Linux default"
    else pass; fi
fi
# With the cache in place, cmd.exe is not asked again and the cached directory is used.
printf '%s\n' "$WORK/wslwin/.sessionpulse" > "$wh/.cache/sessionpulse/windows-dir"
"${wsl_env[@]}" CLAUDE_PID=4242 bash "$HOOK" < "$WORK/default" > /dev/null 2>&1
calls=$(wc -l < "$WORK/cmd-calls" | tr -d ' ')
if [[ $calls != 1 ]]; then fail "wsl windows dir: cmd.exe asked $calls times"
elif ! grep -q '"wsl":"Ubuntu"}$' "$WORK/wslwin/.sessionpulse/sessions/def-1.json" 2>/dev/null; then
    fail "wsl session: $(cat "$WORK/wslwin/.sessionpulse/sessions/def-1.json" 2>&1)"
elif grep -q '"pid"' "$WORK/wslwin/.sessionpulse/sessions/def-1.json"; then fail "wsl session: Linux pid written"
else pass; fi

# --- performance -------------------------------------------------------------

max_ms=${HOOK_MAX_AVG_MS:-150}
small="$WORK/small"
payload "$small" '{"session_id":"perf-1","cwd":"/home/user/projects/demo","hook_event_name":"PreToolUse","tool_name":"Bash","tool_input":{"command":"ls"}}'
large="$WORK/large"
payload "$large" '{"session_id":"perf-2","cwd":"/home/user/projects/demo","hook_event_name":"PreToolUse","tool_name":"Write","tool_input":{"content":"'"${big:0:200000}"'"}}'
for kind in small large; do
    f=${!kind} n=30
    start=$EPOCHREALTIME
    for ((k = 0; k < n; k++)); do
        "${CLEAN[@]}" SESSIONPULSE_DIR="$WORK/perf" bash "$HOOK" < "$f" > /dev/null 2>&1
    done
    end=$EPOCHREALTIME
    avg=$(( (${end//[.,]/} - ${start//[.,]/}) / n / 1000 ))
    printf 'perf: %-5s payload: %3d ms per run (limit %d ms)\n' "$kind" "$avg" "$max_ms"
    if (( avg > max_ms )); then fail "perf $kind: $avg ms > $max_ms ms"; else pass; fi
done

printf '%d passed, %d failed\n' "$PASSED" "$FAILED"
(( FAILED == 0 ))
