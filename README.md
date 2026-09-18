# FRPM

FRPM 是一个用于集中管理第三方 FRP 服务的 Web 控制台。你可以在一个页面中管理多个供应商账号、创建和运行隧道、切换 CLI 版本并查看运行日志。

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

FRPM 基于 [ASP.NET Core](https://dotnet.microsoft.com/apps/aspnet) 构建，界面采用 [MudBlazor](https://mudblazor.com/)。

Copyright © 2026 FRPM Contributors。项目源码、问题反馈和发布记录均可在 [GitHub](https://github.com/ling921/frpm) 查看。

## Docker 部署

镜像地址：[tomcn/frpm](https://hub.docker.com/r/tomcn/frpm)

使用仓库中的 `docker-compose.yml` 启动：

```bash
docker compose pull
docker compose up -d
```

打开 `http://localhost:8080`。

### 初始账号

```text
用户名：admin
密码：Frpm@123
```

首次登录后必须修改默认密码。初始管理员只会在全新数据库中创建一次。

如果管理员无法登录且未配置邮件服务，请在运行 FRPM 的服务器上执行：

```bash
dotnet Frpm.dll --reset-admin-password
```

该命令会输出随机临时密码、使已有会话失效，并要求下次登录后修改密码。请只在受信任的本地终端执行，且不要将命令输出写入日志。

## 第一次使用

1. 使用初始管理员登录并修改密码。
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
dotnet run --project src/Frpm/Frpm.csproj
```

运行测试：

```bash
dotnet test Frpm.slnx
```

## 许可证

本项目依据 [Apache License 2.0](LICENSE) 发布。
