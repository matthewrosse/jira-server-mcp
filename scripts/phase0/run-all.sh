#!/usr/bin/env bash
# The Phase 0 acceptance sequence: an empty Docker host to an authenticated 200
# from the current-user endpoint, with timings. Run it from a clean state:
#
#   ./scripts/phase0/run-all.sh
#
# Answers questions 1, 2, 3, 4 and 5 of docs/design/architecture.md section 14.
# Question 6 has its own script because it needs no Jira: ./07-secret-tool.sh

source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

TOTAL_START=$(date +%s)

log "tearing down any previous run"
docker compose -f "$COMPOSE_FILE" down -v >/dev/null 2>&1 || true

"$HERE/01-boot.sh"
python3 "$HERE/02-setup.py"
"$HERE/03-pat.sh"
"$HERE/04-probe.sh" software
python3 "$HERE/05-seed.py"
python3 "$HERE/06-capture.py"

log "total elapsed: $(( $(date +%s) - TOTAL_START ))s"
log "leaving the instance up; tear it down with:"
log "  docker compose -f $COMPOSE_FILE down -v"
