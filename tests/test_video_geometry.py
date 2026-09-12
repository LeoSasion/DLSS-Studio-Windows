import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'app'))
import nr_video


class VideoGeometryTests(unittest.TestCase):
    def test_rotated_metadata_matches_real_decoded_frames(self):
        ffmpeg, ffprobe = str(ROOT/'app/out/ffmpeg.exe'), str(ROOT/'app/out/ffprobe.exe')
        with tempfile.TemporaryDirectory(prefix='dlss-rotation-') as directory:
            temp = Path(directory)
            for rotation in (0, 90, 180, 270):
                with self.subTest(rotation=rotation):
                    video, frame = temp/f'{rotation}.mp4', temp/f'{rotation}.png'
                    subprocess.run([ffmpeg,'-v','error','-display_rotation',str(rotation),'-i',
                        str(ROOT/'tests/fixtures/video-compare.mp4'),'-c','copy',str(video)],check=True,capture_output=True)
                    src = nr_video.probe(ffprobe,str(video))
                    subprocess.run([ffmpeg,'-v','error','-i',str(video),'-vf',f'scale=flags={nr_video.SWS},format=rgba',
                                    '-frames:v','1',str(frame)],check=True,capture_output=True)
                    with Image.open(frame) as image:
                        self.assertEqual((src['w'],src['h']),image.size)
                    expected = (1080,1920) if rotation%180 else (1920,1080)
                    self.assertEqual(nr_video.output_size(src,0,0,1,'1920x1080'),expected)


if __name__ == '__main__': unittest.main()
