# Windows VM 实例凭据改造、部署与验收指南

## 1. 文档目标

本文说明 Windows VM 为什么从共享默认密码改为每实例独立凭据，以及新版本应如何部署、验证和回滚。

适用人员：

- 平台开发和发布人员；
- KVM Agent 与 Guacamole 运维人员；
- Windows 镜像制作人员；
- 上线验收和安全测试人员。

本文不替代镜像制作说明。镜像配置细节参见 `docs/operations/windows-cloudbase-init-image.md`，数据库结构变化参见 `docs/operations/phase-02-instance-readiness-migration.md`。

## 2. 为什么进行改造

旧方案依赖固定或共享的 Windows 管理凭据，存在以下问题：

1. 多个选手实例可能使用相同密码，一个实例泄露后会影响其他实例。
2. 固定密码容易进入配置文件、日志、截图或部署脚本，难以完成可靠轮换。
3. 平台无法证明某个凭据只属于某个 `VmInstance`，审计和故障定位缺少明确边界。
4. 镜像如果不支持启动时注入凭据，平台只能依赖预置账户，无法安全地进行自动调度。
5. Guacamole 使用共享账户时，平台、远程桌面网关和虚拟机之间缺少实例级隔离。

新方案遵循以下原则：

- 每个 VM 实例生成不同的高强度随机密码；
- 数据库只保存 ASP.NET Data Protection 密文；
- 明文只在生成 Cloudbase-Init seed 和创建 Guacamole 连接时短暂存在内存；
- 镜像、节点或 Guacamole 不满足能力要求时拒绝创建，不回退到共享密码；
- 销毁实例时同时清理 VM、Cloudbase-Init seed 和 Guacamole 连接。

## 3. 新方案的工作流程

```text
用户创建 Windows 实例
  -> 平台校验镜像 Ready、Windows VM、已认证凭据能力
  -> 平台选择具备 KVM 与 Cloud-Init 能力的远程 Agent
  -> 为 VmInstance 生成随机密码
  -> 使用 Data Protection 加密后保存 RdpPasswordProtected
  -> 生成 NoCloud ConfigDrive seed
  -> Agent 创建 VM 并挂载 seed
  -> Cloudbase-Init 在 Windows 内创建或更新 player 用户
  -> Cloudbase-Init 设置实例密码、启用 RDP 和防火墙
  -> 平台使用同一实例凭据创建 Guacamole 连接
  -> VM 就绪后向用户提供入口
```

关键数据边界：

| 数据 | 存储位置 | 是否允许明文持久化 |
| --- | --- | --- |
| 实例 RDP 密码 | `VmInstances.RdpPasswordProtected` | 否 |
| Data Protection 密钥 | PostgreSQL `DataProtectionKeys` | 由平台保护并随数据库备份 |
| Cloudbase-Init user-data | Agent 临时 seed 目录 | 仅实例创建期间允许，销毁时删除 |
| Guacamole API 密码或 Token | 平台密钥配置 | 不进入 Git、日志或普通配置模板 |
| 镜像凭据能力 | `ImageTemplates.SupportsInstanceCredentials` | 可持久化，不属于秘密 |

## 4. 代码和数据库变化

主要实现位置：

- `src/GZCTF/Services/Vm/VmCredentialService.cs`：生成和保护实例密码；
- `src/GZCTF/Services/Fleet/FleetVmService.cs`：镜像、节点能力门禁和 VM 生命周期；
- `src/GZCTF.Agent/Services/KvmService.cs`：Cloudbase-Init seed 权限与 KVM 创建；
- `src/GZCTF/Services/GuacamoleService.cs`：使用实例凭据创建远程桌面连接；
- `src/GZCTF/Controllers/ImageTemplateController.cs`：Windows 镜像凭据能力认证；
- `src/GZCTF/Migrations/20260721151047_CompletePhaseTwoInstanceReadiness.cs`：数据库迁移。

迁移后的行为：

- 删除旧明文列 `VmInstances.RdpPassword`；
- 新增密文字段 `VmInstances.RdpPasswordProtected`；
- 新增 `ImageTemplates.SupportsInstanceCredentials`；
- 历史活动 VM 不会伪造新密文，而是被标记为错误并要求重新创建；
- 历史 Windows 镜像默认未认证，必须完成实机测试后人工认证。

## 5. 部署前准备

### 5.1 固定发布物

平台、Agent、数据库迁移必须来自同一个 Git SHA。镜像应使用 SHA 标签，不应只使用 `latest`。

记录：

```text
Git SHA:
平台镜像摘要:
Agent 镜像或二进制摘要:
迁移 bundle 摘要:
测试环境:
执行人员:
执行时间:
```

### 5.2 数据库与活动实例

