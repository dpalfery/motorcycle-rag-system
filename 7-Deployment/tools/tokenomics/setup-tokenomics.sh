#!/usr/bin/env bash
###############################################################################
# setup-tokenomics.sh
#
# Bootstrap the MotorcycleRAG agent-development toolchain at the project level.
#
#   1. CodeGraph   — vendored project-level install (npm ci) + index build.
#                    This is the only tool with real project-level state
#                    (the .codegraph/ knowledge graph).
#   2. RTK         — detect-only. A global, stateless Rust CLI proxy with no
#                    project-level form; verified on PATH, never auto-installed.
#   3. Kyber-Weave — detect-only. A global governance binary; when present it
#                    runs docs validate + docs drift against the freshly built
#                    CodeGraph index (its "indexing" step).
#
# Idempotent: safe to re-run. Re-runs sync the index incrementally; pass
# --rebuild to force a full reindex from scratch.
#
# Exit status: 0 when CodeGraph installs and indexes successfully (RTK and
# Kyber-Weave are advisory); non-zero only on a CodeGraph failure.
#
# References:
#   - 6-Docs/reference/kyber-weave.md
#   - kyber-weave.yml (host overrides)
#   - CLAUDE.md (RTK usage policy)
#   - 7-Deployment/tools/codegraph/package.json (pinned CodeGraph version)
###############################################################################
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# Script lives at <repo-root>/7-Deployment/tools/tokenomics/ — three levels up.
REPO_ROOT="$(cd "$SCRIPT_DIR/../../.." && pwd)"

# --- Tool locations / pins ---------------------------------------------------
CG_DIR="$REPO_ROOT/7-Deployment/tools/codegraph"
CG_BIN="$CG_DIR/node_modules/.bin/codegraph"
CG_PINNED="1.5.0"          # @colbymchenry/codegraph, from tools/codegraph/package.json
RTK_MIN="0.42"             # Rust Token Killer, recommended floor (see CLAUDE.md)
KW_PINNED="0.1.1"          # @dpalfery/kyber-weave, matches CI pin
INDEX_DB="$REPO_ROOT/.codegraph/codegraph.db"

# --- Flags -------------------------------------------------------------------
REBUILD=0

usage() {
    cat <<'EOF'
Usage: setup-tokenomics.sh [--rebuild] [-h|--help]

  --rebuild, -r   Force a full CodeGraph reindex from scratch (codegraph index)
                  instead of the default incremental sync.
  -h, --help      Show this help and exit.

Default: install/refresh the vendored CodeGraph, build or sync its index,
verify RTK and Kyber-Weave on PATH, and run Kyber-Weave docs checks when
the binary is available.
EOF
}

for arg in "$@"; do
    case "$arg" in
        --rebuild|-r) REBUILD=1 ;;
        -h|--help)    usage; exit 0 ;;
        *) echo "Unknown option: $arg" >&2; usage >&2; exit 2 ;;
    esac
done

# --- Output helpers (colour only on a TTY) -----------------------------------
if [ -t 1 ]; then
    C_RESET=$'\033[0m'; C_BOLD=$'\033[1m'; C_DIM=$'\033[2m'
    C_GREEN=$'\033[32m'; C_YELLOW=$'\033[33m'; C_RED=$'\033[31m'; C_CYAN=$'\033[36m'
else
    C_RESET=''; C_BOLD=''; C_DIM=''; C_GREEN=''; C_YELLOW=''; C_RED=''; C_CYAN=''
fi

section() { printf '\n%s== %s ==%s\n' "$C_BOLD$C_CYAN" "$1" "$C_RESET"; }
info()    { printf '%s[info]%s  %s\n' "$C_DIM" "$C_RESET" "$*"; }
ok()      { printf '%s[ ok ]%s  %s\n' "$C_GREEN" "$C_RESET" "$*"; }
warn()    { printf '%s[warn]%s  %s\n' "$C_YELLOW" "$C_RESET" "$*" >&2; }
die()     { printf '%s[fail]%s  %s\n' "$C_RED" "$C_RESET" "$*" >&2; exit 1; }

