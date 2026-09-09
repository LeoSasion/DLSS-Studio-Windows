# 许可范围与第三方组件

## 本项目原创部分：MIT

项目维护者已选择 MIT 许可，全文见 [LICENSE](LICENSE)。该许可适用于 DLSS-Studio-Windows 贡献者原创的界面、服务桥接、启动器、构建脚本和文档，包括：

- `app/web/index.html`、`app/web/studio.css`、`app/web/dark-polish.css`、`app/web/studio.js`。
- `app/studio_server.py`、`app/serve_local.py`、`app/ui_style.py`、`app/test_studio_server.py`。
- `start_studio.py`、`start-web.bat`。
- `packaging/Launcher.cs`、本项目原创的打包与验证脚本。
- 本项目原创文档；不包括其中引用的第三方文本。

上述 MIT 许可仅授予本项目贡献者有权授予的权利，不改变以下第三方内容的许可，也不为包含它们的整个发行包授予统一的 MIT 许可。

## 上游代码和引擎

来源：[DaniilSokolyuk/video2dlssnr](https://github.com/DaniilSokolyuk/video2dlssnr)。本地依据提交 `55a4ceb588a419b9b56497aa0b563d0c9e2b6c77` 和 v1.3 发行包进行适配。

`app/app.py`、`app/nr_video.py` 包含上游代码；`source/` 为上游检出目录。它们不作为本项目完整原创文件纳入上述 MIT 授权。检查时上游未声明 LICENSE，相关公开再分发许可仍待确认。

`app/out/video2dlssnr.exe` 和 NVIDIA NGX / DLSS DLL 不属于本项目的 MIT 授权范围。原有版权与适用条款继续有效。

## 其他第三方内容

- **Phosphor 图标**：保留其独立 MIT 许可和原作者版权声明，见 `app/web/assets/icons/LICENSE`。
- **Python**：遵循其自带许可，便携包中见 `runtime/LICENSE.txt`。
- **Python 依赖**：各自适用其许可证，见便携包 `runtime/Lib/site-packages/*dist-info/`。
- **FFmpeg / FFprobe**：适用所捆绑构建的许可及分发要求，便携包中保留 `licenses/ffmpeg-license.txt`。
- **示例图片**：本项目制作流程中生成的演示素材，不属于上游渲染结果。

本项目并非 NVIDIA 官方产品。MIT 许可不授予第三方商标权。
