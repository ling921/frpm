# Microsoft Store 桌面版本

产品 ID：`9NQHDN7WR30L`。商店测试版独立递增（本次 `0.1.2.0`），测试完成后再统一正式发布流程。不要递增四段版本号的 revision。

## 手动生成商店测试包

在 GitHub Actions 中选择 **Build Microsoft Store Package**，点击 **Run workflow**：

- `version`：本次填写 `0.1.2`，会生成 `0.1.2.0` 包，程序版本也使用 `0.1.2`。
- `source_ref`：选择含原生桌面代码的分支、标签或提交，当前为 `codex/windows-installer`。

工作流运行测试、发布 Windows x64 桌面程序，并用 MakeAppx 生成 `.msixupload`。成功后在运行页面的 Artifacts 下载 `frpm-0.1.2-win-x64-msixupload`，解压后自行上传 Partner Center。Artifact 保留 30 天。

此流程只生成商店包，不创建 Git 标签、GitHub Release，也不上传商店。原有公开发布流程保持独立；测试完成后再整合。已有 GitHub `v1.0.0`，未来整合时需使用新的可用标签，不能覆盖旧版本。

## 架构与能力

Store 包运行 Avalonia 原生桌面界面，直接调用公共业务程序集。管理供应商账号、创建和运行隧道、安装 CLI 和读取日志均不使用 Blazor、WebView 或本机管理网站。

清单只声明 `runFullTrust`，不声明 `packagedServices`、`localSystemServices` 或 `windows.service`。登录启动项运行同一应用的后台入口；用户关闭窗口时隐藏到托盘，明确退出或注销会停止隧道。用户可以在 Windows 启动应用设置中关闭登录启动。

## runFullTrust 说明草稿

FRPM is a native Avalonia desktop application that helps users manage their own FRP provider accounts and tunnels. It needs runFullTrust to download and launch the provider's frpc command-line client as a child process, supervise its lifetime, and display its output in the desktop UI. The application runs with the signed-in user's permissions and does not install a Windows service or request administrator elevation.

The tunnel client connects only to the FRP provider and local endpoint configured by the user. Starting and stopping tunnels, replacing an installed CLI, and changing active CLI versions are explicit user actions. Downloaded packages are validated by the existing archive and checksum checks. Sensitive credentials are encrypted with the application's persisted data-protection keys, and sensitive values are redacted from tunnel process logs. Closing the main window leaves a visible tray entry with options to reopen the app or exit and stop its tunnels.

FRPM writes its database, encryption keys, installed CLI packages, generated tunnel configurations and application logs under the current user's FRPM application-data directory. The user can explicitly choose another data directory or export logs to a selected file. A temporary loopback listener is used only while the user completes browser OAuth authorization; it validates the request state and releases its port when authorization finishes or is cancelled. The capability will be used only for these stated functions.

Developer website and support: https://github.com/ling921/frpm and https://github.com/ling921/frpm/issues.

## 重新提交前

- 更新商店描述和截图，使其展示原生桌面界面，并移除旧的 Windows 服务和管理端口说明。
- 在 Properties 的 Support info 中设置 GitHub 项目或 Issues 链接。
- 用新包替换被拒的旧包，确认校验信息中已没有 `packagedServices`。
- 在 Submission options 中使用上述实际能力说明。是否通过认证仍由 Microsoft 判断。
