#!/usr/bin/env python3
"""Question 1 and 2: does the Data Center timebomb licence activate 8.20.7 in
single-node mode, and can the first-run setup wizard be driven over HTTP?

The wizard is the most brittle part of the plan, so this does not blind-post a
guessed sequence. It fetches whatever page Jira is showing, parses the form off
it, fills the fields it recognises, and posts. Every request it makes is written
to captured/setup-requests.log so tests/README.md can quote what actually worked
rather than what was intended.

Standard library only: the spike must run on a bare host.
"""

import http.cookiejar
import json
import os
import pathlib
import re
import sys
import time
import urllib.error
import urllib.parse
import urllib.request

HERE = pathlib.Path(__file__).resolve().parent
REPO_ROOT = HERE.parent.parent
OUT_DIR = HERE / "captured"
OUT_DIR.mkdir(exist_ok=True)

BASE_URL = os.environ.get("BASE_URL", "http://localhost:8080").rstrip("/")
ADMIN_USER = os.environ.get("ADMIN_USER", "admin")
ADMIN_PASSWORD = os.environ.get("ADMIN_PASSWORD", "admin123")
ADMIN_EMAIL = os.environ.get("ADMIN_EMAIL", "admin@example.com")
ADMIN_FULLNAME = os.environ.get("ADMIN_FULLNAME", "Phase Zero Admin")

LICENSE_FILE = pathlib.Path(
    os.environ.get("LICENSE_FILE", REPO_ROOT / "tests" / "fixtures" / "jira-dc-timebomb-3h.license")
)
REQUEST_LOG = OUT_DIR / "setup-requests.log"

# Values the wizard asks for, keyed by the form field name Jira uses. Anything
# not listed here is carried through from the form's own value, which covers
# atl_token and the hidden step markers.
FIELD_VALUES = {
    "setupOption": "classic",
    "title": "Phase Zero Jira",
    "mode": "private",
    "baseURL": BASE_URL,
    "setupLicenseKey": None,  # filled from the fixture at runtime
    "licenseKey": None,
    "username": ADMIN_USER,
    "fullname": ADMIN_FULLNAME,
    "email": ADMIN_EMAIL,
    "password": ADMIN_PASSWORD,
    "confirm": ADMIN_PASSWORD,
    "noemail": "true",
}

cookie_jar = http.cookiejar.MozillaCookieJar(str(OUT_DIR / "cookies.txt"))
opener = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(cookie_jar))
opener.addheaders = [("User-Agent", "jira-server-mcp-phase0-spike")]

request_log = []


def log(msg):
    print(f"[{time.strftime('%H:%M:%S')}] {msg}", flush=True)


def record(method, url, fields, status, final_url):
    """Keep a verbatim trace: this trace becomes tests/README.md."""
    request_log.append(
        {
            "method": method,
            "url": url,
            "fields": {k: ("<licence>" if "icense" in k.lower() else v) for k, v in (fields or {}).items()},
            "status": status,
            "landed_on": final_url,
        }
    )


def xsrf_cookie():
    """Jira binds atl_token to the session cookie, so the posted token has to be
    the one from atlassian.xsrf.token rather than whatever the form shipped."""
    for cookie in cookie_jar:
        if cookie.name == "atlassian.xsrf.token":
            return cookie.value
    return None


def fetch(url, data=None, referer=None):
    body = urllib.parse.urlencode(data).encode() if data else None
    request = urllib.request.Request(url, data=body, method="POST" if data else "GET")
    if data:
        request.add_header("Content-Type", "application/x-www-form-urlencoded")
    # The wizard's XSRF filter rejects a post whose Referer is not the instance.
    if referer:
        request.add_header("Referer", referer)
    try:
        with opener.open(request, timeout=120) as response:
            text = response.read().decode("utf-8", "replace")
            status, final_url = response.status, response.geturl()
    except urllib.error.HTTPError as error:
        text = error.read().decode("utf-8", "replace")
        status, final_url = error.code, error.geturl()
    record("POST" if data else "GET", url, data, status, final_url)
    log(f"{'POST' if data else 'GET '} {url} -> {status} {final_url}")
    return status, final_url, text


FORM_RE = re.compile(r"<form\b[^>]*>.*?</form>", re.S | re.I)
ACTION_RE = re.compile(r'\baction\s*=\s*["\']([^"\']*)["\']', re.I)
INPUT_RE = re.compile(r"<input\b[^>]*>", re.I)
SELECT_RE = re.compile(r'<select\b[^>]*\bname\s*=\s*["\']([^"\']+)["\'][^>]*>(.*?)</select>', re.S | re.I)
OPTION_RE = re.compile(r'<option\b([^>]*)>', re.I)
ATTR_RE = re.compile(r'\b(name|value|type|selected)\s*=\s*["\']([^"\']*)["\']', re.I)


