#!/usr/bin/env bash
# SessionPulse status hook.
#
# Claude runs this script for every registered hook event and pipes the event
# JSON to stdin. The script keeps one status file per session in
# <status dir>/sessions/<session_id>.json (format: docs/status-format.md).
#
# Hard rules (docs/LESSONS.md):
#   * Never write to stdout: anything printed there ends up in Claude's context.
#     Only exception: the decision JSON of an opt-in keypad approval
#     (APPROVALS=1, PermissionRequest), written to fd 3.
#   * Always exit 0, even on garbage input or a missing/unwritable directory.
#   * Stay fast: this runs before and after every tool call. Only bash builtins
#     on the hot path; the only external commands are `mv` (always), `mkdir`
#     (first run only), `cat` (only when stdin exceeds the read limit), `grep`
#     (once per turn, on Stop, for the title), `cmd.exe` and maybe `tr` (WSL, first run only)
#     and `date` (bash before 4.2 only, i.e. /bin/bash 3.2 on macOS).
#   * Stay compatible with bash 3.2: on macOS, Claude Desktop runs hooks with
#     /bin/bash (no read -N, no printf %(...)T, no C.UTF-8 locale).
#   * Never search the file system.

exec 3>&1 1>/dev/null 2>/dev/null

# Character-wise string operations (label truncation) need a UTF-8 locale.
# macOS has no C.UTF-8, but always en_US.UTF-8.
if [[ $OSTYPE == darwin* ]]; then export LC_ALL=en_US.UTF-8; else export LC_ALL=C.UTF-8; fi

readonly SP_READ_LIMIT=4000   # characters of stdin to parse; tool_input can be huge
readonly SP_FULL_LIMIT=200000 # upper bound for the rare cases that need the full payload
# Bash 3.2 mangles a quoted "/" in patterns and quoted replacements inside "...": use SL/BSL there.
readonly Q='"' BS=\\ SL=/
readonly BSL="$BS$SL"
SP_OLD_BASH=0   # before 4.2: no printf %(...)T, no \u in printf %b
(( BASH_VERSINFO[0] * 100 + BASH_VERSINFO[1] < 402 )) && SP_OLD_BASH=1

SP_LABELS=1
SP_LABEL_LEN=28
SP_APPROVALS=0
SP_APPROVAL_TIMEOUT=30

# --- helpers -----------------------------------------------------------------
# All helpers return their result in REPLY. Values called *_raw are still
# JSON-escaped; they are copied into the output without decoding.

# Payload string without escaped quotes (ids, names, paths).
sp_pstr() {
    local re='"'"$1"'"[[:space:]]*:[[:space:]]*"([^"]*)"'
    if [[ $2 =~ $re ]]; then REPLY=${BASH_REMATCH[1]}; else REPLY=; fi
}

