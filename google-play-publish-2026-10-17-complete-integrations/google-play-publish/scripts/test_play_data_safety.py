"""Offline checks; no Google requests or real credentials."""
import csv
import hashlib
import http.client
import io
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import MagicMock, patch
import urllib.error
import urllib.request

import play_data_safety as cli


def row(question, response="", value="", requirement="MAYBE_REQUIRED", label="Question"):
    return [question, response, value, requirement, label]


def sample():
    return [
        row(cli.COLLECTS, value="TRUE", requirement="REQUIRED"),
        row(cli.ENCRYPTED, value="TRUE"),
        row(cli.ACCOUNTS, "NONE", "TRUE", "MULTIPLE_CHOICE"),
        row("PSL_DATA_TYPES_PERSONAL", "PSL_NAME", "TRUE", "MULTIPLE_CHOICE"),
        row(f"{cli.USAGE}PSL_NAME:DATA_USAGE_USER_CONTROL", "OPTIONAL", "FALSE", "SINGLE_CHOICE"),
        row(f"{cli.USAGE}PSL_NAME:DATA_USAGE_USER_CONTROL", "REQUIRED", "TRUE", "SINGLE_CHOICE"),
    ]


def encode(rows, bom=False):
    output = io.StringIO(newline="")
    writer = csv.writer(output)
    writer.writerow(cli.COLUMNS)
    writer.writerows(rows)
    return (("\ufeff" if bom else "") + output.getvalue()).encode("utf-8")


class ValidationTests(unittest.TestCase):
    def test_current_incomplete_csv(self):
        path = Path(__file__).resolve().parents[4] / "release/google-play/data-safety-prepared.csv"
        _, errors = cli.validate_csv(path.read_bytes())
        self.assertTrue(any(cli.ENCRYPTED in error for error in errors))
        self.assertTrue(any(cli.ACCOUNTS in error for error in errors))
        self.assertTrue(any("user-control" in error for error in errors))

    def test_csv_preserved_with_bom_commas_multiline(self):
        rows = sample()
        rows[0][-1] = 'Question, quoted "word"\nsecond line: é'
        raw = encode(rows, bom=True)
        content, errors = cli.validate_csv(raw)
        self.assertEqual(errors, [])
        self.assertEqual(content.encode("utf-8"), raw)

    def test_inactive_usage_can_be_blank(self):
        rows = sample() + [row(f"{cli.USAGE}PSL_EMAIL:DATA_USAGE_USER_CONTROL", "OPTIONAL", "", "SINGLE_CHOICE"),
                           row(f"{cli.USAGE}PSL_EMAIL:OTHER", value="", requirement="REQUIRED")]
        self.assertEqual(cli.validate_csv(encode(rows))[1], [])
        rows[-1][0] = f"{cli.USAGE}PSL_NAME:OTHER"
        self.assertTrue(cli.validate_csv(encode(rows))[1])

    def test_no_collection_allows_conditional_blanks(self):
        rows = [row(cli.COLLECTS, value="FALSE", requirement="REQUIRED"),
                row(cli.ENCRYPTED), row(f"{cli.USAGE}PSL_NAME:OTHER", requirement="REQUIRED")]
        self.assertEqual(cli.validate_csv(encode(rows))[1], [])

    def test_missing_required_fields(self):
        for index in (0, 1, 2, 3, 5):
            with self.subTest(index=index):
                rows = sample()
                rows[index][2] = ""
                self.assertTrue(cli.validate_csv(encode(rows))[1])

    def test_duplicates_invalid_booleans_single_choice(self):
        for rows in (sample() + [sample()[0]],
                     sample() + [row("OTHER", "CHOICE", "unknown", "MULTIPLE_CHOICE")],
                     sample() + [row(f"{cli.USAGE}PSL_NAME:DATA_USAGE_USER_CONTROL", "THIRD", "TRUE", "SINGLE_CHOICE")]):
            with self.subTest(rows=rows):
                self.assertTrue(cli.validate_csv(encode(rows))[1])

    def test_bad_columns_and_csv(self):
        self.assertTrue(cli.validate_csv(b"wrong,columns\n1,2\n")[1])
        self.assertTrue(cli.validate_csv(encode(sample()) + b"extra,column\n")[1])
        with self.assertRaises(csv.Error):
            cli.validate_csv(encode(sample()) + b'"unterminated')

    def test_required_choice_needs_selection(self):
        rows = sample() + [row("OTHER", "CHOICE", "FALSE", "REQUIRED")]
        self.assertTrue(cli.validate_csv(encode(rows))[1])
        rows[-1][2] = "TRUE"
        self.assertEqual(cli.validate_csv(encode(rows))[1], [])


class SubmissionTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.folder = Path(self.temp.name)
        self.csv = self.folder / "data.csv"
        self.token = self.folder / "private-token"
        self.receipt = self.folder / "receipt.json"
        self.raw = encode(sample(), bom=True)
        self.csv.write_bytes(self.raw)
        self.token.write_text("secret-oauth-token\n", encoding="utf-8")
        self.opener = MagicMock()
        self.response = self.opener.open.return_value.__enter__.return_value
        self.response.status = 204
        self.build = patch.object(cli.urllib.request, "build_opener", return_value=self.opener)
        self.build.start()
        self.addCleanup(self.build.stop)

    def send(self, package="com.example.game"):
        return cli.submit(package, self.csv, self.token, self.receipt)

    def read_receipt(self):
        text = self.receipt.read_text(encoding="utf-8")
        self.assertNotIn("secret-oauth-token", text)
        self.assertNotIn(str(self.token), text)
        self.assertNotIn("upstream-private", text)
        return json.loads(text)

    def test_invalid_package_no_network(self):
        for package in ("com.game/other", "com.game?key=x", "com.game#x", "com.game\n", "https://evil.test", "game"):
            with self.subTest(package=package), self.assertRaises(ValueError):
                self.send(package)
        self.opener.open.assert_not_called()

    def test_validation_precedes_credentials_and_network(self):
        self.csv.write_bytes(encode([row(cli.COLLECTS, value="TRUE", requirement="REQUIRED")]))
        self.token.unlink()
        with self.assertRaisesRegex(ValueError, "preflight"):
            self.send()
        self.assertFalse(self.receipt.exists())
        self.opener.open.assert_not_called()

    def test_existing_receipt_stops_before_token_and_network(self):
        self.receipt.write_text("keep me", encoding="utf-8")
        self.token.unlink()
        with self.assertRaises(FileExistsError):
            self.send()
        self.assertEqual(self.receipt.read_text(encoding="utf-8"), "keep me")
        self.opener.open.assert_not_called()

    def test_input_collisions(self):
        for destination in (self.csv, self.token):
            with self.subTest(destination=destination), self.assertRaises(ValueError):
                cli.submit("com.example.game", self.csv, self.token, destination)
        self.opener.open.assert_not_called()
        self.assertEqual(self.csv.read_bytes(), self.raw)

    def test_success_payload_and_durable_pending_receipt(self):
        def observe(request, timeout):
            pending = self.read_receipt()
            self.assertEqual(pending["status"], "pending")
            self.assertIsNone(pending["apiAccepted"])
            self.assertEqual(pending["csvSha256"], hashlib.sha256(self.raw).hexdigest())
            self.assertEqual(pending["package"], "com.example.game")
            self.assertTrue(pending["timestamp"])
            self.assertEqual(timeout, 30)
            self.assertEqual(request.full_url, "https://androidpublisher.googleapis.com/androidpublisher/v3/applications/com.example.game/dataSafety")
            self.assertEqual(request.method, "POST")
            self.assertEqual(request.get_header("Authorization"), "Bearer secret-oauth-token")
            self.assertEqual(json.loads(request.data), {"safetyLabels": self.raw.decode("utf-8")})
            return self.opener.open.return_value
        self.opener.open.side_effect = observe
        self.assertTrue(self.send())
        receipt = self.read_receipt()
        self.assertIs(receipt["apiAccepted"], True)
        self.assertIs(receipt["consoleReadbackVerified"], False)
        self.assertEqual(receipt["httpStatus"], 204)
        self.opener.open.assert_called_once()

    def test_http_failure_no_body_or_retry(self):
        self.opener.open.side_effect = urllib.error.HTTPError("https://google.test", 403, "secret-oauth-token", {}, io.BytesIO(b"upstream-private"))
        self.assertFalse(self.send())
        receipt = self.read_receipt()
        self.assertIs(receipt["apiAccepted"], False)
        self.assertEqual(receipt["httpStatus"], 403)
        self.opener.open.assert_called_once()

    def test_timeout_unknown_no_retry(self):
        self.opener.open.side_effect = TimeoutError("secret-oauth-token upstream-private")
        self.assertFalse(self.send())
        receipt = self.read_receipt()
        self.assertIsNone(receipt["apiAccepted"])
        self.assertEqual(receipt["status"], "transport_error")
        self.opener.open.assert_called_once()

    def test_wrapped_transport_error(self):
        self.opener.open.side_effect = urllib.error.URLError("secret-oauth-token")
        self.assertFalse(self.send())
        self.assertIsNone(self.read_receipt()["apiAccepted"])
        self.opener.open.assert_called_once()

    def test_malformed_http_response_unknown(self):
        self.opener.open.side_effect = http.client.BadStatusLine("secret-oauth-token")
        self.assertFalse(self.send())
        self.assertIsNone(self.read_receipt()["apiAccepted"])
        self.opener.open.assert_called_once()

    def test_bad_token_safe_receipt(self):
        self.token.write_text("secret-oauth-token\nextra", encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "supplied file"):
            self.send()
        self.assertEqual(self.read_receipt()["status"], "credential_error")
        self.opener.open.assert_not_called()

    def test_redirect_rejected_by_handler_and_recorded(self):
        handler = cli.RejectRedirects()
        request = urllib.request.Request("https://androidpublisher.googleapis.com/")
        self.assertIsNone(handler.redirect_request(request, None, 302, "Found", {}, "https://evil.test/"))
        self.opener.open.side_effect = urllib.error.HTTPError(request.full_url, 302, "Found", {}, io.BytesIO())
        self.assertFalse(self.send())
        self.assertEqual(self.read_receipt()["httpStatus"], 302)
        self.opener.open.assert_called_once()


if __name__ == "__main__":
    unittest.main()
