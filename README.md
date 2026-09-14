# DLSS-Studio-Windows

面向 Windows 的 DLSS 图片与视频增强工作台，提供中文界面、深浅主题与无边框独立桌面窗口。

> 本项目原创部分采用 MIT 许可；上游代码及第三方组件不在本项目重新授权范围内，详见 [许可说明](THIRD-PARTY-NOTICES.md)。

## 功能

- DLSS 5 图片与视频增强，自动识别显卡并加载适配组件，无需手动选择模型。
- 图片原图、结果与拖动对比；视频同步播放对比，时间轴起止游标选择片段，支持整段或所选片段下载。首次打开显示上传区。
- 预览支持适应窗口、100% 原像素、滚轮缩放与键盘/鼠标平移；视频的 100% 指当前预览文件的像素，预览分辨率可能小于下载结果。
- 仅提供原始、720p、1080p、2K（1440p）、4K，保持素材比例；原始保留原尺寸。
- 高级强度显示百分比，保留 0%–200% 调节范围（色彩融合为 0%–100%），提供恢复默认及参数悬停说明。
- 深浅主题与独立窗口玻璃效果：普通窗口开启玻璃时，面板透明并模糊背景，间隙与边框保持原样；最大化时关闭玻璃并铺满黑色或白色底层，还原后恢复玻璃设置。主界面适配窗口和 200% 显示缩放，窄屏使用设置抽屉。
- `DLSS Studio.exe`：内嵌 WebView2，直接显示完整工作台，无系统边框；支持拖动、调整大小、最小化、最大化/还原和关闭。
- 播放、静音、时间轴与时间收纳为单行底部控制栏，鼠标移到画面底部时显示；键盘与触屏可操作。
- 处理状态、进度和提示统一显示在底部；支持取消任务、断连重试和刷新恢复。
- 保存自己的增强设置；最近结果支持重命名、收藏、重新打开和下载，清理缓存时保护收藏结果。
- 独立窗口右上角“浏览器”可打开同一 Web 服务。关闭独立窗口会停止服务；外部浏览器与独立窗口分别保存当前工作台，共用处理队列与最近结果。
- 默认端口被占用时自动选择可用端口，仅监听 `127.0.0.1`。
- 按旋转信息正确处理竖拍视频；导出参数按编码显示，检查显卡编码可用性，不可用时默认采用 ProRes CPU 编码。

![独立桌面窗口](docs/desktop.png)



## 下载与使用

