#!/usr/bin/env python3
"""Capture real 8.20.7 payloads for the deserialization fixtures Phase 2 needs.

Everything is fetched with the personal access token minted by 03-pat.sh, so the
captures come back over exactly the authentication path the product uses rather
than over the administrator basic-auth path the harness uses to seed.
"""

import json
import os
import pathlib
import sys
import urllib.error
import urllib.parse
import urllib.request

HERE = pathlib.Path(__file__).resolve().parent
REPO_ROOT = HERE.parent.parent
OUT_DIR = HERE / "captured"
# Payloads outlive the spike: Phase 2 deserializes against them, so they land in
# tests/fixtures rather than in this script's scratch directory.
PAYLOAD_DIR = REPO_ROOT / "tests" / "fixtures" / "payloads" / "8.20.7"
PAYLOAD_DIR.mkdir(parents=True, exist_ok=True)

BASE_URL = os.environ.get("BASE_URL", "http://localhost:8080").rstrip("/")
PAT_FILE = OUT_DIR / "pat.txt"
SEEDED = OUT_DIR / "seeded.json"

if not PAT_FILE.exists():
    sys.exit(f"no token at {PAT_FILE}; run 03-pat.sh first")
TOKEN = PAT_FILE.read_text().strip()

seeded = json.loads(SEEDED.read_text()) if SEEDED.exists() else {}
project = seeded.get("project", "PZ")
issue = (seeded.get("issues") or ["PZ-1"])[0]

# name -> path. Names become fixture filenames, so keep them stable.
ENDPOINTS = {
    "myself": "/rest/api/2/myself",
    "serverinfo": "/rest/api/2/serverInfo",
    "search": "/rest/api/2/search?" + urllib.parse.urlencode(
        {"jql": f"project = {project} ORDER BY created DESC", "maxResults": 50}
    ),
    "issue-default": f"/rest/api/2/issue/{issue}",
    "issue-expanded": f"/rest/api/2/issue/{issue}?" + urllib.parse.urlencode(
        {"expand": "changelog,renderedFields,transitions"}
    ),
    "issue-transitions": f"/rest/api/2/issue/{issue}/transitions",
    "issue-comments": f"/rest/api/2/issue/{issue}/comment",
    "issue-worklogs": f"/rest/api/2/issue/{issue}/worklog",
    "project-list": "/rest/api/2/project",
    "project-detail": f"/rest/api/2/project/{project}",
    "project-statuses": f"/rest/api/2/project/{project}/statuses",
    "project-components": f"/rest/api/2/project/{project}/components",
    "project-versions": f"/rest/api/2/project/{project}/versions",
    "createmeta": "/rest/api/2/issue/createmeta?" + urllib.parse.urlencode(
        {"projectKeys": project, "expand": "projects.issuetypes.fields"}
    ),
    "user-search": "/rest/api/2/user/search?" + urllib.parse.urlencode({"username": "admin"}),
    "fields": "/rest/api/2/field",
    "agile-board": "/rest/agile/1.0/board?maxResults=50",
}


def get(path):
    request = urllib.request.Request(f"{BASE_URL}{path}")
    request.add_header("Authorization", f"Bearer {TOKEN}")
    request.add_header("Accept", "application/json")
    try:
        with urllib.request.urlopen(request, timeout=120) as response:
            return response.status, response.read().decode()
    except urllib.error.HTTPError as error:
        return error.code, error.read().decode()


def main():
    index = {}
    for name, path in ENDPOINTS.items():
        status, text = get(path)
        print(f"  {status}  {path}")
        index[name] = {"path": path, "status": status}
        if status == 200:
            try:
                text = json.dumps(json.loads(text), indent=2)
            except json.JSONDecodeError:
                pass
            (PAYLOAD_DIR / f"{name}.json").write_text(text)

    # Boards only exist on a licensed instance, and a scrum board carries the
    # sprint and backlog endpoints the agile tools will need fixtures for.
    status, text = get("/rest/agile/1.0/board?maxResults=1")
    if status == 200:
        boards = json.loads(text).get("values") or []
        if boards:
            board_id = boards[0]["id"]
            for name, path in (
                ("agile-board-detail", f"/rest/agile/1.0/board/{board_id}"),
                ("agile-sprints", f"/rest/agile/1.0/board/{board_id}/sprint"),
                ("agile-backlog", f"/rest/agile/1.0/board/{board_id}/backlog"),
                ("agile-board-issues", f"/rest/agile/1.0/board/{board_id}/issue"),
            ):
                sub_status, sub_text = get(path)
                print(f"  {sub_status}  {path}")
                index[name] = {"path": path, "status": sub_status}
                if sub_status == 200:
                    (PAYLOAD_DIR / f"{name}.json").write_text(
                        json.dumps(json.loads(sub_text), indent=2)
                    )
        else:
            print("  no boards returned; agile sub-resources not captured")

    (PAYLOAD_DIR / "index.json").write_text(json.dumps(index, indent=2))
    captured = sorted(p.name for p in PAYLOAD_DIR.glob("*.json"))
    print(f"{len(captured) - 1} payloads captured into {PAYLOAD_DIR}")


if __name__ == "__main__":
    main()
