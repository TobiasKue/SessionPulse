#!/usr/bin/env bash
# SessionPulse status line helper (optional, docs/configuration.md).
#
# Claude Code runs the configured status line command with session data as JSON
# on stdin, including the subscription usage windows (rate_limits.five_hour,
# rate_limits.seven_day, rate_limits.spend_limit; code.claude.com/docs/en/statusline).
# This script stores them in <status dir>/usage.json for the keypad and prints a
# short status line, or passes stdin on to the command in STATUSLINE_CHAIN
# (sessionpulse.conf) so an existing status line keeps working.
#
# The SessionPulse hook copies this file to <status dir>/statusline.sh on
# every SessionStart, so settings can point at a stable path.

exec 2>/dev/null
export LC_ALL=C.UTF-8
readonly BS=\\

input=''
IFS= read -r -d '' -N 65536 input

# Same default as the hook (sp_status_dir in hooks/scripts/status.sh).
dir=${SESSIONPULSE_DIR:-}
if [[ -z $dir ]]; then
    wsl_cache="${XDG_CACHE_HOME:-$HOME/.cache}/sessionpulse/windows-dir"   # written by the hook
    if [[ -n ${LOCALAPPDATA:-} ]]; then dir="${USERPROFILE:-$HOME}/.sessionpulse"
    elif [[ -n ${WSL_DISTRO_NAME:-} && -s $wsl_cache ]]; then IFS= read -r dir < "$wsl_cache"
    elif [[ $OSTYPE == darwin* ]]; then dir="$HOME/Library/Application Support/SessionPulse"
    else dir="${XDG_STATE_HOME:-$HOME/.local/state}/sessionpulse"
    fi
fi
dir=${dir//"$BS"/"/"}

chain=''
if [[ -f $dir/sessionpulse.conf ]]; then
    while IFS='=' read -r key value || [[ -n $key ]]; do
        [[ $key == STATUSLINE_CHAIN ]] && chain=${value%$'\r'}
    done < "$dir/sessionpulse.conf"
fi

# usage_window <json name> -> "pct resetsAt" or nothing
usage_window() {
    local re='"'"$1"'"[[:space:]]*:[[:space:]]*\{([^}]*)\}' pct='' reset=''
    [[ $input =~ $re ]] || return 1
    local body=${BASH_REMATCH[1]}
    local pre='"used_percentage"[[:space:]]*:[[:space:]]*([0-9]+(\.[0-9]+)?)'
    local rre='"resets_at"[[:space:]]*:[[:space:]]*([0-9]+)'
    [[ $body =~ $pre ]] && pct=${BASH_REMATCH[1]}
    [[ $body =~ $rre ]] && reset=${BASH_REMATCH[1]}
    [[ -n $pct ]] || return 1
    REPLY="$pct ${reset:-0}"
}

now=${SESSIONPULSE_NOW:-}
[[ $now =~ ^[0-9]+$ ]] || printf -v now '%(%s)T' -1

json='' line=''
for pair in five_hour:fiveHour:5h seven_day:sevenDay:7d spend_limit:spend:\$; do
    IFS=: read -r name field short <<< "$pair"
    if usage_window "$name"; then
        read -r pct reset <<< "$REPLY"
        json+=',"'"$field"'":{"pct":'"$pct"',"resetsAt":'"$reset"'}'
        line+="${line:+ · }$short ${pct%%.*}%"
    fi
done

if [[ -n $json ]]; then
    if [[ -d $dir ]] || mkdir -p "$dir"; then
        printf '%s\n' '{"v":1,"updated":'"$now"''"$json"'}' > "$dir/.usage.$$.tmp" &&
            mv -f "$dir/.usage.$$.tmp" "$dir/usage.json"
    fi
fi

if [[ -n $chain ]]; then
    printf '%s' "$input" | bash -c "$chain"
elif [[ -n $line ]]; then
    printf '%s\n' "$line"
fi
exit 0
