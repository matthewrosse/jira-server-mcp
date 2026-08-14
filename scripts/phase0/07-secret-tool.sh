#!/usr/bin/env bash
# Question 6: does secret-tool behave identically across keyring backends?
#
# The Linux credential store shells out to secret-tool, so what matters is not
# whether a backend "works" but whether store, lookup and delete return the same
# output and the same exit codes on each one. Anything that differs has to be
# handled in the adapter.
#
# Both backends are exercised in a throwaway container, because neither can be
# installed on the macOS host this spike runs from.

source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

RESULTS="$OUT_DIR/secret-tool-results.txt"
: > "$RESULTS"

# The contract the credential store adapter depends on, run identically against
# whichever backend is providing org.freedesktop.secrets on the session bus.
read -r -d '' CONTRACT <<'SCRIPT' || true
set -u
label="$1"
echo "### backend: $label"

attrs="service jira-server-mcp profile work"
secret="s3cr3t-value"

printf '%s' "$secret" | secret-tool store --label="jira-server-mcp work" $attrs
echo "store exit: $?"

got="$(secret-tool lookup $attrs)"; rc=$?
echo "lookup exit: $rc"
echo "lookup bytes: $(printf '%s' "$got" | wc -c)"
[ "$got" = "$secret" ] && echo "lookup roundtrip: exact" || echo "lookup roundtrip: DIFFERS (got '$got')"

missing="$(secret-tool lookup service jira-server-mcp profile nonexistent 2>&1)"; rc=$?
echo "lookup-missing exit: $rc"
echo "lookup-missing stdout+stderr: '${missing}'"

secret-tool clear $attrs; echo "clear exit: $?"

after="$(secret-tool lookup $attrs 2>&1)"; rc=$?
echo "lookup-after-clear exit: $rc"
echo "lookup-after-clear output: '${after}'"
SCRIPT

run_backend() {
  local label="$1" install="$2" launch="$3"
  log "=== $label ==="
  # --privileged is not needed; a session bus inside the container is enough.
  docker run --rm -i debian:12 bash -c "
    export DEBIAN_FRONTEND=noninteractive
    apt-get update -qq >/dev/null 2>&1
    apt-get install -y -qq libsecret-tools dbus-x11 $install >/dev/null 2>&1 \
      || { echo 'apt install failed for: $install'; exit 1; }
    echo 'dbus services offering org.freedesktop.secrets:'
    grep -rl 'org.freedesktop.secrets' /usr/share/dbus-1/services/ 2>/dev/null || echo '  (none)'
    cat > /contract.sh <<'EOF'
$CONTRACT
EOF
    chmod +x /contract.sh
    $launch
  " 2>&1 | tee -a "$RESULTS"
  echo | tee -a "$RESULTS"
}

# GNOME Keyring: the daemon registers org.freedesktop.secrets when started with
# the secrets component, and the keyring has to be unlocked before use.
run_backend "gnome-keyring" "gnome-keyring" \
  'dbus-run-session -- bash -c "echo -n test | gnome-keyring-daemon --unlock --components=secrets >/dev/null 2>&1; sleep 2; bash /contract.sh gnome-keyring"'

# KWallet: whether it answers org.freedesktop.secrets at all is the open part of
# the question, so probe the bus name before running the contract. The daemon
# binary lives in kwalletmanager on Debian; there is no kwalletd5 package.
run_backend "kwallet" "kwalletmanager" \
  'dbus-run-session -- bash -c "kwalletd5 >/dev/null 2>&1 & sleep 5; echo -n \"org.freedesktop.secrets owned: \"; dbus-send --session --dest=org.freedesktop.DBus --type=method_call --print-reply /org/freedesktop/DBus org.freedesktop.DBus.NameHasOwner string:org.freedesktop.secrets 2>&1 | tail -1; echo -n \"org.kde.kwalletd5 owned: \"; dbus-send --session --dest=org.freedesktop.DBus --type=method_call --print-reply /org/freedesktop/DBus org.freedesktop.DBus.NameHasOwner string:org.kde.kwalletd5 2>&1 | tail -1; bash /contract.sh kwallet"'

log "results written to $RESULTS"
