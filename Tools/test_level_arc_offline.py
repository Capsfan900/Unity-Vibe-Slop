import unittest

from Tools import level_arc_offline as arc


class LevelArcOfflineTests(unittest.TestCase):
    def test_platform_parser_accepts_nested_metadata_before_name(self):
        text = """  platforms:\n  - meta:\n      objectId: T0.Platform.01\n    name: Ground_Start\n    center: {x: 0, y: 0, z: 0}\n    size: {x: 10, y: 1, z: 20}\n  spawns:\n"""
        boxes = arc.parse_boxes(text)
        self.assertEqual(1, len(boxes))
        self.assertEqual("Ground_Start", boxes[0].name)


if __name__ == "__main__":
    unittest.main()
