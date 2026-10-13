# FRPM

FRPM 用于集中管理第三方 FRP 服务。Windows、Linux 桌面版使用 Avalonia 原生界面；Docker 版提供 Blazor Web 控制台。两种界面复用公共业务逻辑，可以管理多个供应商账号、创建和运行隧道、切换 CLI 版本并查看日志。

目前支持：

- SakuraFrp
- LoliaFrp
- MeFrp

## 主要功能

- 添加多个供应商账号，并自动同步节点和隧道状态。
- 创建、编辑、启动、停止和删除隧道，并展示可访问的节点地址和映射地址。
- 在启动前检查本地服务是否可以访问。
- 根据服务器平台提供供应商感知的 `frpc` 下载建议，支持上传、最终文件直链安装和版本切换。
- 实时查看运行日志和失败原因。
- 自动恢复上次需要运行的隧道。
- 支持自动、浅色和深色主题，并保存个人配色。

## 致谢与版权

桌面界面采用 [Avalonia](https://avaloniaui.net/)，Docker Web 界面采用 [ASP.NET Core](https://dotnet.microsoft.com/apps/aspnet) 和 [MudBlazor](https://mudblazor.com/)。

Copyright © 2026 FRPM Contributors。项目源码、问题反馈和发布记录均可在 [GitHub](https://github.com/ling921/frpm) 查看。

## Docker 部署

镜像地址：[tomcn/frpm](https://hub.docker.com/r/tomcn/frpm)

使用仓库中的 `docker-compose.yml` 启动：

```bash
docker compose pull
docker compose up -d
```

打开 `http://localhost:8180`。

## 桌面版（Windows / Linux）

从 [Releases](https://github.com/ling921/frpm/releases) 下载自包含包，无需另行安装 .NET Runtime。桌面版直接管理本机隧道，不使用 Blazor、WebView 或本机管理网站，因此不需要设置管理端口，也无需登录 FRPM 管理员账号。

| 系统与架构 | 文件 |
| --- | --- |
| Windows x64 安装版 | `frpm-<版本>-win-x64-setup.exe` |
| Linux x64 | `frpm-<版本>-linux-x64.tar.gz` |
| Linux ARM64 | `frpm-<版本>-linux-arm64.tar.gz` |

Windows 安装器安装 Avalonia 桌面程序并提供开始菜单入口，可选择登录后在托盘中运行。升级会请求正在运行的桌面实例正常退出，再更新程序文件；旧的服务版升级时会停止并移除旧服务，旧数据仍保留。桌面版在当前用户会话中运行，用户注销后不继续运行隧道。

也可以从 [Microsoft Store 安装 FRPM](https://apps.microsoft.com/detail/9NQHDN7WR30L)。Store 包使用同一原生桌面程序，由 Microsoft Store 签名和更新；GitHub Release 附带 `frpm-microsoft-store.url` 作为商店入口。商店应用通过认证并发布前，该链接不会显示可安装版本。Store 版不注册 Windows 服务，清单仅保留原生桌面程序所需的 `runFullTrust`。

### 启动与托盘

Windows：从开始菜单打开 FRPM，或运行安装目录中的 `desktop/Frpm.Desktop.exe`。

Linux：

```bash
tar -xzf frpm-<版本>-linux-x64.tar.gz
cd FRPM
chmod +x Frpm.Desktop start.sh install-desktop.sh
./start.sh
```

将应用加入 Linux 桌面应用菜单，并可选在登录后运行：

```bash
./install-desktop.sh --autostart
```

关闭主窗口会隐藏到系统托盘，已启动的隧道继续运行。托盘菜单提供“打开 FRPM”“启动全部隧道”“停止全部隧道”“打开日志文件夹”和“退出 FRPM”。明确退出后停止本地隧道，下次启动会恢复之前需要运行的隧道。

Linux 托盘依赖 `StatusNotifierItem` / `AppIndicator`。没有托盘支持时，FRPM 会保留窗口；关闭时询问是否退出。GNOME 用户可能需要 AppIndicator 扩展，托盘检测还需要系统的 `dbus-send` 命令。已经隐藏的实例也可通过再次启动 FRPM 显示。

Linux 自包含包仍需要系统提供 ICU、X11 和字体库。使用完整桌面的 Debian / Ubuntu 通常已有这些依赖；精简系统请参考 [Avalonia 的 Linux 依赖说明](https://v11.docs.avaloniaui.net/docs/deployment/debian-ubuntu/)，安装对应系统版本的 `libicu`、`libx11-6`、`libice6`、`libsm6` 和 `libfontconfig1`。

### 数据目录与旧版导入

| 平台 | 默认目录 |
| --- | --- |
| Windows | `%LOCALAPPDATA%\FRPM\data` |
| Linux | `$XDG_DATA_HOME/frpm/data`，未配置时为 `~/.local/share/frpm/data` |

数据库、加密密钥、CLI 和日志保存在数据目录中，程序升级不会覆盖它。Windows Setup 卸载保留此目录；Microsoft Store 卸载的数据处理由系统管理，卸载前请自行备份。迁移或备份时必须保留整个目录，不能只复制数据库。

使用 `--data-dir` 可以指定另一个数据目录。旧压缩包版或 Windows 服务版的数据需要在第一次启动桌面版之前导入：先停止旧版 FRPM，然后运行：

```powershell
& "$env:ProgramFiles\FRPM\desktop\Frpm.Desktop.exe" --import-data "$env:ProgramData\FRPM\data"
```

Linux 示例：

```bash
./Frpm.Desktop --import-data /path/to/old/FRPM/data
```

导入会复制账号、加密密钥、CLI 和日志，并修正数据库中指向旧数据目录的 CLI / 日志路径；来源数据不变。目标目录必须没有既有数据，也可以用 `--data-dir /path/to/new-data` 指定新的空目标。导入完成后正常启动桌面程序。

### Linux 无界面运行

`./Frpm.Desktop --headless` 只运行公共隧道业务主机，不启动 Avalonia 窗口，也不提供 Web 管理页面。可以作为当前用户的 systemd 服务运行，无需 sudo：

```bash
./systemd/install-systemd.sh
systemctl --user status frpm
```

桌面窗口和无界面服务不能同时占用同一个数据目录。打开桌面界面前先执行 `systemctl --user stop frpm`，完成操作并退出桌面程序后可再启动该服务。安装脚本不再接受 `--port/-p`，原生运行时没有管理端口。需要浏览器远程管理时，请使用 Docker 版。

## Docker 初始账号

```text
用户名：admin
密码：Frpm@123
```

首次登录后必须修改默认密码。管理员只在新数据库中创建一次。忘记密码时，先停止容器，再使用相同数据卷运行重置命令：

```bash
docker compose stop frpm
docker compose run --rm --no-deps frpm dotnet Frpm.dll --reset-admin-password
docker compose up -d
```

命令输出随机临时密码，使已有会话失效，并要求下次登录后修改密码。不要将该输出公开或放入反馈日志。

## 第一次使用

1. 打开桌面程序；Docker 版则先使用初始管理员登录并修改密码。
2. 在“供应商”页面添加 SakuraFrp、LoliaFrp 或 MeFrp 账号。
3. 在“CLI 版本”页面上传或在线安装与服务器平台匹配的 `frpc`。
   - “官方下载建议”依据运行 FRPM 和 frpc 的服务器系统、架构推荐下载项，而不是浏览器所在的设备。
   - 请从供应商官方下载页下载后上传；“从链接安装”只接受安装包的最终 HTTP/HTTPS 文件直链，不能填写下载中心或 Release 页面。
   - LoliaFrp 的 Release 资产可附带 SHA-256；复制后填写到上传校验字段即可验证文件。
   - 每个供应商首次安装成功的 CLI 会自动激活。
   - 后续版本可以保留并按需切换。
4. 同步或创建隧道，然后点击启动。
5. 如果运行失败，可直接打开对应的实时日志查看原因。

> Docker 镜像运行于 Linux。上传到容器中的 CLI 必须是对应 CPU 架构的 Linux 版本，不能使用 Windows `.exe`。

## 常用命令

查看状态：

```bash
docker compose ps
```

查看日志：

```bash
docker compose logs -f frpm
```

更新版本：

```bash
docker compose pull
docker compose up -d
```

停止服务并保留数据：

```bash
docker compose down
```

## 数据保存

数据库、加密密钥、CLI 文件和运行日志都保存在 `frpm-data` 卷中。

> [!CAUTION]
> 不要使用 `docker compose down -v`，除非你确定要删除所有 FRPM 数据。加密密钥丢失后，已保存的供应商凭据将无法恢复。

## 容器访问宿主机

如果隧道需要访问 Docker 宿主机上的服务，本地地址请填写：

```text
host.docker.internal
```

`127.0.0.1` 在容器内指向 FRPM 容器自身，仅当服务与 FRPM 位于同一容器时才应使用它。若下拉列表未出现 `host.docker.internal`，请保留 Compose 中的 `extra_hosts: host.docker.internal:host-gateway`，或填写同一 Docker 网络中的服务名。

## 可选配置

`docker-compose.yml` 已包含可直接运行的默认值。以下环境变量可按需调整：

| 环境变量 | 默认值 | 用途 |
| --- | --- | --- |
| `ASPNETCORE_FORWARDEDHEADERS_ENABLED` | `true` | 在反向代理后正确识别原始协议和地址。 |
| `ConnectionStrings__DefaultConnection` | `Data Source=/data/frpm.db` | SQLite 数据库位置。 |
| `Frpm__Storage__DataDirectory` | `/data` | CLI、日志和密钥的保存目录。 |
| `Frpm__Storage__LogRetentionDays` | `30` | 日志保留天数。 |
| `Frpm__Storage__MaxLogBytes` | `536870912` | 日志最大总容量，单位为字节。 |
| `Frpm__Storage__MaxPackageBytes` | `268435456` | 单个 CLI 包最大大小，单位为字节。 |
| `Frpm__Storage__MaxArchiveExtractedBytes` | `1073741824` | CLI 压缩包解压后的最大总大小，单位为字节。 |
| `Frpm__Storage__MaxArchiveEntries` | `2000` | CLI 压缩包允许解压的最大文件条目数。 |

供应商同步间隔可在 Web 控制台的“设置”页面修改。

反向代理部署时，请正确传递 `X-Forwarded-Proto` 和 `X-Forwarded-Host`。使用 OAuth 登录供应商时，对外地址需要与供应商登记的回调地址一致。

健康检查地址：

```text
/health
```

## 本地运行

需要安装 .NET 10 SDK：

```bash
dotnet restore Frpm.slnx
dotnet run --project src/Frpm.Desktop/Frpm.Desktop.csproj
```

桌面开发入口是 `src/Frpm.Desktop`。如需开发 Docker Web 界面，运行 `dotnet run --project src/Frpm/Frpm.csproj`。

| 项目 | 职责 |
| --- | --- |
| `Frpm.Application` | 公共业务入口、数据库初始化、桌面 OAuth 回调与数据导入；桌面和 Web 主机共同引用。 |
| `Frpm.Infrastructure` | 供应商适配、加密凭据、SQLite、CLI 安装、同步和隧道进程管理。 |
| `Frpm.Abstractions` / `Frpm.Domain` | 公共模型、接口和领域类型；不依赖界面。 |
| `Frpm.Desktop` | Windows / Linux Avalonia 原生界面与托盘。 |
| `Frpm` / `Frpm.Client` | Docker 使用的 ASP.NET Core / Blazor 界面。 |

运行测试：

```bash
dotnet test Frpm.slnx
```

## 许可证

本项目依据 [Apache License 2.0](LICENSE) 发布。
