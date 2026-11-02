#!/usr/bin/env python3
"""Fail closed on NuGet's JSON vulnerability report (including feed failures)."""
import json
import sys
from pathlib import Path


def check(report):
    if report.get("version") != 1 or not report.get("projects"):
        raise ValueError("Missing or unsupported NuGet audit result")
    problems = [str(item) for item in report.get("problems", [])]
    for project in report["projects"]:
        if not project.get("frameworks"):
            problems.append(f"No frameworks audited: {project.get('path')}")
        for framework in project.get("frameworks", []):
            for kind in ("topLevelPackages", "transitivePackages"):
                for package in framework.get(kind, []):
                    for advisory in package.get("vulnerabilities", []):
                        problems.append(f"{package['id']} {package.get('resolvedVersion')}: {advisory}")
    if problems:
        raise ValueError("\n".join(problems))


if __name__ == "__main__":
    try:
        check(json.loads(Path(sys.argv[1]).read_text(encoding="utf-8-sig")))
    except (ValueError, KeyError, OSError, IndexError) as exc:
        sys.exit(f"Dependency audit failed: {exc}")
    print("NuGet reports no known vulnerable direct or transitive dependencies.")
