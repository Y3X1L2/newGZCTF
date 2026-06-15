# GZCTF 镜像管理服务器部署指南

本目录用于把一台 Ubuntu Server 配置为 GZCTF 内网 Docker 镜像仓库。

平台当前的镜像使用流程是：

1. 管理员在 `/admin/images` 上传 `docker save` 导出的镜像归档，或注册已有镜像。
2. 主平台服务执行 `docker load`、`docker tag`、`docker push`。
3. 镜像被推送到 `DockerRegistrySettings:Address` 指向的内网 Registry。
4. 选手启动题目时，实际承载容器的本地或远程 worker 节点从该 Registry 拉取镜像。

源码不应写死测试网段地址。迁移环境时，只需要保持平台配置、worker Docker 配置和题目镜像模板地址一致。

## 推荐部署

假设镜像管理服务器地址是 `10.24.1.130`，允许 `10.24.0.0/16` 内的平台和 worker 访问：

```bash
sudo bash docs/registry-server/setup-gzctf-image-registry.sh \
  --host 10.24.1.130 \
  --port 5000 \
  --data-dir /var/lib/gzctf-registry \
  --allow-cidr 10.24.0.0/16 \
  --configure-local-insecure
```

默认 `--backend auto` 会优先使用 `registry:2` 容器；如果服务器无法访问 Docker Hub，可以指定使用 Ubuntu 源里的 `docker-registry` 服务：

```bash
sudo bash docs/registry-server/setup-gzctf-image-registry.sh \
  --host 10.24.1.130 \
  --backend apt \
  --allow-cidr 10.24.0.0/16 \
  --configure-local-insecure
```

脚本会执行以下工作：

- 安装或配置 Docker。
- 启动 `registry:2` 容器，或启动系统 `docker-registry` 服务。
- 将镜像数据保存到 `--data-dir`。
- 可选配置 `ufw` 放行 Registry 端口。
- 可选把本机 Docker 配置为信任该 HTTP Registry。

## 平台配置

在 GZCTF 主平台服务器的 `appsettings.json` 中配置：

```json
{
  "DockerRegistrySettings": {
    "Address": "10.24.1.130:5000",
    "Namespace": "ctf",
    "MaxUploadSizeGb": 10
  }
}
```

修改后重启平台：

```bash
sudo systemctl restart gzctf
```

## Worker 节点配置

每台运行 Docker 题目的 worker 都必须能访问 Registry。如果 Registry 使用 HTTP，需要在 worker 的 `/etc/docker/daemon.json` 中配置：

```json
{
  "insecure-registries": ["10.24.1.130:5000"]
}
```

然后重启 Docker 和 agent：

```bash
sudo systemctl restart docker
sudo systemctl restart gzctf-agent
```

平台的节点自动部署流程会读取 `DockerRegistrySettings:Address`，并把该地址写入新注册节点的 Docker insecure registry 配置。已经存在的旧节点需要重新注册，或手动补充上述配置。

## 验证

先在主平台服务器上推送一个测试镜像：

```bash
docker pull hello-world:latest
docker tag hello-world:latest 10.24.1.130:5000/ctf/smoke/hello-world:latest
docker push 10.24.1.130:5000/ctf/smoke/hello-world:latest
```

再在每台 worker 上验证拉取：

```bash
docker pull 10.24.1.130:5000/ctf/smoke/hello-world:latest
```

## 迁移注意事项

迁移到新网段时需要同步处理以下地址：

- `DockerRegistrySettings:Address`：改成新的镜像仓库地址。
- 所有 worker 的 `insecure-registries`：改成新的 Registry 地址。
- 已保存题目镜像模板的 `RegistryUrl`：如果仍指向旧 Registry，需要重新注册、重新上传，或批量修正数据库。
- 节点管理里的 worker `HostAddress`：应使用主平台、worker 和选手侧实际可达的地址。
- 防火墙和 FRP：平台与 worker 需要能访问 Registry 端口，选手需要能访问 worker 暴露的题目端口。

当前脚本默认搭建 HTTP Registry。正式环境如果需要账号密码或 HTTPS，建议后续升级为 Harbor，或给 `registry:2` 增加 TLS/Auth。启用认证前，还需要在主平台和每台 worker 上配置 `docker login`，否则平台推送和 worker 拉取会失败。
