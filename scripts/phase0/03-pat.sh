#!/usr/bin/env bash
# Question 3: can a personal access token be minted programmatically on 8.20.7?
# Then the Phase 0 acceptance check itself: an authenticated GET of the
# current-user endpoint with that token, returning 200.

source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

log "POST /rest/pat/latest/tokens as $ADMIN_USER"
response="$(curl -sS -u "$ADMIN_USER:$ADMIN_PASSWORD" \
  -H 'Content-Type: application/json' \
  -H 'X-Atlassian-Token: no-check' \
  -X POST -d '{"name":"phase0-spike","expirationDuration":1}' \
  -w '\n%{http_code}' \
  "$BASE_URL/rest/pat/latest/tokens")"

status="$(printf '%s' "$response" | tail -1)"
body="$(printf '%s' "$response" | sed '$d')"

if [ "$status" != "201" ]; then
  log "PAT creation returned $status, not 201"
  printf '%s\n' "$body"
  exit 1
fi

# The raw token is returned once and never again, exactly as in the UI.
token="$(printf '%s' "$body" | sed -n 's/.*"rawToken" *: *"\([^"]*\)".*/\1/p')"
[ -n "$token" ] || { log "no rawToken in response"; printf '%s\n' "$body"; exit 1; }

printf '%s' "$token" > "$PAT_FILE"
chmod 600 "$PAT_FILE"
log "PAT minted (id $(printf '%s' "$body" | sed -n 's/.*"id" *: *\([0-9]*\).*/\1/p')), written to $PAT_FILE"

log "GET /rest/api/2/myself with bearer token"
myself="$(curl -sS -H "Authorization: Bearer $token" \
  -w '\n%{http_code}' "$BASE_URL/rest/api/2/myself")"
myself_status="$(printf '%s' "$myself" | tail -1)"
myself_body="$(printf '%s' "$myself" | sed '$d')"

printf '%s\n' "$myself_body" > "$OUT_DIR/myself.json"
log "GET /rest/api/2/myself -> $myself_status"
printf '%s\n' "$myself_body"

[ "$myself_status" = "200" ] || exit 1
log "PHASE 0 ACCEPTANCE MET: authenticated 200 from the current-user endpoint"
