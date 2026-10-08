# GZCTF 节点部署简要指南

节点是在 `/admin/nodes` 登记的独立 Linux Worker。主站保存镜像目录、引用和运行计划；
Agent 在 Worker 上执行本机 Docker/KVM 操作。建议在 PVE 中建立专用 Worker VM，
不要把 PVE 管理宿主直接用作靶机执行节点。

## 节点准备

- .NET / ASP.NET Core Runtime 10，用于 `gzctf-agent`。
- Docker，承载容器；KVM/libvirt，承载 VM。两种能力独立，纯 Docker 节点不必安装 KVM。
- 节点可访问平台、镜像 Registry；主站可访问 Agent（默认 `5001/tcp`）。
- TeamLab 节点还须通过平台完成 OVN/OVS、Fabric 和隧道健康配置。仅在线不代表组网可用。
- 核对 Docker 数据目录、`Kvm:ImageStoragePath`、`TeamLab:RuntimeStateRoot` 的真实挂载和剩余空间。
  根盘、VM 数据盘和 PVE 的 thin pool 是不同容量约束，都需要监测。

先检查基础依赖，命令只进行检查：

```bash
sudo bash scripts/prepare-agent-node.sh --check-only
```

需要安装依赖时使用本目录的初始化脚本；它准备系统，不直接安装平台注册的 Agent：

```bash
sudo bash docs/node-deployment/setup-gzctf-worker-node.sh   --insecure-registry <REGISTRY_HOST>:5000
```

完成后通过后台“节点部署”安装/同步 Agent。不要把密码、token 或私钥写入脚本和 Git。

## 镜像来源与按需分发

普通镜像导入、课程绑定、题目导入/编辑、TeamLab 发布、创建试运行和批量 rollout 不再隐含向所有节点复制镜像。
正常运行沿已有部署队列选择节点，在所选节点确认或准备镜像，下载/校验成功后才启动。
缺缓存不创建空白 VM 来代替原模板；下载失败应在对应票据和分发记录中报告。

```text
规范导入 -> Registry 制品 + 主站模板/摘要 -> 选择运行节点
                                             |
                                             v
                            仅选中 Worker 检查缓存 -> 缺失则下载/校验
                                             |
                                             v
                                     创建实例自己的可写盘
```

- Docker 规范导入把镜像推到配置的 Registry，Worker 使用登记的镜像地址拉取。
  导入仍可经过主站 Docker load/tag/push，不代表主站变成所有运行实例的执行节点。
- 规范 qcow2 导入把制品推入 Registry；成功后删除导入暂存文件，模板保留不可变摘要和来源。
- 旧的后台 VM Upload、Local 和 Archive 路径仍可能保留 `LocalFilePath` 作为当前唯一源文件，
  后续分发可将其制作为 Registry 制品。不得在该迁移成功并核验前删除源文件。
  本期没有自动迁移所有旧格式，也没有宣称主站已经没有本地源文件。
- Worker 的 VM 缓存为 `<Kvm:ImageStoragePath>/<templateId>.qcow2`；下载中可有 `.part`。
  实例 qcow2 是依赖该底盘的差异盘，不能只按“VM 已停止”判断底盘可删。

不需要在每个节点手工 `rsync` 全部镜像。原始 PVE 导出文件、快照与教学材料独立保存，
不属于平台节点缓存或自动回收范围。

## 显式预热与准备诊断

管理员主动调用镜像分发、TeamLab templates/release Prepare 时仍会预热合格节点。
这属于额外存储操作，应先确认目标范围与容量；普通启动不依赖先调用这些接口。
本期没有实现管理员自选目标/租期 API，也没有实现基于缓存年龄的 TTL/LRU。

“可以启动”表示主制品及当前放置条件满足，不表示所有 Worker 已缓存、更不表示来宾业务实测通过。
“已缓存 1/2”表示两个能力合格节点中一个已有缓存；所选节点尚未缓存时，启动流程会按需下载。
未被选中节点的旧分发失败不能单独阻断本次运行；所选节点下载失败仍会使当前运行任务失败。

外部 preparation `ReadyToStart` 同样表示允许按需启动；`State=onDemand` 表示仍需下载，
每模板 cache counts 保留真实记录。它不是调度资源预留，也不是所选节点的就绪保证。

## 回收边界

销毁实例、撤销预热和全局删除模板是不同操作。底盘清理必须通过平台引用检查及
Agent 实际 backing/inventory 检查；不能绕过保护手工批量删除正在使用的缓存。
模板全局删除还会检查课程、题目、TeamLab 草稿和不可变版本等业务引用。
Registry manifest 删除后，未被引用的 blob 仍需受控 Registry GC 才释放物理空间。

本期按需分发使用原 `DeploymentQueueTicket` 和 `ImageDistributionRecord`；没有新增第二套队列。
同一 LAN/组内资产跨节点放置尚未实现；现状仍以不可拆网络组调度，
OVN/OVS 已具备跨节点网络基础不代表当前调度器已能拆分这类组。
