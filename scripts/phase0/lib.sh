# Shared settings for the Phase 0 spike scripts. Sourced, not executed.
# Throwaway code: this exists to answer the open questions in
# docs/design/architecture.md section 14, not to become the harness.

set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$HERE/../.." && pwd)"

BASE_URL="${BASE_URL:-http://localhost:8080}"
ADMIN_USER="${ADMIN_USER:-admin}"
ADMIN_PASSWORD="${ADMIN_PASSWORD:-admin123}"
ADMIN_EMAIL="${ADMIN_EMAIL:-admin@example.com}"
ADMIN_FULLNAME="${ADMIN_FULLNAME:-Phase Zero Admin}"

COMPOSE_FILE="${COMPOSE_FILE:-$HERE/docker-compose.yml}"
LICENSE_FILE="${LICENSE_FILE:-$REPO_ROOT/tests/fixtures/jira-dc-timebomb-3h.license}"
OUT_DIR="$HERE/captured"
COOKIE_JAR="$OUT_DIR/cookies.txt"
PAT_FILE="$OUT_DIR/pat.txt"

mkdir -p "$OUT_DIR"

log() { printf '[%s] %s\n' "$(date -u +%H:%M:%S)" "$*"; }

# Jira's setup wizard and its REST endpoints both require the XSRF token that
# rides in the atlassian.xsrf.token cookie. Read it back out of the jar.
xsrf_token() {
  awk '$6 == "atlassian.xsrf.token" { print $7 }' "$COOKIE_JAR" | tail -1
}

# The published licence is line-wrapped for display. Jira wants it unwrapped.
license_key() {
  tr -d '[:space:]' < "$LICENSE_FILE"
}
