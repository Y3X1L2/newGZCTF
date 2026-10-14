# 自主练习题库分页

## 页面与接口

`/practice`、`/practice/browse` 和 `/admin/exercises` 使用服务端分页，每页默认20道，可选20/50/100。页码与每页大小保存在 URL，搜索、筛选或切换每页大小时回到第一页；翻页和浏览器返回不会依赖本地全量题目缓存。

- `GET /api/exercise/page`：登录用户可见、已启用的公共题目，继续校验 `MinimumVisibleRole`。
- `GET /api/exercise/manage/page`：Teacher+ 管理公共题目，包含已停用题目。
- 两个接口都排除 `TrainingCourseId != null` 的课程题目。新接口不涉及创建实例、Flag或提交操作。
- `Page` 默认1，允许1–1000000；`PageSize` 默认20，允许1–100。非法值返回400，超出最后一页的合法页码回到最后一页，空题库返回第一页空列表。
- 返回 `items / total / page / pageSize / enabledCount / containerCount / tags`。题目按ID升序，在数据库中先筛选再 `Skip/Take`；个人提交与解题状态只对当页题目投影。

旧 `GET /api/exercise`、`GET /api/exercise/manage` 数组接口保持兼容；本次涉及的三个题库页面不再使用它们。现有 `/practice/stats` 仍使用旧数组接口计算个人统计，聚合统计接口是独立后续优化，不能将当页数据当作全库统计。

## 筛选与统计

支持 `Search / Categories / Difficulties / Tags / Credit / Sources`；练习浏览另支持 `Solved=true/false`，只读取当前用户完成事实。搜索支持题目名称、内容、标签、完整分类名或编号，关键词中的 `%`、`_` 按普通字符处理。标签数组沿用“任一匹配”语义。

`total`、启用数量与容器题数量由相同过滤查询计算，后台摘要是当前筛选的全部匹配题目数量，不是当页数量。`tags` 为调用方可见题库的去重标签元数据，跨页稳定；学员不会收到课程、停用或超权限题目的标签。只传标签选项，不传全量题目。现有标签列通过 value converter 保存为JSON文本，不能作为EF原生数组执行 `Any/SelectMany`；分页查询在PostgreSQL展开标签元数据，再组合相同可见性与匹配条件，无需全量拉题或修改存储模型。

## 边界与验证

Controller只负责认证、查询绑定和应用用例调用。查询与投影在 Exercise Application 中复用，没有修改公开 token API 的 cursor 分页契约、数据库模型、来源导入、附件管理、动态实例或统一调度队列。

`ExerciseCatalogPaginationTests` 在PostgreSQL通过正式API验证默认上限、分页顺序、总数、停用/教师/课程隔离、页码校正、完成与标签筛选、非法参数和管理权限拒绝。页面回归验证分页请求、全库计数、筛选/页大小复位和无效URL规范化。发布需要同时更新Main与前端，按[维护窗口流程](../../../operations/vnext-maintenance-window-rollout.md)执行。10月11日的API与页面分页验收见[发布记录](../../../records/2026-10-11-training-quota-settings.md)，当前部署基线见[状态页](../../../current-state.md)。