def parse_forms(html):
    """Return [(action, {field: value})] for every form on the page."""
    forms = []
    for block in FORM_RE.findall(html):
        action_match = ACTION_RE.search(block)
        if not action_match:
            continue
        fields = {}
        for tag in INPUT_RE.findall(block):
            attrs = {k.lower(): v for k, v in ATTR_RE.findall(tag)}
            name = attrs.get("name")
            if not name or attrs.get("type", "").lower() == "submit":
                continue
            fields[name] = attrs.get("value", "")
        # Selects matter on the wizard's database and mail steps, and a form
        # posted without them fails validation and re-renders the same page.
        for name, body in SELECT_RE.findall(block):
            chosen = ""
            for raw_attrs in OPTION_RE.findall(body):
                attrs = {k.lower(): v for k, v in ATTR_RE.findall(raw_attrs)}
                if "selected" in raw_attrs.lower():
                    chosen = attrs.get("value", "")
                    break
            fields[name] = chosen
        forms.append((action_match.group(1), fields))
    return forms


def page_errors(html):
    """Jira renders wizard validation failures in these two shapes."""
    found = []
    for pattern in (
        r'<div[^>]*class="[^"]*aui-message[^"]*error[^"]*"[^>]*>(.*?)</div>',
        r'<span[^>]*class="[^"]*errMsg[^"]*"[^>]*>(.*?)</span>',
        r'<div[^>]*class="[^"]*errMsg[^"]*"[^>]*>(.*?)</div>',
    ):
        for raw in re.findall(pattern, html, re.S | re.I):
            text = re.sub(r"<[^>]+>", " ", raw)
            text = re.sub(r"\s+", " ", text).strip()
            if text:
                found.append(text)
    return found


def fill(fields, license_key):
    """Overwrite the fields we have answers for; leave the rest as Jira sent them."""
    filled = dict(fields)
    for name in list(filled):
        if name in ("setupLicenseKey", "licenseKey"):
            filled[name] = license_key
        elif name in FIELD_VALUES and FIELD_VALUES[name] is not None:
            filled[name] = FIELD_VALUES[name]
    token = xsrf_cookie()
    if token:
        filled["atl_token"] = token
    return filled


def main():
    if not LICENSE_FILE.exists():
        sys.exit(f"licence fixture missing: {LICENSE_FILE}")
    license_key = re.sub(r"\s+", "", LICENSE_FILE.read_text())
    log(f"licence fixture loaded ({len(license_key)} chars)")

    started = time.time()
    status, url, html = fetch(f"{BASE_URL}/")

    for step in range(1, 16):
        # The mail step hands off to WelcomeToJIRA.jspa, which is the wizard's
        # real terminus; the dashboard only follows once a human clicks through.
        if any(p in url for p in ("/secure/WelcomeToJIRA.jspa", "/secure/Dashboard.jspa", "/login.jsp")):
            log(f"wizard complete after {step - 1} steps, landed on {url}")
            break

        errors = page_errors(html)
        if errors:
            log(f"  page reports: {errors}")

        forms = parse_forms(html)
        if not forms:
            (OUT_DIR / f"setup-step-{step}-noform.html").write_text(html)
            sys.exit(f"no form on {url}; page saved for inspection")

        # A wizard page carries several forms posting to the same Setup* action:
        # the licence step opens with a stub holding nothing but atl_token. The
        # real one is whichever carries the most fields besides the token.
        setup_forms = [f for f in forms if "Setup" in f[0]] or forms
        action, fields = max(
            setup_forms,
            key=lambda f: len([n for n in f[1] if n != "atl_token"]),
        )
        target = urllib.parse.urljoin(url, action)
        log(f"step {step}: form -> {action} fields={sorted(fields)}")
        slug = re.sub(r"[^A-Za-z0-9._-]", "_", action.split("!")[0])
        (OUT_DIR / f"setup-step-{step}-{slug}.html").write_text(html)

        status, url, html = fetch(target, fill(fields, license_key), referer=url)

        if status >= 400:
            (OUT_DIR / f"setup-step-{step}-error.html").write_text(html)
            sys.exit(f"step {step} returned {status}; page saved for inspection")
    else:
        (OUT_DIR / "setup-stuck.html").write_text(html)
        sys.exit(f"wizard did not finish in 15 steps; stuck at {url}")

    elapsed = time.time() - started
    log(f"setup wizard driven over HTTP in {elapsed:.0f}s")

    REQUEST_LOG.write_text(json.dumps(request_log, indent=2))
    log(f"request trace written to {REQUEST_LOG}")
    (OUT_DIR / "setup-elapsed.txt").write_text(f"{elapsed:.0f}\n")


if __name__ == "__main__":
    main()
