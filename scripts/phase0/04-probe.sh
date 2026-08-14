#!/usr/bin/env bash
# Question 4: what does the software API return where Jira Software is absent?
# The capability probe's whole discrimination rests on this, so record the status
# and the body on both kinds of instance and compare.
#
# Run against a Jira Software instance and against a Jira Core one; pass a label
# so the two captures do not overwrite each other.

source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

LABEL="${1:?usage: 04-probe.sh <label>   e.g. software | core}"

probe() {
  local path="$1" name="$2"
  local response status body
  response="$(curl -sS -u "$ADMIN_USER:$ADMIN_PASSWORD" -w '\n%{http_code}' "$BASE_URL$path")"
  status="$(printf '%s' "$response" | tail -1)"
  body="$(printf '%s' "$response" | sed '$d')"

  log "GET $path -> $status"
  printf '%s\n' "$body" | head -c 600
  echo
  {
    echo "GET $path"
    echo "status: $status"
    echo "body:"
    printf '%s\n' "$body"
  } > "$OUT_DIR/probe-$LABEL-$name.txt"
}

log "probing instance labelled '$LABEL' at $BASE_URL"
probe "/rest/api/2/serverInfo" "serverinfo"
probe "/rest/api/2/applicationrole" "applicationrole"
probe "/rest/agile/1.0/board?maxResults=1" "agile-board"

log "captures written to $OUT_DIR/probe-$LABEL-*.txt"
