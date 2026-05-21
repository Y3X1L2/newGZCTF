# 决策: 测试基础设施选型

日期: 2026-05-19
参与人: Lead, Follow
状态: ✅ 已解决

## 背景
原 TDD 补充中的 GZCTFTestFixture 与现有 IntegrationTestCollection 共享同一个 PostgreSQL container，ResetDatabaseAsync 会破坏其他测试。Moq/Respawn 未入依赖。速度限制在工厂被禁用导致速率限制测试永远不触发 429。

## 决策结果
1. 使用独立 TestContainers (PostgreSQL + Redis) 做 DB 隔离 — IsolatedTestFixture 每个测试类自己的 DB
2. 工厂配置用 `TestAuthHandler` 走真实认证管线，不用 Header hack
3. 添加 Moq 4.x + Respawn + Testcontainers 为测试依赖
4. 工厂 with `.WithRateLimit(enabled: true)` 允许速率限制测试

## 影响范围
- 涉及文件: `IsolatedTestFixture.cs`(新增), 所有集成测试基类, 测试 csproj
- 需要操作: 添加 3 个 NuGet 包依赖