1. 进入维护状态，停止新的实例创建和其他外部写入。
2. 通过旧平台正常销毁所有活动 Windows VM。
3. 核对 KVM VM、Guacamole 连接、端口租约和数据库状态都已收敛。
4. 执行 PostgreSQL 完整备份并计算 SHA-256。
5. 确认备份包含 `DataProtectionKeys` 表。

不得仅删除 `VmInstances` 数据行代替实例销毁，否则可能遗留真实 VM 和 Guacamole 连接。

### 5.3 节点能力

远程 Agent 必须报告：

```text
runtime.kvm.v1
runtime.vm.cloud-init.v1
image.vm.download.v1
```

本机 KVM 和不支持 Cloud-Init 的旧 Agent 必须被平台拒绝，不允许回退到旧密码方案。

### 5.4 Windows 镜像

候选镜像至少满足：

- QCOW2 格式，Windows 类型，平台状态为 `Ready`；
- VirtIO 存储和网卡驱动可用；
- Cloudbase-Init 以 `LocalSystem` 运行；
- 启用 NoCloud ConfigDrive 和 PowerShell user-data；
- 能处理 `#ps1_sysnative`；
- RDP 服务、用户管理和防火墙修改可用；
- 已清理历史 metadata、临时凭据和制作日志，并完成 Sysprep。

镜像在完成第 8 节双实例测试前不得设置 `SupportsInstanceCredentials=true`。

### 5.5 Guacamole

配置预置短期 Token 或独立受管 API 账户。不得使用默认的 `guacadmin/guacadmin`，不得将凭据写入仓库。

## 6. 自动化验证

自动化测试用于验证平台代码契约，不能证明 Windows 客户机真的执行了 Cloudbase-Init。

建议在固定 SHA 上执行：

```bash
dotnet test src/GZCTF.Test/GZCTF.Test.csproj --configuration Release
dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj --configuration Release
dotnet build src/GZCTF.slnx --configuration Release --no-restore
dotnet ef migrations has-pending-model-changes \
  --project src/GZCTF/GZCTF.csproj \
  --startup-project src/GZCTF/GZCTF.csproj
```

自动化门禁至少覆盖：

- 密码随机生成和 Data Protection 往返；
- 数据库不再保存明文密码；
- 未认证 Windows 镜像拒绝调度；
- 非 Windows 或非 VM 镜像不能认证；
- 无 KVM 或无 Cloud-Init 能力节点拒绝调度；
- Guacamole 未配置时拒绝创建连接；
- 历史迁移结果与 EF 模型一致；
- 日志、API DTO 和遥测不返回实例密码。

当前 RC 的自动化结果必须和发布记录一起保存，不能只记录“测试通过”。

## 7. 隔离环境部署步骤

先在生产数据库副本和隔离 KVM 节点执行，不能直接以现网作为首次验证环境。

1. 恢复生产数据库副本，记录恢复时间和摘要。
2. 部署固定 SHA 的平台和 Agent。
3. 配置 Data Protection 数据库持久化和 Guacamole 受管账户。
4. 应用 `20260721151047_CompletePhaseTwoInstanceReadiness` 迁移。
5. 查询迁移后的 VM、镜像认证和 Data Protection 数据。
6. 启动平台，确认数据库、Redis、Storage 和 Agent 健康。
7. 上传候选 Windows QCOW2，等待状态变为 `Ready`。
8. 先在隔离节点完成镜像离线注入测试。
9. 使用有权限的管理员或镜像所有者执行“认证 Cloudbase-Init”。
10. 再从比赛和培训业务页面分别创建 Windows 实例。

迁移核对示例：

```sql
SELECT COUNT(*) FROM "VmInstances" WHERE "RdpPasswordProtected" IS NOT NULL;
SELECT COUNT(*) FROM "VmInstances" WHERE "Status" IN (0, 1, 2);
SELECT "Id", "Name", "Status", "SupportsInstanceCredentials"
FROM "ImageTemplates"
WHERE "OSType" = 1
ORDER BY "Id";
SELECT COUNT(*) FROM "DataProtectionKeys";
```

## 8. Windows 双实例实机验收

### 8.1 实例 A

1. 使用已准备但尚未认证的镜像，在隔离 KVM 节点注入随机密码 A。
2. 启动 VM，确认系统盘和 VirtIO 网卡正常。
3. 检查 Cloudbase-Init 服务成功处理 ConfigDrive 和 PowerShell user-data。
4. 使用 `player` 和密码 A 通过原生 RDP 登录。
5. 通过平台 Guacamole 入口登录。
6. 重启 VM，确认密码 A 仍可使用。
7. 销毁实例，确认 VM、Guacamole 连接和 seed 目录全部删除。

### 8.2 实例 B

