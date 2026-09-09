import sys
import unittest
from pathlib import Path
from typing import get_args
from pydantic import ValidationError
sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'app'))
from studio_server import Settings, OutputSize, engine

class SettingsTests(unittest.TestCase):
    def test_obsolete_resize_inputs_rejected(self):
        for fields in ({'width':640}, {'height':480}, {'scale':2}, {'size':'×2'}, {'size':'自定义'}, {'size':'8K (7680×4320)'}):
            with self.subTest(fields=fields), self.assertRaises(ValidationError):
                Settings(asset_id='test', **fields)

    def test_defaults_and_named_sizes(self):
        settings = Settings(asset_id='test')
        self.assertEqual(settings.skin, 1)
        self.assertEqual(settings.global_tone, 1)
        self.assertEqual(settings.size, '原始尺寸（不放大）')
        self.assertEqual(len(get_args(OutputSize)), 5)
        for size in get_args(OutputSize):
            self.assertEqual(Settings(asset_id='test', size=size).size, size)

    def test_portrait_and_non_widescreen_preserve_proportions(self):
        for source, expected in (((1920,1080),(3840,2160)), ((2160,3840),(2160,3840)), ((1448,1086),(2880,2160)), ((1000,1000),(2160,2160))):
            with self.subTest(source=source):
                self.assertEqual(engine.fit_box(*source, '3840x2160'), expected)

if __name__ == '__main__':
    unittest.main()
