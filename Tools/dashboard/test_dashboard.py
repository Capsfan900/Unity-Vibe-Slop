import unittest

from Tools.dashboard import build_dashboard as dashboard


class DashboardTests(unittest.TestCase):
    def test_current_and_history_are_separated(self):
        self.assertEqual("Level Building", dashboard.categorize_doc("docs/LEVEL-EDITOR.md"))
        self.assertEqual("Archive", dashboard.categorize_doc("docs/plans/old-plan.md"))
        self.assertEqual("Archive", dashboard.categorize_doc("docs/ENGINEERING-LOG.md"))


if __name__ == "__main__":
    unittest.main()
