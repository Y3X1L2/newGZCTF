# 模块文档与自动维护实施

状态：进行中。基线：origin/main 9bc3db66，整合此前已提交的文档审计93038788；审计工作区的未提交空白改动保留原处。

## 目标与验收

按用户确认的轻量结构落地：overview、按功能模块README/development、共用architecture、全站operations、current-state、records、archive与小型catalog。TeamLab采用专题参考。入口用简体中文先说明用途，代码和限制均有依据，不复制大量DTO或历史流水。

自动化应检查链接/源码定位、目录映射、入口篇幅，生成模块导航，基于Git差异报告受影响文档；PR要求更新相关说明或给出明确无需更新理由。篇幅只提示。复杂任务一份记录，首次有效提交后建立Draft PR。暂不引入源码指纹、facts证据库、AI外部付费服务或自动合并。

## 分工与冻结约定

- 统筹：codex/docs-modular-maintenance，负责目录迁移、全局入口、架构/运维、catalog、合入与验证。
- 自动化子任务：scripts/validation/check-documentation.py、测试、生成/影响工具、.github/workflows/documentation.yml及PR模板。禁止修改catalog正文或业务源码。
- 业务文档子任务：ctf、awdp、theory、training、exercise、identity、provisioning模块README/development和已有参考。
- 执行面文档子任务：teamlab、content、runtime、audit模块README/development与TeamLab专题拆分。

每个子任务使用独立分支/worktree，按源码核验，在自身工作区提交；统筹正常合入，不由子任务改共享状态/导航。文档默认README约500–1000中文字、development目标100–200行；超过200行提示，按独立链路拆分，关键权限/状态/验收条件不能删。

目标catalog格式：version=1；modules数组，每项包含id、title、overview、development、code（仓库相对glob列表）、docs（仓库相对glob列表）。module目录名即id；其他共享规则/工具可登记platform条目。文件路径glob以/分隔。源文件归属可多模块；未映射的代码变更必须在影响报告可见，不能默默通过。

PR正文采用中文“文档影响”段落，模板和解析规则由自动化子任务提供，并将命令/字段告知统筹。没有PR正文的push/定期手动巡检仅报告，不伪造复核确认。PR内容只能作为数据解析，不能执行。CI保持contents:read，报告作为artifact，不自动发消息或创建外部AI请求。

## 步骤

- [x] 最新主线建立独立集成worktree，正常合入已审计分支。
- [ ] 迁移旧目录、修复引用并冻结子任务基线。
- [ ] 并行编写模块页和自动化；代码与文档链接复核。
- [ ] 汇总catalog与首页、缩短当前状态、维护规范与PR流程。
- [ ] 正向/负向自动化回归、生成一致性、全部现行链接、Git差异、文档可读性复核。
- [ ] 提交推送、Draft PR与审查；报告实际测试和剩余限制。生产不在本任务范围。
