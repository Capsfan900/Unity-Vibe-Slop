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

    def test_level_map_groups_objects_under_zone(self):
        fixture = """  zones:\n  - zoneId: T2\n    canonicalName: Helix Tower\n    splitName: Knight\n    center: {x: 0, y: 10, z: 200}\n    size: {x: 40, y: 30, z: 100}\n  spawns:\n  - meta:\n      objectId: T2.Sentry.02\n      zoneIdOverride: T2\n    prefabKey: pshooter_enemy01\n    position: {x: 10, y: 20, z: 210}\n"""
        level = dashboard.parse_level_asset(fixture)
        self.assertEqual("Knight", level["zones"]["T2"]["split"])
        self.assertEqual("pshooter_enemy01", level["objects"]["T2.Sentry.02"]["dataKey"])

    def test_only_timing_capture_action_is_allowed(self):
        self.assertEqual(dashboard.timing_capture_dir(), dashboard.resolve_action("open-timing-captures"))
        with self.assertRaises(ValueError):
            dashboard.resolve_action("../../Windows")


if __name__ == "__main__":
    unittest.main()
