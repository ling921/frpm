# FRPM Docker 镜像

FRPM 是一个用于集中管理第三方 FRP 服务的 Web 控制台，支持 SakuraFrp、LoliaFrp 与 MeFrp。通过本镜像可在服务器上管理供应商账号、隧道、`frpc` CLI 版本及运行日志。

完整的原生部署、配置与开发文档请参见 [GitHub 仓库](https://github.com/ling921/frpm)。

## 快速开始

推荐使用仓库中的 `docker-compose.yml`：

```bash
curl -O https://raw.githubusercontent.com/ling921/frpm/master/docker-compose.yml
docker compose up -d
```

也可以直接运行：

```bash
docker run -d \
  --name frpm \
  --restart unless-stopped \
  -p 8180:8180 \
  -v frpm-data:/data \
  --add-host host.docker.internal:host-gateway \
  tomcn/frpm:latest
```

打开 `http://服务器地址:8180`。首次登录账号为 `admin`，初始密码为 `Frpm@123`；请立即修改密码。

## 镜像标签

- `latest`：最新正式版本。
- `1.0.0`：固定的 FRPM 1.0.0 版本。

生产环境建议固定使用具体版本标签，验证后再升级。

## 数据持久化

请始终挂载 `/data`：其中保存 SQLite 数据库、供应商凭据的加密密钥、CLI 文件与运行日志。

> [!IMPORTANT]
> 备份或迁移时必须同时保留整个 `/data`。删除卷或丢失其中的密钥后，已保存的供应商凭据无法恢复。

不要执行 `docker compose down -v`，除非确定要永久删除所有 FRPM 数据。

## 常用配置

| 环境变量 | 默认值 | 用途 |
| --- | --- | --- |
| `ASPNETCORE_URLS` | `http://+:8180` | 容器内监听地址与端口。 |
| `ASPNETCORE_FORWARDEDHEADERS_ENABLED` | `true` | 反向代理后正确识别原始协议和地址。 |
| `ConnectionStrings__DefaultConnection` | `Data Source=/data/frpm.db` | SQLite 数据库位置。 |
| `Frpm__Storage__DataDirectory` | `/data` | CLI、日志和密钥的保存目录。 |
| `Frpm__Storage__LogRetentionDays` | `30` | 日志保留天数。 |
| `Frpm__Storage__MaxLogBytes` | `536870912` | 日志最大总容量，单位为字节。 |
| `Frpm__Storage__MaxPackageBytes` | `268435456` | 单个 CLI 包最大大小，单位为字节。 |

示例：将宿主机 9000 端口映射到容器的默认 8180 端口：

```bash
docker run -d -p 9000:8180 -v frpm-data:/data tomcn/frpm:latest
```

## 容器访问宿主机服务

若隧道需要访问 Docker 宿主机上的服务，本地地址请填写 `host.docker.internal`，并保留 Compose 中的：

```yaml
extra_hosts:
  - "host.docker.internal:host-gateway"
```

容器内的 `127.0.0.1` 指向 FRPM 容器自身，不是宿主机。

## 健康检查与许可证

健康检查地址为 `/health`。镜像基于 Apache License 2.0 发布；详见仓库中的 [LICENSE](https://github.com/ling921/frpm/blob/master/LICENSE)。
