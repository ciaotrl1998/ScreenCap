# ScreenCap 本地更新

- 每次修改 ScreenCap 程序后递增补丁版本。同步更新 `src/ScreenCap/Core/VersionInfo.cs`、`src/ScreenCap/ScreenCap.csproj` 和 `dist/installer/ScreenCap.iss` 中的版本号。
- 构建 Release，把构建出的 `ScreenCap.exe` 同步到 `update/ScreenCap.exe`，更新 `version.json` 中的版本、SHA-256 和更新说明。
- 从运行进程确认托盘实例的可执行文件路径。等正在进行的录制或导出完成，从托盘正常退出，更新该路径下的程序并重启。验证新进程使用同一路径及本次构建的版本。
- 将源码、更新包和更新清单一并提交并推送到自动更新使用的 Git 分支。确认远端清单的版本和更新包的 SHA-256 一致。
- 在 Windows 上使用 PowerShell 7（`pwsh`）。
