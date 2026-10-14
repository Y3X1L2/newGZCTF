# .31 独立数据盘迁移

## 目标、授权与范围

用户授权处理平台镜像分发与存储，可扩展节点；并说明当前平台实例都是测试环境，可以关闭。本任务只迁移 PVE VM115 / Worker `10.24.0.31` 的镜像缓存和 TeamLab 运行目录，保持该节点停止调度。源码部署仍由主任务在候选通过门禁后确认，本任务不发布主站或 Agent。

保留 16 GiB 内存、原 128 GiB 系统盘、Docker data-root、其他 Worker、Registry 制品、PVE VM118–121、快照和旧备份。本轮没有获准批量删除旧模板，没有执行 Registry GC。

起点为任务分支 `codex/image-cache-reclamation`，回收源码候选 `bd9b6815`、补充工程说明 `fe3fb255`。迁移没有部署源码；服务器仍运行旧 Agent `cc6c5737`，实际 SHA256 `9b2142e9731d77495322e20a100432a728cf3d86f87dc67abebf85ed58a9b3e8`。

## 迁移前现场事实

- 主站数据库：`.31 IsSchedulable=false`，该节点活动部署票据 0；主站首页 HTTP 200。
- `.31`：无 libvirt 域、运行 Docker 容器或镜像 `.part`。23 个现有文件；images 占 `102,500,483,072` 已分配字节，teamlab `49,152` 字节，Docker `3,651,039,232` 字节。根文件系统总 `132,564,258,816` 字节、已用 `128,826,253,312`、普通可用 0。
- PVE：VM115 running，4 核、16 GiB RAM，scsi0 `local-lvm:vm-115-disk-0` 128 GiB、VirtIO SCSI single。scsi1 空；`sdb-storage` 可用约 12.4 TB，local-lvm thin pool 75.63%。

## 新增磁盘与固定路径

| 项目 | 已核对的配置 |
| --- | --- |
| PVE 数据盘 | scsi1，`sdb-storage:115/vm-115-disk-0.qcow2`，500 GiB qcow2、discard、iothread |
| 唯一序列号 | `YINYU115DATA20261008` |
| 文件系统 | 仅在确认同一序列号、容量、无分区/签名/挂载后建立 GPT + ext4 |
| 文件系统 UUID | `88b34ec0-03d2-4231-bcf5-41b0733ce118` |
| 数据卷 | `/srv/yinyu-data` |
| 保持的镜像路径 | `/var/lib/gzctf/images` → bind `/srv/yinyu-data/images` |
| 保持的运行目录 | `/var/lib/gzctf/teamlab` → bind `/srv/yinyu-data/teamlab` |

路径保持原样，已有 qcow2 backing path 和 Agent 配置不需要重写。两目录属于同一文件系统，容量不能相加。

`fstab` 使用 UUID 挂载数据卷；bind 挂载显式依赖数据卷。`gzctf-agent.service` 增加 `RequiresMountsFor`，要求数据卷及两原路径先挂载，数据盘异常时不能回退向根盘写缓存。

## 执行链路与记录

1. 保存 PVE VM115 配置到 `/root/yinyu-storage/node115-20261008/115.conf.before`，root 600；原配置 SHA256 `0ef6a8bc8c6a57675af3d67c92335f4f22ae4e31ad0f74d7f5b21af23a1770aa`。
2. 热插数据盘并核对 guest 中唯一 serial；没有重启 PVE，也没有修改 RAM。
3. 新卷保存原 fstab 与 Agent 服务及已有 drop-in，位于 `/srv/yinyu-data/storage-migration/node31-20261008`、root 700，单个配置备份 600。只记录摘要，不在 Git 保存原配置。
4. 停止旧 Agent，复核 VM、Docker、下载和打开镜像文件均为空。独立 systemd 临时任务执行 `rsync -aHAXS --numeric-ids`，保留稀疏、权限、时间、ACL、xattr 与硬链接；原文件仍留根盘。
5. 逐文件比较全部原件和副本的完整 SHA256、大小与权限/属主/mtime；两物理盘的独立读取并行，最多两个 reader。校验 manifest 受保护保存于新卷。只有全部验证后才能切换 bind 并按 manifest 删除本任务的根盘重复 entries；不递归盲删，不删除唯一镜像。
6. 切换后执行 `mount -a`、挂载依赖和 Agent API / inventory 检查；回收根盘空闲块。确认无计算或新任务后，仅对 VM115 做一次受控重启并核对冷启动。

## 验证状态

当前：本次存储迁移 `complete / VERIFIED`。源码部署和两套环境验收由主任务继续完成；`.31` 保持停止调度，不能称为新调度候选已经验收。

