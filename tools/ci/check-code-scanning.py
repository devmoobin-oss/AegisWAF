#!/usr/bin/env python3
"""Fail-closed preflight for GitHub code-scanning availability.

The CodeQL action can spend several minutes building and querying a database
before reporting that the repository cannot accept code-scanning results. This
small standard-library-only probe turns that configuration gap into an early,
actionable failure without printing the API response body.
"""

from __future__ import annotations

import os
import sys
import urllib.error
import urllib.request


def fail(reason: str) -> int:
    print(f"CODE_SCANNING_PREFLIGHT_FAIL {reason}", file=sys.stderr)
    return 1


def main() -> int:
    token = os.environ.get("GITHUB_TOKEN", "").strip()
    repository = os.environ.get("GITHUB_REPOSITORY", "").strip()
    api_url = os.environ.get("GITHUB_API_URL", "https://api.github.com").rstrip("/")
    if not token or not repository:
        return fail("GITHUB_TOKEN and GITHUB_REPOSITORY are required")

    request = urllib.request.Request(
        f"{api_url}/repos/{repository}/code-scanning/analyses?per_page=1",
        headers={
            "Accept": "application/vnd.github+json",
            "Authorization": f"Bearer {token}",
            "X-GitHub-Api-Version": "2022-11-28",
        },
        method="GET",
    )
    try:
        with urllib.request.urlopen(request, timeout=15) as response:
            if response.status != 200:
                return fail(f"unexpected GitHub API status {response.status}")
    except urllib.error.HTTPError as error:
        if error.code in (403, 404):
            return fail(
                "Code scanning is not enabled or the token cannot read code-scanning analyses; "
                "enable Code scanning/Advanced Security and grant security-events read access"
            )
        return fail(f"GitHub API status {error.code}")
    except (urllib.error.URLError, TimeoutError) as error:
        return fail(f"GitHub API unavailable: {type(error).__name__}")

    print("CODE_SCANNING_PREFLIGHT_PASS status=200")
    return 0


if __name__ == "__main__":
    sys.exit(main())
