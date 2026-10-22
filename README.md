# ScreenCap

Windows 10/11 屏幕录制与 GIF 工具：

- 全局快捷键 `Alt+Shift+Q` 框选区域录制，托盘左键单击也可开始。
- 支持任意显示器、框选后拖动边框调整区域，选区右键取消。
- 录制时可选择「GIF / 视频」，视频为 H.264 MP4，支持系统声音。
- 内置剪辑窗：时间轴裁剪、GIF 导出、复制视频到剪贴板。
- 导出时可自选目录和文件名，并记住上次使用的目录。
- 启动时检查主分支上的新版本，发现更新可一键下载、替换并重启，无需重新安装。
- 设置（快捷键、画质、录制声音）持久化到 `%LOCALAPPDATA%\ScreenCap\settings.json`。

## 构建

环境：Windows、.NET SDK（支持 net48 的 Windows 桌面开发负载）。

```powershell
dotnet build src\ScreenCap\ScreenCap.csproj -c Release
```

输出：

```
src\ScreenCap\bin\x64\Release\net48\ScreenCap.exe
```

运行依赖：

- `libvlc`、视频预览所需 DLL 由 NuGet 包 `VideoLAN.LibVLC.Windows` 输出到构建目录。
- `tools\ffmpeg.exe` 用于 GIF/视频导出。仓库不提交该文件，可执行 `tools\get-ffmpeg.ps1` 下载，或自行放置 ffmpeg.exe。
- 导出需要系统安装 Microsoft Visual C++ 2015-2022 运行库（ScreenRecorderLib 依赖）。

## 自动更新

更新源是仓库 `main` 分支：

- `version.json`：最新版本号、更新包地址、SHA-256、更新说明。
- `update/ScreenCap.exe`：最新构建的程序本体。

程序启动约 2.5 秒后请求：

```
https://raw.githubusercontent.com/ciaotrl1998/ScreenCap/main/version.json
```

若远端版本高于当前内置版本，会弹出更新提示。点击更新后：

1. 下载 `update/ScreenCap.exe` 并校验 SHA-256；
2. 当前程序退出；
3. 临时 PowerShell 更新器等待进程退出，替换 `ScreenCap.exe` 并重新启动新版本。

因此后续更新只需下载一个 exe，**不需要重新运行安装包**。安装版和便携版都适用（程序目录需可写）。

### 发布新版本

1. 修改版本号：
   - `src/ScreenCap/Core/VersionInfo.cs` 的 `VersionInfo.Current`
   - `src/ScreenCap/ScreenCap.csproj` 的 `<Version>` / `<FileVersion>` / `<AssemblyVersion>`
   - `dist/installer/ScreenCap.iss` 的 `MyAppVersion` 与 `VersionInfo*`
2. 构建：

   ```powershell
   dotnet build src\ScreenCap\ScreenCap.csproj -c Release
   ```

3. 复制新的 exe 并计算 SHA-256：

   ```powershell
   Copy-Item "src\ScreenCap\bin\x64\Release\net48\ScreenCap.exe" "update\ScreenCap.exe" -Force
   (Get-FileHash "update\ScreenCap.exe" -Algorithm SHA256).Hash.ToLowerInvariant()
   ```

4. 更新 `version.json` 的 `version`、`sha256`、`notes`，提交并推送到 `main`。
5. 已安装的旧版本下次启动（或托盘菜单「检查更新」）即可收到提示。

本地验证自动更新时，可用环境变量覆盖版本清单地址：

```powershell
$env:SCREENCAP_UPDATE_VERSION_URL = "file:///C:/path/to/version.json"
```

该变量仅用于测试，正常运行时不需要设置。

## 安装包

安装包使用 Inno Setup 6 编译：

```powershell
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" "dist\installer\ScreenCap.iss"
```

脚本默认按用户安装到 `%LOCALAPPDATA%\Programs\ScreenCap`，并读取 `dist\ScreenCap-portable` 作为打包目录。打包前需要把构建输出复制到该目录（含 `libvlc`、`tools\ffmpeg.exe`、`Assets` 等）。

## 目录说明

```
src/ScreenCap/          程序源码
  Capture/              框选、边框、控制栏、录制
  Core/                 设置、日志、钩子、原生 API、版本与更新信息
  Services/             导出、剪贴板、自动更新
  UI/                   剪辑窗、设置窗、托盘、时间轴
update/ScreenCap.exe    自动更新分发的程序
version.json            自动更新清单
tools/                  ffmpeg 获取脚本（ffmpeg.exe 不提交）
dist/installer/         安装包脚本
```

## 说明

- 更新检查只访问上述 GitHub 地址，不上传任何本地数据。
- 设置、日志、缓存均位于 `%LOCALAPPDATA%\ScreenCap`。
- 录制缓存会在启动/退出时清理；关闭剪辑窗会删除对应缓存源文件。
