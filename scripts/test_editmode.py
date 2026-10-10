"""Run the host project's EditMode tests and check their NUnit report."""

from pathlib import Path
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET


def summarize(report):
    # NUnit 3 format: https://docs.nunit.org/articles/nunit/technical-notes/usage/Test-Result-XML-Format.html
    try:
        root = ET.parse(report).getroot()
        counts = [int(root.attrib[key]) for key in ("total", "passed", "failed")]
        total, passed, failed = counts
        if root.tag != "test-run" or any(count < 0 for count in counts):
            raise ValueError("expected a test-run with nonnegative counts")
        if passed + failed > total:
            raise ValueError("passed and failed counts exceed total")
        if root.get("result") not in ("Passed", "Failed", "Skipped", "Inconclusive", "Warning"):
            raise ValueError("missing or unknown test-run result")
    except (OSError, ET.ParseError, KeyError, ValueError) as error:
        print(f"No valid NUnit results report: {error}", file=sys.stderr)
        return 1

    print(f"total / passed / failed: {total} / {passed} / {failed}")
    failing_tests = [test for test in root.iter("test-case") if test.get("result") == "Failed"]
    for test in failing_tests:
        print(test.get("fullname", test.get("name", "<unnamed test>")))
        print(test.findtext("failure/message", "<no failure message>"))
    if total == 0:
        print("No tests matched; zero tests is not a passing run.", file=sys.stderr)
        return 1
    return int(failed > 0 or bool(failing_tests) or root.get("result") == "Failed")


def main(args=None):
    args = sys.argv[1:] if args is None else args
    if len(args) > 1:
        print("Usage: bash scripts/test-editmode.sh [filter]", file=sys.stderr)
        return 2

    project = Path(__file__).resolve().parent.parent
    with tempfile.TemporaryDirectory(prefix="agent-skills-editmode-") as temp:
        report = Path(temp) / "results.xml"
        command = ["unity", "test", str(project), "--mode", "EditMode",
                   "--report-format", "nunit", "--output", str(report)]
        if args:
            command.extend(["--filter", args[0]])
        try:
            run = subprocess.run(command, check=False)
        except OSError as error:
            print(f"Could not start unity test: {error}", file=sys.stderr)
            return 1
        result = summarize(report)
        if run.returncode:
            print(f"unity test failed with exit code {run.returncode}.", file=sys.stderr)
            return 1
        return result


if __name__ == "__main__":
    sys.exit(main())
