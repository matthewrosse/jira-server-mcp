#!/usr/bin/env bash
# Bring up a canonical Jira Server 8.20.7 locally and seed it — the one command.
#
# Starts the two containers, then hands the running instance to the integration suite, which drives
# first-run setup over HTTP, applies the committed testing licence, seeds the fixtures, mints a
# personal access token, and runs the tests against it. Setup and seeding are not duplicated here:
# this script starts containers, and the same C# the CI path uses does the rest.
#
# The licence expires three hours after it is applied.

set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$HERE/.." && pwd)"
COMPOSE_FILE="$REPO_ROOT/tests/harness/docker-compose.yml"

BASE_URL="${JIRA_HARNESS_BASE_URL:-http://localhost:8080}"

echo "Starting Jira 8.20.7 and Postgres. First boot takes several minutes."
docker compose -f "$COMPOSE_FILE" up -d

echo
echo "Setting up, licensing, seeding, and running the suite against $BASE_URL."
echo "Tear it down afterwards with scripts/jira-down.sh."
echo

JIRA_HARNESS_BASE_URL="$BASE_URL" \
  dotnet test "$REPO_ROOT/tests/JiraServerMcp.JiraIntegration.Tests" \
  -- --filter-trait "Category=JiraIntegration"
