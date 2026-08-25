# Phase 2 实例就绪与 Windows 凭据迁移手册

## 1. 迁移范围

迁移：`20260721151047_CompletePhaseTwoInstanceReadiness`

结构变化：

- `Containers` 新增 `EntryStatus`、`EntryReadyAt`、`EntryError`。
- `VmInstances.RdpPassword` 删除，新增 `RdpPasswordProtected`。
- `ImageTemplates` 新增 `SupportsInstanceCredentials`。

数据变化：

- 历史容器回填为 `Ready`，`EntryReadyAt=StartedAt`，避免升级后隐藏原有可用入口。
- 历史 `Creating/Running/Stopped` VM 标记为 `Error` 并清除 `RdpUrl`。
- 历史 VM 明文密码直接删除，不转换成伪 Data Protection 密文。
- 所有历史 Windows 镜像默认“未认证”。

## 2. 迁移前门禁

维护窗口开始后停止外部写入，执行完整 PostgreSQL 备份并校验 SHA-256。备份必须包含 `DataProtectionKeys`。

查询活动 VM：

```sql
SELECT "Id", "VmName", "Status", "NodeId", "GuacamoleConnectionId"
FROM "VmInstances"
WHERE "Status" IN (0, 1, 2)
ORDER BY "CreatedAt";
```

正式迁移前必须通过旧平台正常销毁这些 VM，并确认 KVM、Guacamole 和数据库记录都已收敛。不能只删除数据库行，否则会遗留虚拟机、端口或 Guacamole 连接。

查询历史镜像：

```sql
SELECT "Id", "Name", "OSType", "ImageType", "Status"
FROM "ImageTemplates"
WHERE "OSType" = 1
ORDER BY "Id";
```

记录待复测的 Windows 镜像清单，不在迁移 SQL 中批量认证。

## 3. 离线迁移验证

先在生产数据库副本执行：

```bash
dotnet ef database update \
  --project src/GZCTF/GZCTF.csproj \
  --startup-project src/GZCTF/GZCTF.csproj \
  --connection '<隔离副本连接串>'
```

迁移后核对：

```sql
SELECT COUNT(*) FROM "Containers" WHERE "EntryStatus" = 1;
SELECT COUNT(*) FROM "Containers" WHERE "EntryStatus" = 0;
SELECT COUNT(*) FROM "VmInstances" WHERE "RdpPasswordProtected" IS NOT NULL;
SELECT COUNT(*) FROM "VmInstances" WHERE "Status" IN (0, 1, 2);
SELECT COUNT(*) FROM "ImageTemplates" WHERE "SupportsInstanceCredentials";
```

预期：

- 迁移前已有容器全部为 `EntryStatus=1`。
- 没有新建业务的离线副本中，`Pending=0`。
- 历史 VM 的 `RdpPasswordProtected` 全部为空。
- 活动历史 VM 数量为 0；若迁移前未清理，会被标记 Error。
- 历史镜像认证数量为 0。

执行仓库自动化验证：

```bash
dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj \
  --filter FullyQualifiedName~InstanceReadinessMigrationTests
dotnet ef migrations has-pending-model-changes \
  --project src/GZCTF/GZCTF.csproj \
  --startup-project src/GZCTF/GZCTF.csproj
```

## 4. 发布顺序

1. 停止平台外部写入。
2. 清理活动 VM，并保存资源清理证据。
3. 完成最终数据库备份和摘要校验。
4. 部署公网网关 revision/ACK 同步器，但暂不开放用户流量。
5. 应用数据库迁移。
6. 启动相同 Git SHA 的平台和 Agent。
7. 验证本地登录、IAM、健康检查和管理端。
8. 创建 Docker 实例，确认 `Pending -> Ready`。
9. 认证一份已验收 Windows 镜像，创建两次 VM，确认凭据不同且 Guacamole 可用。
10. 比赛和培训实例各验证一次，再开放外部写入。

## 5. 回滚边界

本迁移会删除历史明文 RDP 密码，EF `Down` 只能恢复一个空的 `RdpPassword` 列，无法恢复历史凭据。因此生产回滚不能只执行 `dotnet ef database update <旧迁移>`。

正确回滚方式：

1. 恢复维护窗口前的完整数据库备份。
2. 恢复旧应用、旧 Agent 和旧公网网关配置。
3. 验证旧版本登录与实例管理。
4. 重新开放写入。

新版本已产生业务写入后，不得直接覆盖数据库回滚；应重新进入维护模式，评估新增数据补偿后再决定版本回退。