| 验证项 | 结果 |
| --- | --- |
| 新磁盘身份、空白确认、ext4 挂载 | VERIFIED；新卷约 527.29 GB 总量、521.90 GB 可用（复制前） |
| 稀疏复制 | 已完成；目标 images 已分配 `102,013,693,952` 字节，较源少约 0.5 GB；内容一致须以 SHA 为准 |
| 完整文件 SHA / 权限 | VERIFIED：23/23 完整源/目标 SHA、size、mode、uid、gid、mtime 一致，总逻辑字节 `102,500,392,457`；双盘并行校验 493.6 秒 |
| bind、fstab 与 service 依赖 | VERIFIED：两原目录同 UUID；`mount -a`、fstab 0 parse errors / 0 errors；两 bind unit 的 `Requires` / `After` 依赖数据卷，Agent `RequiresMountsFor` 生效 |
| 根盘重复空间回收 | VERIFIED：只移除 manifest 覆盖的 23 个重复 entries，所有原逻辑镜像留在新卷；`fstrim /` 回收 `106,283,032,576` 字节 |
| 旧 Agent / 主站节点事实 | VERIFIED：Agent status / runtime inventory HTTP 200，VM / Docker / TeamLab 资源 0，旧 binary 不变；主站数据库 `.31 false`、活动票据 0、首页 200（最终时点见原始证据） |
| VM115 冷启动 | VERIFIED：先检查自动挂载，未运行 `mount -a` 辅助；boot ID 变更，Agent 自恢复、PID 5945 / NRestarts 0，23 文件清单/size/权限/mtime 和校验 manifest 保持 |
| 两套环境实际启动 | 由主任务在新候选发布后验收 |

原始证据、脚本与完整清单只在本机 `D:/Work/YINYU-Image-Lifecycle-20261008/node31-storage/` 和服务器受保护目录，不提交 Git。

冷启动后根盘可用 `99,487,670,272` 字节（约 92.7 GiB），新卷可用 `419,892,060,160` 字节（约 391.1 GiB）。PVE local-lvm thin pool 从 75.63% 降至 53.08%，VM115 原根盘 allocated 从 96.33% 降至 19.37%。VM115 在重启返回后的即时检查为 stopped，随后通过只在 stopped 时启动的安全核对恢复 running；其他 VM112 / 113 / 114 原 PID 保持，未重启 PVE 宿主或修改 RAM。

冷启动 boot ID 从 `d0ff11e8-dc7b-4317-a90e-76fff7e0b0efc` 变为 `7290a10a-9d29-458c-bc06-fe191ea3fa8b`。完整文件 SHA 在切换前验证，冷启动复核清单、大小、权限、mtime 与 manifest，不重复整盘 SHA。manifest SHA256 `536fa8789725da26ff6283dbb33feda04f48524bee70cb1297be546139c65466`；新 fstab SHA256 `6847ce82774389e8418b972e037861fb66115406129164251153b84292bbdfaa`；新增 mount drop-in SHA256 `bdbaa69664df3a31ea77e72614203cd13f1b3794e9563af5597d8413805b20d7`。

`findmnt --verify` 对原有 `/swap.img` regular file 给出 1 条警告，但无解析或挂载错误；复制开始时目标 teamlab 目录尚未创建产生过 `du` 提示，随后复制、23 文件校验与冷启动验证均完成。不能将这些记录写为“所有日志零错误”。主站管理员 Node API 回读由主任务复用已有认证完成，本记录明确区分它与已做的 Agent API 和数据库查询。

## 回退与后续

- 复制/校验失败时原路径仍在根盘，复制任务会重新启动旧 Agent，保留错误和所有原件。
- bind 切换或后续验证失败时先保持 Agent 停止，按受保护配置备份检查挂载；不盲删不明数据。
- 根盘重复文件移除后，不能只恢复旧 fstab 就回退。需要先停止 Agent、确认根盘容量，再反向复制新卷文件并重新校验后解除 bind 和恢复配置；这会回到原来的空间紧张状态，优先修复新卷挂载。
- `.31` 继续禁止调度。旧 Agent 尚未包含分盘容量保护，磁盘迁移本身不等于上报和调度逻辑已修复；候选 Agent 升级与两套环境验收另行记录。
- 已实测旧 manifest 的 `host.availableVmImageStorageBytes` 仍为约 99.49 GB 根盘余量，而实际 VM 目录属于约 419.89 GB 空余的新卷。后续升级必须核验主站与 Agent 对同一物理文件系统的事实，不把 images 和 teamlab 两个 bind 路径的余量相加。
