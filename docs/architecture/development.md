# 共用基础的开发定位

这里处理跨模块基础、前端壳层与仓库工具。业务规则先到 [对应模块](../modules/README.md) 查，不把所有问题归入“平台”。

| 修改目标 | 代码与规则 |
| --- | --- |
| 模块注册与共享设施 | [Composition](../../src/GZCTF/Composition)、[Infrastructure](../../src/GZCTF/Infrastructure)、[模块边界](module-boundary-map.md) |
| 全局配置与管理员设置 | [配置模型](../../src/GZCTF/Models/Internal/Configs.cs)、[AdminController](../../src/GZCTF/Controllers/AdminController.cs)；业务策略也阅读所属模块 |
| 数据模型与迁移 | [Migrations](../../src/GZCTF/Migrations)、[数据库规则](database-index-and-lifecycle-audit.md)；改模型须新增前向迁移，不覆盖历史 |
| 前端壳层与导航 | [vnext/app](../../src/GZCTF/ClientApp/src/vnext/app)、[前端开发](frontend/development.md) |
| 展示组件与主题 | [shared](../../src/GZCTF/ClientApp/src/vnext/shared)、[design](../../src/GZCTF/ClientApp/src/vnext/design)、[组件](frontend/component-boundary.md)、[Token](frontend/style-tokens.md) |
| API生成与兼容检查 | [现有兼容工具](../../scripts/verify-openapi-contract.ps1)、[Quality工作流](../../.github/workflows/quality.yml)、[API标准](external-api-standard.md) |
| 文档工具与维护方式 | [文档维护](documentation.md)、[文档检查器](../../scripts/validation/check-documentation.py) |

业务主站、Agent与前端的构建入口见 [根README](../../README.md)。共享契约、认证、调度、计分和迁移改变时扩大验证；单纯文档移动检查链接/代码引用/生成一致性，不能声称完整软件门禁或生产验收通过。
