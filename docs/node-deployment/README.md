# GZCTF 节点部署简要指南

本目录用于沉淀远程计算节点的初始化流程。这里的“节点”指在管理后台 `/admin/nodes`
添加的远程服务器，例如 `10.0.7.125`。平台会通过 SSH 连接节点，部署
`gzctf-agent`，之后由 agent 在该节点上启动 Docker 容器或 KVM 虚拟机。

## 一、节点需要提前准备什么

每台远程节点至少需要：

- Linux 系统，建议 Ubuntu/Debian 系。
- Docker：用于普通 CTF、AWDP 的容器靶机。
- .NET / ASP.NET Core Runtime 10：用于运行 `gzctf-agent`。
- KVM/libvirt：用于 Windows 靶机、渗透测试靶机等虚拟机场景。
- 可从节点访问主平台：例如 `http://10.0.7.118:8080`。
- 可从主平台访问节点 agent 端口：默认 `5001/tcp`。

推荐初始化脚本：

```bash
sudo bash docs/node-deployment/setup-gzctf-worker-node.sh
```

如果要配置 Docker 私有仓库或镜像仓库挂载：

```bash
sudo bash docs/node-deployment/setup-gzctf-worker-node.sh \
  --insecure-registry 10.0.7.120:5000 \
  --registry-mirror https://registry-1.docker.io \
  --nfs-source 10.24.110.110:/data/nfs-pve/gzctf-images \
  --repo-dir /mnt/gzctf-image-repo
```

脚本不会安装 `gzctf-agent`。脚本执行完成后，在平台后台“节点部署”页面填写
节点 IP、用户名、密码，由平台自动下发 agent。

仓库根目录还保留了一个轻量入口：

```bash
sudo bash scripts/prepare-agent-node.sh --check-only
```

它适合快速检查或安装 Docker、.NET、KVM/libvirt 基础依赖；如果需要配置 Docker
私有仓库、registry mirror 或 NFS 镜像仓库，优先使用本目录下的
`setup-gzctf-worker-node.sh`。

## 二、当前项目里的镜像机制

### Docker 镜像

Docker 题目实际启动时，如果目标节点没有对应镜像，agent 会在该节点执行 pull。
因此远程节点不要求提前拥有所有 Docker 镜像，但必须满足其中一个条件：

- 节点能访问 Docker Hub 或指定私有 registry；
- 节点已经预拉取了对应镜像；
- 平台的镜像模板中配置了可访问的 registry 地址和必要的认证信息。

推荐做法是搭建局域网私有 registry 或 Harbor，把所有比赛镜像推到内网 registry。
题目配置里使用固定 tag 或 digest，例如：

```text
registry.ctf.lan/web/basic-sqli:20260610
```

比赛前可对常用镜像做预拉取，减少选手首次启动容器时的等待。

### KVM / Windows 镜像

当前 KVM agent 约定镜像目录为：

```text
/var/lib/gzctf/images
```

平台创建 VM 时会查找：

```text
/var/lib/gzctf/images/<templateId>.qcow2
```

如果存在该模板文件，会用它作为 backing file 创建运行时 qcow2；如果不存在，
会创建一个空盘。因此 VM 镜像需要提前分发到被调度的 KVM 节点，或者通过平台的
镜像上传/分发接口让节点下载。

## 三、结合当前 PVE 的建议

已观察到 PVE 8.4 上有以下存储：

- `local`：目录存储，空间较小，适合 ISO、临时文件。
- `local-lvm`：本机 LVM thin，适合 PVE VM 本身磁盘。
- `nfs-pve-shared`：NFS 共享存储，约 35T 可用，挂载源为
  `10.24.110.110:/data/nfs-pve`。

推荐把 `nfs-pve-shared` 作为“镜像母仓库”，不要把所有大镜像散落在每台节点上手工维护。

推荐流程：

1. 在 PVE 中制作 Windows/Linux 靶机黄金模板。
2. 关机并清理模板，导出为 qcow2。
3. 把 qcow2 放到 NFS 共享镜像仓库，例如：

   ```text
   /mnt/pve/nfs-pve-shared/gzctf-images/
   ```

4. 比赛前同步到各 GZCTF worker 的本地缓存：

   ```bash
   rsync -aH --info=progress2 \
     /mnt/gzctf-image-repo/*.qcow2 \
     /var/lib/gzctf/images/
   ```

5. 在平台中导入/登记镜像模板，并确认 `<templateId>.qcow2` 在会被调度的节点存在。

短期最稳妥方案：

- Docker：统一走内网 registry。
- VM/qcow2：PVE NFS 做母仓库，worker 本地 `/var/lib/gzctf/images` 做运行缓存。

不建议把 PVE 管理节点本身直接作为 GZCTF worker；更建议在 PVE 里开专门的 worker VM，
例如 `10.0.7.125` 这种节点。这样平台、worker、PVE 管理面之间职责清晰。

## 四、注意事项

- 不要把 PVE root 密码写入脚本、仓库或平台配置文件。
- 大型 Windows qcow2 不建议比赛开始后临时分发，最好赛前预同步。
- 当前代码的 VM 运行时 overlay 也会放在 `ImageStoragePath` 下。若直接把
  `/var/lib/gzctf/images` 挂成 NFS，运行时磁盘也会落到 NFS 上，性能和稳定性不如本地缓存。
- 更理想的后续改造是把“基础镜像目录”和“运行时磁盘目录”拆开：基础镜像可读共享，
  运行时 overlay 放 worker 本地 SSD/NVMe。
