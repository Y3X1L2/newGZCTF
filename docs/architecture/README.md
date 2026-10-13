# 共用架构与规则

这里保存多个功能共同遵守的规则。了解项目先读 [项目概览](../overview.md)，修改具体功能先读 [模块开发地图](../modules/README.md)，需要确认跨模块边界时再查本目录。

| 主题 | 入口 |
| --- | --- |
| 主站、Agent、业务与运行层关系 | [平台架构](platform.md) |
| 哪个模块拥有事实、可以调用谁 | [模块边界](module-boundary-map.md) |
| 领域术语与当前实体 | [术语表](domain-glossary.md) |
| 外部调用、授权、错误与幂等 | [外部 API 标准](external-api-standard.md) |
| Agent 能力和兼容协商 | [Agent 协议](agent-capability-protocol.md) |
| 缓存何时失效 | [缓存关系](cache-invalidation-map.md) |
| 索引与数据生命周期 | [数据库规则](database-index-and-lifecycle-audit.md) |
| 日志和异步事件命名 | [事件分类](event-taxonomy.md) |
| vNext 页面、组件与主题 | [前端开发](frontend/development.md)、[组件边界](frontend/component-boundary.md)、[Token](frontend/style-tokens.md) |
| 前端体验与路由对照 | [设计语言](frontend/design-language.md)、[页面/API地图](frontend/page-api-map.md)、[待补契约](frontend/backlog.md) |
| 文档写作、拆分与自动检查 | [文档维护](documentation.md) |

稳定规则在这里说明一次，模块只链接对应规则。具体部署与恢复步骤见 [运维入口](../operations/README.md)，某次环境结果见 [任务记录](../records/README.md)。
