import unittest
import tempfile
from pathlib import Path

from Tools.dashboard import build_dashboard as dashboard


class DashboardTests(unittest.TestCase):
    def test_current_and_history_are_separated(self):
        self.assertEqual("Level Building", dashboard.categorize_doc("docs/LEVEL-EDITOR.md"))
        self.assertEqual("Archive", dashboard.categorize_doc("docs/plans/old-plan.md"))
        self.assertEqual("Archive", dashboard.categorize_doc("docs/ENGINEERING-LOG.md"))

    def test_audit_finds_broken_link_and_current_insight_term(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "docs").mkdir()
            (root / "docs" / "X.md").write_text("[bad](missing.md) Insight route", encoding="utf-8")
            findings = dashboard.audit_docs([
                {"path": "docs/X.md", "title": "X", "category": "Level Building", "text": "[bad](missing.md) Insight route"}
            ], root)
        self.assertIn("broken-local-link", {f["code"] for f in findings})
        self.assertIn("obsolete-current-term", {f["code"] for f in findings})


if __name__ == "__main__":
    unittest.main()