# Payload string that may contain escapes (prompt, title). Returns at most the
# first $3 characters of the raw value and does not need the closing quote, so
# it works on values cut off by the read limit.
sp_ptext() {
    local re='"'"$1"'"[[:space:]]*:[[:space:]]*"' body='^(([^"\\]|\\.)*)'
    REPLY=
    [[ $2 =~ $re ]] || return 1
    local rest=${2#*"${BASH_REMATCH[0]}"}
    rest=${rest:0:$3}
    [[ $rest =~ $body ]] && REPLY=${BASH_REMATCH[1]}
    return 0
}

# String field from our own (compact) status line.
sp_ostr() {
    local re='"'"$1"'":"(([^"\\]|\\.)*)"'
    if [[ $2 =~ $re ]]; then REPLY=${BASH_REMATCH[1]}; else REPLY=; fi
}

# Unsigned integer field from our own status line.
sp_onum() {
    local re='"'"$1"'":([0-9]+)'
    if [[ $2 =~ $re ]]; then REPLY=${BASH_REMATCH[1]}; else REPLY=0; fi
}

# JSON string content -> plain text on one line.
sp_decode() {
    local s=$1
    s=${s//"$BS$Q"/"$Q"}
    s=${s//"$BSL"/"$SL"}
    (( SP_OLD_BASH )) && [[ $s == *"$BS"u* ]] && sp_unicode_escapes "$s" && s=$REPLY
    printf -v s '%b' "$s"
    REPLY=${s//[[:cntrl:]]/ }
}

# Bash 3.2: \uXXXX (and surrogate pairs) -> UTF-8 as \0nnn octal escapes, which printf %b understands.
sp_unicode_escapes() {
    local s=$1 out='' re='^([^\\]*(\\[^u][^\\]*)*)\\u([0-9A-Fa-f]{4})(.*)$' c lo
    while [[ $s =~ $re ]]; do
        out+=${BASH_REMATCH[1]}
        c=$((16#${BASH_REMATCH[3]}))
        s=${BASH_REMATCH[4]}
        if (( c >= 0xD800 && c < 0xDC00 )) && [[ $s =~ ^\\u([dD][c-fC-F][0-9A-Fa-f]{2})(.*)$ ]]; then
            lo=$((16#${BASH_REMATCH[1]}))
            s=${BASH_REMATCH[2]}
            c=$(( 0x10000 + ((c - 0xD800) << 10) + (lo - 0xDC00) ))
        fi
        if (( c < 0x80 )); then printf -v c '\\0%o' "$c"
        elif (( c < 0x800 )); then printf -v c '\\0%o\\0%o' $((0xC0 | c >> 6)) $((0x80 | (c & 0x3F)))
        elif (( c < 0x10000 )); then printf -v c '\\0%o\\0%o\\0%o' $((0xE0 | c >> 12)) $((0x80 | (c >> 6 & 0x3F))) $((0x80 | (c & 0x3F)))
        else printf -v c '\\0%o\\0%o\\0%o\\0%o' $((0xF0 | c >> 18)) $((0x80 | (c >> 12 & 0x3F))) $((0x80 | (c >> 6 & 0x3F))) $((0x80 | (c & 0x3F)))
        fi
        out+=$c
    done
    REPLY=$out$s
}

# Plain text -> JSON string content.
sp_encode() {
    local s=$1
    s=${s//"$BS"/"$BS$BS"}
    s=${s//"$Q"/"$BS$Q"}
    REPLY=${s//[[:cntrl:]]/ }
}

# Collapse whitespace, trim, cut to $2 characters, encode.
sp_label() {
    local s=$1
    while [[ $s == *"  "* ]]; do s=${s//"  "/" "}; done
    s=${s#" "}
    s=${s:0:$2}
    s=${s%" "}
    sp_encode "$s"
}

# Number of occurrences of $2 in $1.
sp_count() {
    local t=${1//"$2"/}
    REPLY=$(( (${#1} - ${#t}) / ${#2} ))
}

# Agents still running in the background, from `background_tasks` in the Stop and SubagentStop
# payloads; returns 1 if the payload has no such list. Quotes inside JSON strings are escaped, so
# message text can't fake the patterns. Shells, monitors and the like don't count: they may run for
# as long as the session (a dev server, tail -f) and would keep it from ever being done.
sp_background_agents() {
    local re='"background_tasks"[[:space:]]*:[[:space:]]*\[' rest type n=0
    [[ $1 =~ $re ]] || return 1
    rest=${1#*"${BASH_REMATCH[0]}"}
    rest=${rest%%'"session_crons"'*}
    for type in subagent workflow; do
        sp_count "$rest" '"type":"'"$type"'"'; n=$(( n + REPLY ))
        sp_count "$rest" '"type": "'"$type"'"'; n=$(( n + REPLY ))
    done
    REPLY=$n
}

# First JSON object inside the array that starts in $1 (raw text), or empty.
# Byte-wise scan (C locale keeps substring access cheap), strings respected.
sp_first_object() {
    local LC_ALL=C
    local s=${1:0:6000} i c depth=0 in_str=0 esc=0 start=-1
    REPLY=
    for (( i = 0; i < ${#s}; i++ )); do
        c=${s:i:1}
        if (( in_str )); then
            if (( esc )); then esc=0
            elif [[ $c == "$BS" ]]; then esc=1
            elif [[ $c == "$Q" ]]; then in_str=0
            fi
            continue
        fi
        case $c in
            "$Q") in_str=1 ;;
            '{') (( depth == 0 )) && start=$i; (( depth++ )) ;;
            '}') (( depth-- ))
                 if (( depth == 0 && start >= 0 )); then REPLY=${s:start:i-start+1}; return 0; fi ;;
            ']') (( depth == 0 )) && return 1 ;;
        esac
    done
    return 1
}

# Risk of a tool call for the keypad: high (destructive shell command), edit, normal.
sp_risk() {
    local high='(^|[;&|({[:space:]])(rm[[:space:]]+-[a-zA-Z]*[rR]|sudo[[:space:]]|mkfs|dd[[:space:]]+if=|chmod[[:space:]]+-R|chown[[:space:]]+-R|git[[:space:]]+push|git[[:space:]]+reset[[:space:]]+--hard|git[[:space:]]+clean[[:space:]]+-[a-zA-Z]*f|Remove-Item[^;|]*-Recurse|rd[[:space:]]+/s|rmdir[[:space:]]+/s|del[[:space:]]+/[sSqQ]|format[[:space:]]+[a-zA-Z]:|shutdown|reboot|DROP[[:space:]]+(TABLE|DATABASE)|TRUNCATE[[:space:]])|--force|\|[[:space:]]*(ba|z)?sh([[:space:]]|$)|>[[:space:]]*/dev/sd'
    case $1 in
        Bash|PowerShell) if [[ $2 =~ $high ]]; then REPLY=high; else REPLY=normal; fi ;;
        Write|Edit|MultiEdit|NotebookEdit) REPLY=edit ;;
        *) REPLY=normal ;;
    esac
}

# Opt-in keypad approval (APPROVALS=1): publish the request, wait for the
# keypad's decision file, answer Claude on fd 3. No decision ("ask" or
# timeout) means no output, so Claude shows its normal permission dialog.
sp_await_approval() {
    local dir=$1 sid=$2 tool=$3 input=$4 now=$5
    local adir="$dir/approvals" preview='' risk remember=false suggestion=''
    [[ -d $adir ]] || mkdir -p "$adir" || return 0

    case $tool in
        Bash|PowerShell) sp_ptext command "$input" 400 && sp_decode "$REPLY" && preview=$REPLY ;;
        Write|Edit|MultiEdit|NotebookEdit|Read)
            sp_ptext file_path "$input" 400 && sp_decode "$REPLY" && preview=${REPLY##*[/\\]} ;;
        WebFetch) sp_ptext url "$input" 200 && sp_decode "$REPLY" && preview=$REPLY ;;
        WebSearch) sp_ptext query "$input" 200 && sp_decode "$REPLY" && preview=$REPLY ;;
    esac
    sp_risk "$tool" "$preview"; risk=$REPLY
    sp_label "$preview" 160; preview=$REPLY

    local sre='"permission_suggestions"[[:space:]]*:[[:space:]]*\['
    if [[ $input =~ $sre ]]; then
        sp_first_object "${input#*"${BASH_REMATCH[0]}"}" && suggestion=$REPLY && remember=true
    fi

    local short=${tool##*__}
    local req="${sid:0:40}-$now-$$-$RANDOM$RANDOM"
    local reqfile="$adir/$req.json" decfile="$adir/$req.decision" tmp="$adir/.$req.tmp"
    local expires=$(( now + SP_APPROVAL_TIMEOUT ))
    # Also clean up if Claude stops the hook (paths expanded now: locals are gone at exit).
    # shellcheck disable=SC2064 # expanding now is the point
    trap "rm -f $(printf '%q %q' "$reqfile" "$decfile")" EXIT
    printf '%s\n' '{"v":1,"id":"'"$req"'","session":"'"$sid"'","tool":"'"${short:0:40}"'","preview":"'"$preview"'","risk":"'"$risk"'","remember":'"$remember"',"created":'"$now"',"expires":'"$expires"'}' > "$tmp" || return 0
    mv -f "$tmp" "$reqfile" || return 0

    local decision='' k
    for (( k = 0; k < SP_APPROVAL_TIMEOUT * 4; k++ )); do
        if [[ -f $decfile ]]; then
            IFS= read -r decision < "$decfile" || true
            break
        fi
        sleep 0.25 || break
    done

    rm -f "$reqfile" "$decfile"

    local out='' dest='"destination"[[:space:]]*:[[:space:]]*"[^"]*"'
    case $decision in
        allow) out='{"behavior":"allow"}' ;;
        session|always)
            if [[ -n $suggestion ]]; then
                if [[ $decision == session && $suggestion =~ $dest ]]; then
                    suggestion=${suggestion/"${BASH_REMATCH[0]}"/'"destination":"session"'}
                fi
                out='{"behavior":"allow","updatedPermissions":['"$suggestion"']}'
            else
                out='{"behavior":"allow"}'
            fi
            ;;
        deny) out='{"behavior":"deny","message":"Denied on the SessionPulse keypad."}' ;;
    esac
    [[ -n $out ]] && printf '%s\n' '{"hookSpecificOutput":{"hookEventName":"PermissionRequest","decision":'"$out"'}}' >&3
    return 0
}

# WSL: the keypad plugin runs on Windows, so write to <Windows profile>/.sessionpulse. Linux and
# Windows user names may differ, and asking Windows costs a cmd.exe start (~50 ms), so the answer is
# cached (deleting the cache file makes the hook ask again).
sp_wsl_dir() {
    local cache="${XDG_CACHE_HOME:-$HOME/.cache}/sessionpulse/windows-dir" d='' w
    [[ -f $cache ]] && { IFS= read -r d < "$cache" || true; }
    if [[ -z $d ]]; then
        w=$(cmd.exe /c 'echo %USERPROFILE%' 2>/dev/null) || w=''
        w=${w%%$'\r'*}
        [[ $w =~ ^([A-Za-z]):[\\/](.*)$ ]] || return 1
        if command -v wslpath > /dev/null; then
            d=$(wslpath -u "$w") || return 1
        else
            d="/mnt/$(printf '%s' "${BASH_REMATCH[1]}" | tr '[:upper:]' '[:lower:]')/${BASH_REMATCH[2]//"$BS"/$SL}"   # default automount root
        fi
        d="${d%/}/.sessionpulse"
        mkdir -p "${cache%/*}" && printf '%s\n' "$d" > "$cache"
    fi
    REPLY=$d
}

# Title from the session's transcript (Claude Code CLI sessions have no Claude Desktop metadata):
# the last custom title (/rename), else the last generated one, which is derived from the
# conversation and therefore follows LABELS. Only the title lines are read, with one grep.
sp_transcript_title() {
    local found line custom='' ai=''
    local re_c='^"customTitle":"(.*)"$' re_a='^"aiTitle":"(.*)"$'
    [[ -f $1 ]] || return 1
    # A JSON string: characters other than quote and backslash, or a backslash escape ([\] = backslash).
    found=$(grep -o -E '"(customTitle|aiTitle)":"([^"\]|[\].)*"' -- "$1" 2>/dev/null) || return 1
    while IFS= read -r line; do
        if [[ $line =~ $re_c ]]; then custom=${BASH_REMATCH[1]}
        elif [[ $line =~ $re_a ]]; then ai=${BASH_REMATCH[1]}
        fi
    done <<< "$found"
    (( SP_LABELS )) || ai=''
    REPLY=${custom:-$ai}
    [[ -n $REPLY ]]
}

sp_status_dir() {
    local d=${SESSIONPULSE_DIR:-}
    if [[ -z $d ]]; then
        if [[ -n ${LOCALAPPDATA:-} ]]; then
            # Windows (Git Bash). Not below AppData: the Store version of Claude Desktop redirects
            # new files there into its package folder, out of the keypad plugin's sight (LESSONS).
            d="${USERPROFILE:-$HOME}/.sessionpulse"
        elif [[ -n ${WSL_DISTRO_NAME:-} ]] && sp_wsl_dir; then
            d=$REPLY
        elif [[ $OSTYPE == darwin* ]]; then
            d="$HOME/Library/Application Support/SessionPulse"
        else
            d="${XDG_STATE_HOME:-$HOME/.local/state}/sessionpulse"
        fi
    fi
    # Git Bash accepts C:/... paths, so no cygpath process is needed.
    REPLY=${d//"$BS"/"/"}
}

sp_read_config() {
    local k v
    [[ -f $1 ]] || return 0
    while IFS='=' read -r k v || [[ -n $k ]]; do
        v=${v%$'\r'}
        case $k in
            LABELS)    [[ $v =~ ^[01]$ ]] && SP_LABELS=$v ;;
            LABEL_LEN) [[ $v =~ ^[0-9]{1,3}$ ]] && (( v > 0 && v <= 200 )) && SP_LABEL_LEN=$v ;;
            APPROVALS) [[ $v =~ ^[01]$ ]] && SP_APPROVALS=$v ;;
            APPROVAL_TIMEOUT) [[ $v =~ ^[0-9]{1,3}$ ]] && (( v >= 5 && v <= 280 )) && SP_APPROVAL_TIMEOUT=$v ;;
        esac
    done < "$1"
}

# --- main --------------------------------------------------------------------

sp_main() {
    local input='' truncated=0
    # -d '' -n instead of -N (bash 3.2). read succeeds only if it stopped at the limit, not at EOF.
    IFS= read -r -d '' -n "$SP_READ_LIMIT" input && truncated=1
    [[ -n $input ]] || return 0

    sp_pstr session_id "$input"
    local id=${REPLY//[^A-Za-z0-9_-]/}
    id=${id:0:128}
    [[ -n $id ]] || return 0

    sp_pstr hook_event_name "$input"
    local event=${REPLY//[^A-Za-z]/}
    [[ -n $event ]] || return 0

    sp_pstr tool_name "$input"
    local tool_name=${REPLY//[^A-Za-z0-9_.:-]/}

    # A few cases need the whole payload; everything else discards the rest so
    # the writer never blocks on a full pipe.
    if (( truncated )); then
        if [[ ($event == PreToolUse && $tool_name == TodoWrite) || $event == PostToolUseFailure || $event == PermissionRequest || $event == Stop || $event == SubagentStop ]]; then
            local rest
            rest=$(cat)
            input+=${rest:0:$SP_FULL_LIMIT}
        else
            cat >/dev/null
        fi
    fi

    local now
    if [[ ${SESSIONPULSE_NOW:-} =~ ^[0-9]+$ ]]; then now=$SESSIONPULSE_NOW; elif (( ! SP_OLD_BASH )); then printf -v now '%(%s)T' -1; else now=$(date +%s); fi

    sp_status_dir
    local dir=$REPLY
    sp_read_config "$dir/sessionpulse.conf"
    local sdir="$dir/sessions"
    if [[ ! -d $sdir ]]; then
        mkdir -p "$sdir" || return 0
    fi
    local file="$sdir/$id.json"

    # Previous state of this session.
    local prev=''
    if [[ -f $file ]]; then
        IFS= read -r prev < "$file" || [[ -n $prev ]] || prev=''
    fi
    local src label sched title state detail tool started turn activity prompts tools tdone ttotal agents
    sp_ostr src "$prev";        src=$REPLY
    sp_ostr label "$prev";      label=$REPLY
    sp_ostr sched "$prev";      sched=$REPLY
    sp_ostr title "$prev";      title=$REPLY
    sp_ostr state "$prev";      state=${REPLY:-idle}
    sp_ostr detail "$prev";     detail=${REPLY:-idle}
    sp_ostr tool "$prev";       tool=$REPLY
    sp_onum started "$prev";    started=$REPLY
    sp_onum turnStarted "$prev"; turn=$REPLY
    sp_onum activity "$prev";   activity=$REPLY
    sp_onum prompts "$prev";    prompts=$REPLY
    sp_onum tools "$prev";      tools=$REPLY
    sp_onum tasksDone "$prev";  tdone=$REPLY
    sp_onum tasksTotal "$prev"; ttotal=$REPLY
    sp_onum agents "$prev";     agents=$REPLY
    sp_ostr waitAgent "$prev";  local wait_agent=$REPLY

    # Which agent this event comes from; empty = the main agent. Only looked for in front of
    # tool_input, where a tool's own arguments could have a field of the same name.
    sp_pstr agent_id "${input%%'"tool_input"'*}"
    local agent=${REPLY//[^A-Za-z0-9_-]/}
    agent=${agent:0:64}
    (( started > 0 )) || started=$now

    local cwd_raw
    sp_pstr cwd "$input"; cwd_raw=$REPLY
    if [[ -z $src ]]; then
        if [[ $cwd_raw == *local-agent-mode-sessions* ]]; then src=cowork; else src=code; fi
    fi

    # --- event -> state (docs/status-format.md) ---
    local is_activity=1 prev_state=$state transition=$event
    # A waiting session stays waiting until the agent it waits for moves on: tool calls of the
    # others (a subagent working on while the main agent asks a question) don't end the wait.
    # An interrupt does, it stops everything.
    if [[ $state == waiting && $agent != "$wait_agent" ]]; then
        local int_re='"is_interrupt"[[:space:]]*:[[:space:]]*true'
        case $event in
            PreToolUse|PermissionRequest|PostToolUse) transition=OtherAgent ;;
            PostToolUseFailure) [[ $input =~ $int_re ]] || transition=OtherAgent ;;
        esac
    fi
    case $transition in
        OtherAgent)
            [[ $event == PreToolUse ]] && tools=$(( tools + 1 ))
            ;;
        SessionStart)
            # Cowork also sends this when a session is merely opened or
            # switched to, so an existing state is kept.
            is_activity=0
            [[ -n $prev ]] || { state=idle; detail=idle; }
            ;;
        UserPromptSubmit)
            if (( prompts == 0 )) && sp_ptext prompt "$input" 600; then
                sp_decode "$REPLY"
                local first=$REPLY
                local sre="^[[:space:]]*<scheduled-task[[:space:]]+name=['\"]([^'\"]*)['\"]"
                if [[ $first =~ $sre ]]; then
                    sp_label "${BASH_REMATCH[1]}" 64; sched=$REPLY
                    label=$sched
                elif [[ -z $label && $src == cowork ]] && (( SP_LABELS )); then
                    sp_label "$first" "$SP_LABEL_LEN"; label=$REPLY
                fi
            fi
            state=working; detail=thinking; tool=''
            turn=$now
            prompts=$(( prompts + 1 ))
            ;;
        PreToolUse)
            tools=$(( tools + 1 ))
            case $tool_name in
                AskUserQuestion) state=waiting; detail=question; tool='' ;;
                ExitPlanMode)    state=waiting; detail=plan; tool='' ;;
                *)
                    state=working; detail=tool
                    tool=${tool_name##*__}
                    tool=${tool:0:24}
                    if [[ $tool_name == TodoWrite ]] && (( ${#input} <= SP_FULL_LIMIT )); then
                        local n1 n2
                        sp_count "$input" '"status":"completed"'; n1=$REPLY
                        sp_count "$input" '"status": "completed"'; tdone=$(( n1 + REPLY ))
                        sp_count "$input" '"status":"'; n2=$REPLY
                        sp_count "$input" '"status": "'; ttotal=$(( n2 + REPLY ))
                    fi
                    ;;
            esac
            ;;
        PermissionRequest)
            state=waiting; detail=permission
            tool=${tool_name##*__}; tool=${tool:0:24}
            ;;
        PostToolUse)
            state=working; detail=thinking; tool=''
            ;;
        PostToolUseFailure)
            local ire='"is_interrupt"[[:space:]]*:[[:space:]]*true'
            if [[ $input =~ $ire ]]; then
                state=idle; detail=interrupted
            else
                state=working; detail=thinking
            fi
            tool=''
            ;;
        Notification)
            sp_pstr notification_type "$input"
            case $REPLY in
                permission_prompt) state=waiting; detail=permission ;;
                elicitation_dialog|elicitation_url_dialog|agent_needs_input) state=waiting; detail=input ;;
                idle_prompt)
                    # A timer ("idle for 60 s"), not activity. Says nothing about background agents.
                    is_activity=0
                    [[ $state == working ]] && (( agents == 0 )) && { state='done'; detail='done'; tool=''; }
                    ;;
                *) is_activity=0 ;;
            esac
            ;;
        SubagentStop)
            # The agent this session was waiting for is gone, so nothing waits for it any more.
            [[ $state == waiting && -n $agent && $agent == "$wait_agent" ]] && state=working
            [[ $state == working ]] && { detail=subagent; tool=''; }
            if sp_background_agents "$input"; then
                agents=$REPLY
                # The list still shows the agent that is just stopping as running.
                local self='"id"[[:space:]]*:[[:space:]]*"'"$agent"'"'
                [[ -n $agent && $input =~ $self ]] && (( agents > 0 )) && agents=$(( agents - 1 ))
            fi
            ;;
        TaskCreated)
            ttotal=$(( ttotal + 1 ))
            ;;
        TaskCompleted)
            tdone=$(( tdone + 1 ))
            (( tdone <= ttotal )) || ttotal=$tdone
            ;;
        Stop)
            # With agents still running the main agent only pauses; they wake it, and it stops again.
            sp_background_agents "$input" && agents=$REPLY || agents=0
            if (( agents > 0 )); then
                # A running subagent may still be waiting for the user.
                [[ $state == waiting && -n $wait_agent ]] || { state=working; detail=subagent; tool=''; }
            else
                state='done'; detail='done'; tool=''
            fi
            ;;
        StopFailure)
            sp_pstr error "$input"
            state='done'; tool=''; agents=0
            if [[ $REPLY == rate_limit ]]; then detail=limit; else detail=error; fi
            ;;
        PreCompact)
            state=working; detail=compacting; tool=''
            ;;
        PostCompact)
            # A manual /compact ends there; an automatic one continues the turn.
            sp_pstr trigger "$input"
            if [[ $REPLY == manual ]]; then state=idle; detail=idle; else state=working; detail=thinking; fi
            tool=''
            ;;
        SessionEnd)
            # Cowork sends this after every answer; readers treat it like done.
            is_activity=0
            state=ended; detail=ended; tool=''; agents=0
            ;;
        *)
            is_activity=0
            ;;
    esac
    # Readers sort waiting sessions by how long they have been waiting, so a wait that goes on isn't activity.
    [[ $prev_state == waiting && $state == waiting ]] && is_activity=0
    (( is_activity )) && activity=$now
    if [[ $state != waiting ]]; then wait_agent=''
    elif [[ $prev_state != waiting ]]; then wait_agent=$agent
    fi

    # --- label ---
    local title_raw
    if sp_ptext session_title "$input" 600 && [[ -n $REPLY ]]; then
        title_raw=$REPLY
        sp_decode "$title_raw"
        sp_label "$REPLY" "$SP_LABEL_LEN"; label=$REPLY
    elif [[ -z $label && $src == code && -n $cwd_raw ]]; then
        sp_decode "$cwd_raw"
        local p=${REPLY%%[/\\]}
        p=${p%[/\\]}
        p=${p##*[/\\]}
        sp_label "$p" "$SP_LABEL_LEN"; label=$REPLY
    fi

    # --- title: once per turn, it changes rarely and needs a grep ---
    if [[ $event == Stop ]] && sp_ptext transcript_path "$input" 1000 && [[ -n $REPLY ]]; then
        sp_decode "$REPLY"
        if sp_transcript_title "${REPLY//"$BS"/$SL}"; then
            sp_decode "$REPLY"
            sp_label "$REPLY" 80; title=$REPLY
        fi
    fi

    # --- where the session runs: lets the plugin open the right window ---
    local host=${CLAUDE_CODE_ENTRYPOINT:-} pid='' wsl=${WSL_DISTRO_NAME:-}
    host=${host//[^A-Za-z0-9_.-]/}; host=${host:0:32}
    wsl=${wsl//[^A-Za-z0-9_.-]/}; wsl=${wsl:0:64}
    # A Linux PID inside WSL means nothing to the plugin on Windows.
    [[ -z $wsl && ${CLAUDE_PID:-} =~ ^[0-9]{1,10}$ ]] && pid=$CLAUDE_PID

    # --- write atomically ---
    local out='{"v":2,"id":"'$id'","src":"'$src'","label":"'$label'"'
    [[ -n $sched ]] && out+=',"sched":"'$sched'"'
    [[ -n $title ]] && out+=',"title":"'$title'"'
    out+=',"state":"'$state'","detail":"'$detail'"'
    [[ -n $tool ]] && out+=',"tool":"'$tool'"'
    out+=',"event":"'$event'","started":'$started',"turnStarted":'$turn',"activity":'$activity
    out+=',"updated":'$now',"prompts":'$prompts',"tools":'$tools',"tasksDone":'$tdone',"tasksTotal":'$ttotal
    (( agents > 0 )) && out+=',"agents":'$agents
    [[ -n $wait_agent ]] && out+=',"waitAgent":"'$wait_agent'"'
    [[ -n $host ]] && out+=',"host":"'$host'"'
    [[ -n $pid ]] && out+=',"pid":'$pid
    [[ -n $wsl ]] && out+=',"wsl":"'$wsl'"'
    out+='}'

    local tmp="$sdir/.$id.$$.tmp"
    printf '%s\n' "$out" > "$tmp" || return 0
    # A reader holding the file open can make the rename fail on Windows; retry once.
    mv -f "$tmp" "$file" || mv -f "$tmp" "$file"

    # Keep a copy of the status line helper at a stable path (docs/configuration.md).
    if [[ $event == SessionStart ]]; then
        local helper="${BASH_SOURCE[0]%/*}/../../statusline/statusline.sh"
        if [[ -f $helper && ( ! -f $dir/statusline.sh || $dir/statusline.sh -ot $helper ) ]]; then
            cp -f "$helper" "$dir/statusline.sh"
        fi
    fi

    if [[ $event == PermissionRequest ]] && (( SP_APPROVALS )) && [[ $tool_name != AskUserQuestion && $tool_name != ExitPlanMode ]]; then
        sp_await_approval "$dir" "$id" "$tool_name" "$input" "$now"
    fi
    return 0
}

sp_main
exit 0
