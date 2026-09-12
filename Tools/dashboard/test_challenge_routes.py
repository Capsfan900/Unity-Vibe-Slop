"""Focused fixture for current and legacy Challenge Route level-map parsing."""

import importlib.util
import tempfile
import unittest
from pathlib import Path


SCRIPT = Path(__file__).with_name("build_dashboard.py")
SPEC = importlib.util.spec_from_file_location("dashboard_builder", SCRIPT)
dashboard = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(dashboard)


def asset_with(route_key):
    return """displayName: Fixture
  playerStart: {x: 0, y: 0, z: 0}
  platforms:
  - name: Deck
    center: {x: 0, y: 0, z: 0}
    size: {x: 1, y: 1, z: 1}
  $ROUTE_KEY:
  - routeId: T1_Challenge_Flare
    entryCenter: {x: 0, y: 5, z: 37}
  - routeId: T2_Challenge_Flare
    entryCenter: {x: 0, y: 13, z: 149}
  - routeId: T3_Challenge_Flare
    entryCenter: {x: 0, y: 25, z: 276}
""".replace("$ROUTE_KEY", route_key)


class ChallengeRouteDashboardTests(unittest.TestCase):
    def parse_asset(self, route_key):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            asset = root / "Assets" / "Data" / "Levels" / "Fixture.asset"
            asset.parent.mkdir(parents=True)
            asset.write_text(asset_with(route_key), encoding="utf-8")
            original_root = dashboard.ROOT
            dashboard.ROOT = root
            try:
                return dashboard.level_map()
            finally:
                dashboard.ROOT = original_root

    def test_current_challenge_routes_are_normalized_for_the_level_map(self):
        levels = self.parse_asset("challengeRoutes")
        self.assertEqual(["T1_Challenge_Flare", "T2_Challenge_Flare", "T3_Challenge_Flare"],
                         [route["routeId"] for route in levels[0]["challengeRoutes"]])
        self.assertNotIn("insightRoutes", levels[0])

    def test_legacy_routes_normalize_under_the_current_key(self):
        levels = self.parse_asset("insightRoutes")
        self.assertEqual(["T1_Challenge_Flare", "T2_Challenge_Flare", "T3_Challenge_Flare"],
                         [route["routeId"] for route in levels[0]["challengeRoutes"]])
        self.assertNotIn("insightRoutes", levels[0])


if __name__ == "__main__":
    unittest.main()
