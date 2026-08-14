#!/usr/bin/env bash
# Question 5: how long does 8.20.7 take to become ready?
# Brings up Postgres and Jira, then polls GET /status until it stops changing.
# Every state transition is stamped so the timing breakdown is readable.

source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

TIMINGS="$OUT_DIR/boot-timings.txt"
: > "$TIMINGS"

stamp() {
  local elapsed=$(( $(date +%s) - START ))
  printf '%4ds  %s\n' "$elapsed" "$1" | tee -a "$TIMINGS"
}

log "docker compose up -d"
START=$(date +%s)
docker compose -f "$COMPOSE_FILE" up -d

stamp "containers created"

last_state=""
deadline=$(( START + 900 ))

while [ "$(date +%s)" -lt "$deadline" ]; do
  body="$(curl -sS --max-time 10 "$BASE_URL/status" 2>/dev/null || true)"
  state="$(printf '%s' "$body" | sed -n 's/.*"state" *: *"\([A-Z_]*\)".*/\1/p')"

  if [ -n "$state" ] && [ "$state" != "$last_state" ]; then
    stamp "GET /status -> $state"
    last_state="$state"
  fi

  # FIRST_RUN means Tomcat is serving and the setup wizard is waiting for us.
  # RUNNING means an already-configured instance finished starting.
  if [ "$state" = "FIRST_RUN" ] || [ "$state" = "RUNNING" ]; then
    stamp "/status reports $state"

    # /status flips before the web layer will serve anything: / answers 503 and
    # redirects to startup.jsp for a while longer. Readiness is the second gate,
    # not the first, and a harness that trusts /status alone races the wizard.
    while [ "$(date +%s)" -lt "$deadline" ]; do
      # -L matters: / answers 302 immediately and only the page it redirects to
      # carries the 503, so an unfollowed request looks ready when it is not.
      # A failed curl must not merge its own output with the code, hence the
      # assignment-or-000 rather than a pipeline fallback.
      code="$(curl -sSL -o /dev/null -w '%{http_code}' --max-time 30 "$BASE_URL/" 2>/dev/null)" || code=000
      if [ "$code" = "200" ]; then
        stamp "GET / -> $code, wizard servable"
        echo
        log "timings written to $TIMINGS"
        exit 0
      fi
      sleep 5
    done
    stamp "TIMED OUT waiting for the web layer after /status reported $state"
    exit 1
  fi

  sleep 5
done

stamp "TIMED OUT after 900s (last state: ${last_state:-none})"
log "container logs tail:"
docker compose -f "$COMPOSE_FILE" logs --tail 50 jira
exit 1
