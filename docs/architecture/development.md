# 共用基础的开发定位

这里处理跨模块基础、前端壳层与仓库工具。业务规则的实现和测试入口归入 [对应模块](../modules/README.md)。

| 修改目标 | 代码与规则 |
| --- | --- |
| 模块注册与共享设施 | [Composition](../../src/GZCTF/Composition)、[Infrastructure](../../src/GZCTF/Infrastructure)、[模块边界](module-boundary-map.md) |
| 全局配置与管理员设置 | [配置模型](../../src/GZCTF/Models/Internal/Configs.cs)、[AdminController](../../src/GZCTF/Controllers/AdminController.cs)；业务策略也阅读所属模块 |
| 数据模型与迁移 | [Migrations](../../src/GZCTF/Migrations)、[数据库规则](database-index-and-lifecycle-audit.md)；模型变更使用新的前向迁移，历史文件保留 |
| 前端壳层与导航 | [vnext/app](../../src/GZCTF/ClientApp/src/vnext/app)、[前端开发](frontend/development.md) |
| 展示组件与主题 | [shared](../../src/GZCTF/ClientApp/src/vnext/shared)、[design](../../src/GZCTF/ClientApp/src/vnext/design)、[组件](frontend/component-boundary.md)、[Token](frontend/style-tokens.md) |
| API生成与兼容检查 | [现有兼容工具](../../scripts/verify-openapi-contract.ps1)、[Quality工作流](../../.github/workflows/quality.yml)、[API标准](external-api-standard.md) |
| 文档工具与维护方式 | [文档维护](documentation.md)、[文档检查器](../../scripts/validation/check-documentation.py) |

业务主站、Agent与前端的构建入口见 [根README](../../README.md)。共享契约、认证、调度、计分和迁移改变时扩大验证；文档移动验证链接、代码引用和生成一致性；软件门禁与生产验收分别记录实际执行范围。