1. 使用同一模板创建实例 B，并记录平台生成的密码 B。
2. 确认密码 B 与密码 A 不同。
3. 确认密码 A 无法登录实例 B。
4. 确认密码 B 可以通过 RDP 和 Guacamole 登录实例 B。
5. 销毁实例 B 并确认资源清理。

### 8.3 平台业务入口

镜像通过上述离线测试后，在管理端标记“认证 Cloudbase-Init”，然后验证：

1. 比赛 Windows 容器题可以创建、访问、延期和销毁。
2. 培训章节 Windows 实例可以创建、访问、延期和销毁。
3. 两个用户并发创建时密码不同，入口不会串到其他实例。
4. 页面倒计时、实例状态和销毁结果与后台状态一致。

## 9. 必须执行的负向测试

| 场景 | 预期结果 |
| --- | --- |
| Windows 镜像未认证 | 编辑器不可选择或运行时明确拒绝 |
| 镜像状态不是 `Ready` | 不允许认证和创建实例 |
| Docker 或 Linux 镜像申请凭据认证 | API 返回 `400` |
| 教师认证其他人的镜像 | API 返回 `403` |
| Agent 缺少 `runtime.kvm.v1` | 不调度到该节点 |
| Agent 缺少 `runtime.vm.cloud-init.v1` | 创建失败且不回退共享密码 |
| Guacamole 未配置受管认证 | 创建失败且不使用默认管理员 |
| Cloudbase-Init 未执行 | VM 不得被误判为完整可用，应保留可诊断错误 |
| Data Protection 密钥不可用 | 不伪造密码，活动实例进入可诊断失败状态 |
| 销毁过程中 Agent 失败 | 平台记录失败，资源清理可重试且不谎报成功 |

## 10. 日志与证据要求

保存以下证据，但必须遮盖密码、Token、Cookie 和完整 user-data：

- 固定 Git SHA 和发布物 SHA-256；
- 数据库迁移前后核对结果；
- Agent capability manifest；
- 镜像 ID、镜像哈希和 Cloudbase-Init 版本；
- 实例 A、B 的不同凭据证明，只记录“不同”，不记录密码值；
- 原生 RDP 和 Guacamole 登录成功截图；
- 密码 A 登录实例 B 失败的截图；
- VM、seed 和 Guacamole 连接销毁证据；
- 平台、Agent、Cloudbase-Init 和 Guacamole 的脱敏日志；
- 所有负向测试的 HTTP 状态和用户可见错误。

任何日志中发现明文 RDP 密码、Guacamole 密码、同步 Token 或完整 Cloudbase-Init user-data，均判定验收失败。

## 11. 故障处理

| 现象 | 优先检查 |
| --- | --- |
| `does not advertise Cloud-Init` | Agent 版本、capability manifest、virt-install 和 seed 探测 |
| `image is not verified` | 镜像是否完成双实例测试并经过有权限用户认证 |
| `has no protected RDP credential` | 是否为迁移前旧实例、Data Protection 是否正常 |
| VM 启动但不能登录 | Cloudbase-Init 日志、PowerShell user-data、VirtIO 网卡、RDP 防火墙 |
| Guacamole 连接失败 | API Token/账户、连接参数、VM 网络和 RDP 端口 |
| 重启后无法解密凭据 | `DataProtectionKeys` 是否随数据库恢复、平台实例是否连接同一数据库 |
| 销毁后仍有 VM | Agent 销毁日志、KVM 域、seed 目录和 Guacamole 清理任务 |

## 12. 回滚

此迁移会删除历史明文 RDP 密码，EF `Down` 无法恢复原密码。正确回滚必须使用维护窗口前的完整数据库备份。

1. 保持维护模式，停止新版本平台和 Agent。
2. 恢复旧应用、旧 Agent 和旧 Guacamole 配置。
3. 恢复迁移前完整数据库备份，包括 `DataProtectionKeys`。
4. 验证旧版本登录、节点和实例管理。
5. 核对没有新版本遗留的 VM、seed、端口租约和 Guacamole 连接。
6. 验证通过后再恢复外部写入。

新版本开放写入后如需回滚，必须先评估新增业务数据，不能直接覆盖数据库。

## 13. 完成标准

只有同时满足以下条件，Windows VM 改造才可视为通过：

- 自动化测试、Release 构建、迁移一致性检查全部通过；
- 生产数据库副本完成迁移和完整恢复演练；
- 候选 Windows 镜像完成双实例凭据隔离测试；
- 比赛和培训各完成一次真实 Windows 实例流程；
- RDP、Guacamole、重启、延期和销毁均通过；
- 负向测试确认系统不会回退到共享密码；
- 平台、Agent、Guacamole 和 Cloudbase-Init 日志无敏感信息；
- 回滚流程在隔离环境执行成功并保存证据。

自动化测试通过但没有真实 KVM 双实例证据时，只能标记为“代码门禁通过”，不能标记为“Windows VM 生产验收通过”。