当前版本：[v1.2.0](https://github.com/LeoSasion/DLSS-Studio-Windows/releases/tag/v1.2.0)。下载 Release 附件中的 `DLSS-Studio-Windows-x64.zip`，内含已编译 EXE 与运行环境；GitHub 自动生成的 Source code 压缩包仅包含源码。附件同时提供 SHA-256 校验文件。

获取发行包后，完整解压 ZIP，双击 `DLSS Studio.exe` 直接进入独立窗口。需要外部浏览器时，点击窗口右上角“浏览器”，并保持独立窗口开启或最小化。Python 运行环境随发行包提供。独立窗口还使用 Microsoft WebView2；缺少时可选择通过包内微软引导程序联网安装。

从旧版升级时，将新版解压到新文件夹。需要保留素材和历史结果时，先退出旧版，再将旧目录的 `app/uploads/`、`app/ui_out/` 和已有的 `app/studio-state.json` 一起复制到新目录。v1.0.0 没有任务索引，旧文件可保留，但不会自动出现在「最近结果」中。不要用旧版程序文件覆盖新版。

### 浏览器访问地址与端口

启动 `DLSS Studio.exe` 后，默认浏览器地址为 **http://127.0.0.1:7860/**。

- 若 7860 已被占用，启动器会在 **7860–7959** 内自动寻找空闲端口，例如 7861、7862。
- 点击独立窗口右上角的 **“浏览器”**，会打开当前实例的实际地址；不要固定使用旧标签页的端口。
- 服务仅监听本机 `127.0.0.1`，不对局域网开放。使用浏览器时需保持 EXE 开启或最小化，关闭 EXE 会停止它启动的服务。
- 手动运行 `start-web.bat` 默认使用 7860；自动避让端口是 EXE 启动器提供的功能。

Windows x64；DLSS 神经渲染需要兼容的 NVIDIA 显卡与驱动。CPU 编码仅负责视频编码，不能替代神经渲染所需的显卡。

## 源码运行

1. 安装 Python 3.10 x64，在项目根目录创建环境：`python -m venv app/.venv`。
2. 安装锁定依赖：`app\.venv\Scripts\python.exe -m pip install -r app/requirements-installed.txt`。
3. 从上游项目自行取得所需处理引擎及 FFmpeg，保持上游发布目录结构，将它们放到 `app/out/`。源码仓库不含这些二进制文件。
4. 运行 `app\.venv\Scripts\python.exe packaging/fetch_dlss5.py` 下载并校验三套 DLL。解包 RAR 需要安装 UnRAR / WinRAR，仅构建时需要。
5. 双击 `start-web.bat`。

原始依赖项目：[DaniilSokolyuk/video2dlssnr](https://github.com/DaniilSokolyuk/video2dlssnr)。请参阅上游的运行要求及适用条款。

## 目录

| 目录或文件 | 用途 |
| --- | --- |
| `app/web/` | 中文前端与主题 |
| `app/studio_server.py` | 上传、进度、取消、历史记录与下载 API |
| `app/studio_storage.py`、`app/process_runner.py` | 任务持久化与处理进程管理 |
| `app/app.py`、`app/nr_video.py`、`app/render_engine.py` | 上游代码及本地适配，许可待确认 |
| `packaging/DesktopShell.cs` | 无边框 WebView2 桌面窗口 |
| `packaging/StudioService.cs` | 服务进程管理 |
| `packaging/` | 便携包构建脚本与使用说明 |

上传素材保存在 `app/uploads/`，结果保存在 `app/ui_out/`，任务索引保存在 `app/studio-state.json`。以上数据、浏览器配置、日志、Python 运行环境和发行 ZIP 均排除在源码仓库之外。

## 验证情况

27 项自动化测试覆盖显卡匹配、参数边界、任务取消和恢复、进程树清理、缓存保留及旋转视频尺寸。Windows 工作站另行验证独立解压、包内 Python 依赖加载、EXE 服务启停、端口占用自动切换，以及真实图片增强和短视频 ProRes 导出。

独立窗口完成 25 项工作台流程检查，覆盖同步视频对比、时间轴片段范围、播放控制栏、缩放和平移、断连重试、刷新恢复、历史下载、设置复用、收藏保护等；另行验证深浅主题、玻璃持续显示与背景更新、边框不变、最大化/还原、200% 显示缩放及正常退出。实机为 RTX PRO 6000 Blackwell；30 / 40 系、其他驱动和长视频未全部实测。

## 来源和许可

这不是 NVIDIA 官方项目。DLSS 等名称归相应权利人所有。

上游代码、NVIDIA DLL、FFmpeg、Python、Python 依赖和 Phosphor 图标分别受其适用条款约束。未对第三方组件统一套用 MIT 或其他新许可证。

本项目原创的前端、服务桥接和 EXE 启动器采用 **MIT License**，见 [LICENSE](LICENSE)。授权范围及第三方归属见 [许可说明](THIRD-PARTY-NOTICES.md)。

## 构建与验证

准备好依赖环境与 `app/out/` 后，执行 `app\.venv\Scripts\python.exe packaging/build_package.py`，再执行 `app\.venv\Scripts\python.exe packaging/archive_package.py`。构建会从微软 NuGet 获取固定版本的 WebView2 SDK，并验证微软引导安装器签名。

运行 `app\.venv\Scripts\python.exe -B -m unittest discover -s tests -p "test_*.py"` 执行自动化测试；其中视频几何测试需要 `app/out/` 下的 FFmpeg 和 FFprobe。

`packaging/verify_package.py` 会独立解压发行包，验证所有校验值，并测试 EXE 的服务管理及桌面窗口，包括真实图片/视频处理、恢复与历史结果工作流、窗口控制和关闭清理。该验证需要兼容显卡，结果写入 `outputs/package-verification.json`。重建已有暂存包时应先保留或移走 `packaging/build/DLSS-Studio-Portable/`，构建脚本会拒绝覆盖非空目录。

## 自动显卡适配

统一使用 DLSS 5 神经渲染。旧 E/F/J/K/L/M 是内部 SR 预设，并非分别验证过的 DLSS 5 模型，现已移除；内部放大阶段保持驱动默认预设。

自动识别 RTX 30 / 40 / 50 系及可识别的同架构专业卡，通过 DXGI 高性能排序选择显卡，使用 CUDA LUID 核对架构，显式传入 DXGI 编号和对应 DLL 目录。每次提交任务前重新识别，避免多卡或热插拔造成编号混淆。运行时三套组件已内置，无需手动替换。未知显卡、缺失或校验失败的组件会显示原因，不会猜测其他架构 DLL。

三套 DLL 来自 [purkatyy/DLSS5- 的 dlss 发布](https://github.com/purkatyy/DLSS5-/releases/tag/dlss)。下载地址、附件及 DLL 的 SHA-256 记录在 `app/dlss5-components.json`；仅提取各包中的 `nvngx_dlssnr.dll`。它们不属于本项目的 MIT 授权范围，原有版权及适用条款继续有效。

已在 RTX PRO 6000 Blackwell 工作站使用 50 系 DLL 实测。30 / 40 系已有型号识别、显卡编号、组件匹配的自动化测试，尚未在实体卡上验证渲染。原发布者也注明 30 / 50 系替换包未经其测试。界面区分“已匹配”和本次启动“已成功渲染”。
