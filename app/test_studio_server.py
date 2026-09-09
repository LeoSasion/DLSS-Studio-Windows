"""Contract tests for the local upload/render/download boundary; no GPU required."""
import io
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch
from PIL import Image
from fastapi.testclient import TestClient
import studio_server as server


class StudioContractTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.uploads = self.root / "uploads"
        self.outputs = self.root / "outputs"
        self.uploads.mkdir()
        self.outputs.mkdir()
        self.patches = [patch.object(server, "UPLOADS", self.uploads), patch.object(server, "OUTPUTS", self.outputs)]
        for p in self.patches:
            p.start()
        server.assets.clear()
        server.jobs.clear()
        self.client = TestClient(server.api)

    def tearDown(self):
        for p in reversed(self.patches):
            p.stop()
        self.temp.cleanup()

    def upload(self):
        image = io.BytesIO()
        Image.new("RGB", (80, 60), "navy").save(image, "PNG")
        response = self.client.post("/api/upload", files={"file": ("photo.png", image.getvalue(), "image/png")})
        self.assertEqual(response.status_code, 200)
        return response.json()

    def test_upload_validation_and_geometry(self):
        bad = self.client.post("/api/upload", files={"file": ("bad.png", b"not an image", "image/png")})
        self.assertEqual(bad.status_code, 400)
        item = self.upload()
        self.assertEqual((item["width"], item["height"]), (80, 60))
        self.assertNotIn("path", item)
        self.assertEqual(self.client.get(item["url"]).status_code, 200)

    def test_reject_invalid_settings_and_unready_download(self):
        item = self.upload()
        response = self.client.post("/api/jobs", json={"asset_id": item["id"], "style": "unknown"})
        self.assertEqual(response.status_code, 400)
        response = self.client.post("/api/jobs", json={"asset_id": item["id"], "codec": "prores", "container": "mp4"})
        self.assertEqual(response.status_code, 400)
        self.assertEqual(self.client.get("/api/jobs/missing/download").status_code, 404)

    def test_image_and_video_parameter_mapping(self):
        item = self.upload()
        settings = server.Settings(asset_id=item["id"], size="自定义", width=1920, style="自然", intensity=.7)
        command = server.build_command(settings, server.assets[item["id"]], self.outputs)
        self.assertEqual(command[command.index("--nr-width") + 1], "1920")
        self.assertEqual(command[command.index("--nr-style") + 1], "1")
        settings = server.Settings(asset_id=item["id"], codec="prores", container="mov", frames=30, audio="none")
        command = server.build_command(settings, {**server.assets[item["id"]], "kind": "video"}, self.outputs)
        self.assertEqual(command[command.index("--codec") + 1], "prores")
        self.assertEqual(command[command.index("--frames") + 1], "30")
        self.assertEqual(command[command.index("--audio") + 1], "none")

    def test_renderer_failure_does_not_enable_download(self):
        item = self.upload()
        job = {"status": "queued", "log": ""}
        server.jobs["failed"] = job
        class FailedProcess:
            stdout = io.StringIO("NGX OutOfDate 0xBAD0000C\n")
            def wait(self):
                return 1
        with patch.object(server.subprocess, "Popen", return_value=FailedProcess()):
            server.render("failed", server.Settings(asset_id=item["id"]), server.assets[item["id"]])
        self.assertEqual(job["status"], "error")
        self.assertIn("616.56", job["message"])
        self.assertEqual(self.client.get("/api/jobs/failed/download").status_code, 404)

    def test_success_exposes_preview_and_exact_output(self):
        item = self.upload()
        server.jobs["success"] = {"status": "queued", "log": ""}
        class SuccessfulProcess:
            stdout = io.StringIO("done\n")
            def wait(self):
                Image.new("RGB", (160, 120), "green").save(self.outputs / "success" / "source_nr.png")
                return 0
        process = SuccessfulProcess()
        process.outputs = self.outputs
        with patch.object(server.subprocess, "Popen", return_value=process):
            server.render("success", server.Settings(asset_id=item["id"]), server.assets[item["id"]])
        response = self.client.get("/api/jobs/success").json()
        self.assertEqual(response["status"], "done")
        self.assertNotIn("result_path", response)
        preview = self.client.get(response["preview"])
        result = self.client.get(response["download"])
        self.assertEqual(preview.content, result.content)
        self.assertEqual(Image.open(io.BytesIO(result.content)).size, (160, 120))


if __name__ == "__main__":
    unittest.main()