# version_ge A B -> success (0) when A >= B, comparing dotted numerics.
# An optional leading 'v' and any pre-release/build suffix ('-foo', '+build')
# are stripped from BOTH operands before comparison, so "0.42.4-pre" is
# treated numerically as "0.42.4" (pure dotted numerics).
version_ge() {
    local IFS=.
    local i
    local v1="${1#v}" v2="${2#v}"
    v1="${v1%%[-+]*}"
    v2="${v2%%[-+]*}"
    local a=($v1) b=($v2)
    for ((i = 0; i < ${#a[@]} || i < ${#b[@]}; i++)); do
        local x=${a[i]:-0} y=${b[i]:-0}
        if (( 10#$x > 10#$y )); then return 0
        elif (( 10#$x < 10#$y )); then return 1; fi
    done
    return 0
}

# Detect a running CodeGraph daemon. Returns 0 (live) when either
# daemon.pid or daemon.sock is present; the caller is expected to prompt
# the user before `npm ci` replaces the binary the daemon is running from.
is_codegraph_daemon_running() {
    local pid_file="$REPO_ROOT/.codegraph/daemon.pid"
    local sock_file="$REPO_ROOT/.codegraph/daemon.sock"
    if [ ! -f "$pid_file" ] && [ ! -S "$sock_file" ]; then
        return 1
    fi
    local pid=""
    [ -f "$pid_file" ] && pid="$(cat "$pid_file" 2>/dev/null || true)"
    warn "Detected a live CodeGraph daemon (pid=${pid:-?}, socket=$sock_file)."
    warn "Reinstalling replaces the vendored binary under a running daemon."
    warn "Stop it first (see 'codegraph daemon --help')."
    return 0
}

# Single-instance guard for install + init/sync/index. Two concurrent
# invocations would race on .codegraph/codegraph.db. Implemented with an
# atomic mkdir on a lock directory — portable across macOS bash 3.2 (no
# flock(1) dependency). A recorded PID lets us steal the lock if the
# previous holder died without releasing it.
CG_LOCK_DIR=""

acquire_codegraph_lock() {
    local lock_dir="$REPO_ROOT/.codegraph/.setup-tokenomics.lock"
    local pid_file="$lock_dir/pid"
    if mkdir "$lock_dir" 2>/dev/null; then
        if ! echo $$ > "$pid_file" 2>/dev/null; then
            rm -rf "$lock_dir"
            die "Could not write pid file under $lock_dir."
        fi
        CG_LOCK_DIR="$lock_dir"
        trap '[ -n "$CG_LOCK_DIR" ] && rm -rf "$CG_LOCK_DIR"' EXIT
        return 0
    fi
    # Lock directory exists — try stale-lock recovery before giving up.
    if [ -f "$pid_file" ]; then
        local old_pid
        old_pid="$(cat "$pid_file" 2>/dev/null || true)"
        if [ -n "$old_pid" ] && ! kill -0 "$old_pid" 2>/dev/null; then
            warn "Stale lock at $lock_dir (pid $old_pid not alive) — stealing."
            rm -rf "$lock_dir"
            if mkdir "$lock_dir" 2>/dev/null && echo $$ > "$pid_file" 2>/dev/null; then
                CG_LOCK_DIR="$lock_dir"
                trap '[ -n "$CG_LOCK_DIR" ] && rm -rf "$CG_LOCK_DIR"' EXIT
                return 0
            fi
            die "Failed to steal stale lock at $lock_dir."
        fi
        warn "Another setup-tokenomics.sh is running (pid=${old_pid:-?}, lock=$lock_dir)."
        die "Refusing to race with another setup-tokenomics.sh."
    fi
    die "Lock directory $lock_dir exists but no pid file — refusing to proceed."
}

# --- 1. CodeGraph: project-level install + index -----------------------------
bootstrap_codegraph() {
    section "CodeGraph (project-level, pinned $CG_PINNED)"

    command -v npm >/dev/null 2>&1 || die "npm not found on PATH — required to install CodeGraph."
    command -v node >/dev/null 2>&1 || die "node not found on PATH — required to run CodeGraph."
    [ -f "$CG_DIR/package.json" ] || die "CodeGraph manifest not found: $CG_DIR/package.json"

    # Hold the lock for the whole bootstrap (install + index) so a second
    # invocation cannot race on .codegraph/codegraph.db.
    acquire_codegraph_lock

    local need_install=0
    # Normalize an optional leading 'v' (e.g. upstream "v1.5.0") so format
    # drift does not force a spurious reinstall every run.
    local detected_ver="$("$CG_BIN" --version 2>/dev/null || true)"
    detected_ver="${detected_ver#v}"
    if [ ! -x "$CG_BIN" ]; then
        need_install=1
        info "Vendored binary missing — installing dependencies."
    elif [ "$detected_ver" != "${CG_PINNED#v}" ]; then
        need_install=1
        info "Vendored version (${detected_ver:-?}) differs from pin ($CG_PINNED) — reinstalling."
    else
        ok "Vendored CodeGraph $detected_ver already installed (pin ${CG_PINNED#v})."
    fi

    if [ "$need_install" -eq 1 ]; then
        # npm ci removes and rebuilds node_modules/, which can pull the
        # binary out from under a running CodeGraph daemon. Detect the
        # daemon and ask before proceeding; do NOT auto-kill it.
        if is_codegraph_daemon_running; then
            if [ -t 0 ]; then
                local reply=""
                if ! read -r -p "Proceed with reinstall despite live daemon? [y/N] " reply; then
                    die "Aborted — input closed while prompting about live CodeGraph daemon."
                fi
                case "${reply:-}" in
                    [yY]|[yY][eE][sS]) info "Proceeding (live CodeGraph daemon confirmed)." ;;
                    *)                   die "Aborted by user — live CodeGraph daemon." ;;
                esac
            else
                die "Live CodeGraph daemon detected; rerun interactively to confirm."
            fi
        fi
        # npm ci for reproducible installs from the lockfile; fall back to
        # npm install when node_modules is in a partial state npm ci rejects.
        (cd "$CG_DIR" && npm ci) || (cd "$CG_DIR" && npm install) \
            || die "Failed to install CodeGraph dependencies in $CG_DIR."
        [ -x "$CG_BIN" ] || die "Install finished but $CG_BIN is not executable."
        ok "Installed CodeGraph $("$CG_BIN" --version)."
    fi

    # Build / refresh the index. init on first run; sync (or index with
    # --rebuild) thereafter. The vendored binary is used explicitly so the
    # pinned version — not any global codegraph — drives indexing.
    if [ ! -f "$INDEX_DB" ]; then
        if [ "$REBUILD" -eq 1 ]; then
            info "--rebuild requested but no index at .codegraph/ — running initial build (codegraph init), equivalent to --rebuild."
        else
            info "No index at .codegraph/ — running initial build (codegraph init)."
        fi
        "$CG_BIN" init "$REPO_ROOT" || die "codegraph init failed."
        ok "Initial index built."
    elif [ "$REBUILD" -eq 1 ]; then
        info "Rebuilding the full index from scratch (codegraph index)."
        "$CG_BIN" index "$REPO_ROOT" || die "codegraph index failed."
        ok "Full reindex complete."
    else
        info "Index exists — syncing incrementally (codegraph sync)."
        "$CG_BIN" sync "$REPO_ROOT" || die "codegraph sync failed."
        ok "Index synced."
    fi

    # Status summary.
    "$CG_BIN" status "$REPO_ROOT" || warn "codegraph status returned non-zero."
}

# --- 2. RTK: detect-only -----------------------------------------------------
detect_rtk() {
    section "RTK — Rust Token Killer (detect-only)"

    if ! command -v rtk >/dev/null 2>&1; then
        warn "rtk not found on PATH."
        warn "  Install:  brew install rtk        (macOS)"
        warn "         or cargo install brokk-rtk (other platforms)"
        warn "  See CLAUDE.md for the rtk-prefix usage policy."
        return 0
    fi

    # Guarded: a failing pipeline (e.g. --version unsupported) must not abort
    # the script under `set -e`/`pipefail`. `|| true` lets ver be empty instead.
    local ver
    ver=$(rtk --version 2>/dev/null | awk '{print $NF}' | head -1) || ver=""
    if [ -z "$ver" ]; then
        warn "rtk found but its version could not be read."
        return 0
    fi

    if version_ge "$ver" "$RTK_MIN"; then
        ok "rtk $ver (>= recommended $RTK_MIN) on PATH."
    else
        warn "rtk $ver is older than the recommended $RTK_MIN."
        warn "  Upgrade:  brew upgrade rtk"
    fi
}

# --- 3. Kyber-Weave: detect-only, then docs checks ---------------------------
detect_kyber_weave() {
    section "Kyber-Weave (detect-only, pinned $KW_PINNED)"

    # The kyber-weave CLI is built on Spectre.Console and exposes no version
    # flag or subcommand, so only presence is checked here. The pin (0.1.1)
    # is enforced by the CI install action, not by the binary itself.
    command -v kyber-weave >/dev/null 2>&1 || {
        warn "kyber-weave not found on PATH — skipping docs validate/drift."
        warn "  Install one of:"
        warn "    npm i -g @dpalfery/kyber-weave@$KW_PINNED   (matches CI pin)"
        warn "    brew install dpalfery/kyber-weave/kyber-weave"
        warn "    https://github.com/dpalfery/kyber-weave/releases"
        return 0
    }
    ok "kyber-weave on PATH (CI pin: $KW_PINNED)."

    # These read the .codegraph index built above + the 6-Docs corpus. They
    # are diagnostic governance checks, not build gates: report, do not abort.
    info "Running kyber-weave docs validate (frontmatter schema)..."
    (cd "$REPO_ROOT" && kyber-weave docs validate --format table) \
        || warn "kyber-weave docs validate reported issues."

    info "Running kyber-weave docs drift (code-entity resolution)..."
    (cd "$REPO_ROOT" && kyber-weave docs drift --format table) \
        || warn "kyber-weave docs drift reported issues (see 6-Docs/reference/kyber-weave.md)."
}

# --- Summary -----------------------------------------------------------------
summary() {
    section "Done"
    info "CodeGraph index: $INDEX_DB"
    info "RTK and Kyber-Weave are global binaries; re-run this script after installing either."
}

main() {
    printf '%sMotorcycleRAG dev-tooling bootstrap%s\n' "$C_BOLD" "$C_RESET"
    info "Repository: $REPO_ROOT"
    bootstrap_codegraph   # critical path — may exit non-zero
    detect_rtk            # advisory
    detect_kyber_weave    # advisory
    summary
}

main "$@"
