"""Regression checks for the test command, without launching Unity."""

from contextlib import redirect_stderr, redirect_stdout
import io
from pathlib import Path
import subprocess
import unittest
from unittest.mock import patch

import test_editmode


class EditModeCommandTests(unittest.TestCase):
    def run_command(self, xml, exit_code=0, args=None):
        output = io.StringIO()

        def launch(command, check):
            self.assertFalse(check)
            self.command = command
            self.report = Path(command[command.index("--output") + 1])
            if xml is not None:
                self.report.write_text(xml, encoding="utf-8")
            return subprocess.CompletedProcess(command, exit_code)

        with patch("test_editmode.subprocess.run", side_effect=launch):
            with redirect_stdout(output), redirect_stderr(output):
                result = test_editmode.main(args or [])
        self.assertFalse(self.report.parent.exists(), "temporary results must be removed")
        return result, output.getvalue()

    def test_passing_run_and_filter_forwarding(self):
        result, output = self.run_command(
            '<test-run total="2" passed="2" failed="0" result="Passed"/>',
            args=["Some.Tests with spaces"])
        self.assertEqual(result, 0)
        self.assertIn("2 / 2 / 0", output)
        self.assertEqual(self.command[-2:], ["--filter", "Some.Tests with spaces"])
        self.assertEqual(self.command[2], str(Path(__file__).resolve().parent.parent))
        self.assertIn("EditMode", self.command)

    def test_zero_tests_is_failure_even_with_successful_cli(self):
        result, output = self.run_command('<test-run total="0" passed="0" failed="0" result="Passed"/>')
        self.assertNotEqual(result, 0)
        self.assertIn("No tests matched", output)

    def test_failure_prints_full_name_and_assertion_even_with_successful_cli(self):
        result, output = self.run_command('''
            <test-run total="1" passed="0" failed="1" result="Failed">
              <test-case fullname="Example.Tests.Fails" result="Failed">
                <failure><message>Expected: 2\nBut was: 1 &amp; wrong</message></failure>
              </test-case>
            </test-run>''')
        self.assertNotEqual(result, 0)
        self.assertIn("Example.Tests.Fails", output)
        self.assertIn("Expected: 2\nBut was: 1 & wrong", output)

    def test_missing_or_invalid_report_is_failure(self):
        for xml in (None, "broken XML", '<test-run/>',
                    '<test-run total="x" passed="0" failed="0"/>',
                    '<test-run total="1" passed="2" failed="0"/>',
                    '<test-run total="-1" passed="0" failed="0"/>',
                    '<test-run total="1" passed="1" failed="0" result="invalid"/>',
                    '<test-run total="1" passed="1" failed="0"/>',
                    '<other total="1" passed="1" failed="0"/>'):
            with self.subTest(xml=xml):
                result, output = self.run_command(xml)
                self.assertNotEqual(result, 0)
                self.assertIn("No valid NUnit results report", output)

    def test_suite_failures_print_full_name_and_message(self):
        for suite_type, message in (
                ("TestFixture", "OneTimeSetUp failed"),
                ("TestFixture", "OneTimeTearDown failed"),
                ("Assembly", "Assembly initialization failed")):
            with self.subTest(suite_type=suite_type, message=message):
                result, output = self.run_command(f'''
                    <test-run total="1" passed="0" failed="1" result="Failed">
                      <test-suite fullname="Example.Parent" result="Failed">
                        <test-suite type="{suite_type}" fullname="Example.FailingSuite" result="Failed">
                          <failure><message>{message}</message></failure>
                        </test-suite>
                      </test-suite>
                    </test-run>''')
                self.assertNotEqual(result, 0)
                self.assertIn("Example.FailingSuite", output)
                self.assertIn(message, output)
                self.assertNotIn("<no failure message>", output)

    def test_failed_suite_overrides_inconsistent_summary(self):
        result, output = self.run_command('''
            <test-run total="1" passed="1" failed="0" result="Passed">
              <test-suite fullname="Example.FailingSuite" result="Failed">
                <failure><message>OneTimeTearDown failed</message></failure>
              </test-suite>
            </test-run>''')
        self.assertNotEqual(result, 0)
        self.assertIn("Example.FailingSuite", output)
        self.assertIn("OneTimeTearDown failed", output)

    def test_cli_failure_overrides_passing_xml(self):
        result, output = self.run_command(
            '<test-run total="1" passed="1" failed="0" result="Passed"/>', exit_code=6)
        self.assertNotEqual(result, 0)
        self.assertIn("exit code 6", output)

    def test_failed_case_overrides_inconsistent_summary(self):
        result, output = self.run_command('''
            <test-run total="1" passed="1" failed="0" result="Passed">
              <test-case fullname="Example.Fails" result="Failed">
                <failure><message>Assertion failed</message></failure>
              </test-case>
            </test-run>''')
        self.assertNotEqual(result, 0)
        self.assertIn("Example.Fails", output)
        self.assertIn("Assertion failed", output)

    def test_cli_cannot_start(self):
        output = io.StringIO()
        with patch("test_editmode.subprocess.run", side_effect=FileNotFoundError("unity")):
            with redirect_stderr(output):
                result = test_editmode.main([])
        self.assertNotEqual(result, 0)
        self.assertIn("Could not start unity test", output.getvalue())

    def test_extra_arguments_are_rejected_before_launch(self):
        with patch("test_editmode.subprocess.run") as launch, redirect_stderr(io.StringIO()):
            self.assertEqual(test_editmode.main(["one", "two"]), 2)
        launch.assert_not_called()


if __name__ == "__main__":
    unittest.main()
