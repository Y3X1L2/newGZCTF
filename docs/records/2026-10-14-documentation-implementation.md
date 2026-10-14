# 模块文档与自动维护实施

状态：实现与本地验证完成，PR审查中。基线：origin/main 9bc3db66，整合此前已提交的文档审计93038788；审计工作区的未提交空白改动保留原处。

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
- [x] 迁移旧目录、修复引用并冻结子任务基线。
- [x] 并行编写模块页和自动化；代码与文档链接复核。
- [x] 汇总catalog与首页、缩短当前状态、维护规范与PR流程。
- [x] 正向/负向自动化回归、生成一致性、全部现行链接、Git差异、文档可读性复核。
- [ ] 提交推送、Draft PR与审查；报告实际测试和剩余限制。生产不在本任务范围。


## 分工结果与审查

三个子agent均使用GPT-6.1 Sol和独立worktree：业务文档cb6debec及文风255489b3；运行/TeamLab文档bc330ff2及文风a4094050；自动化940c30d9、UTF-8修复4d489746及独立审查修复cdfb2f36。上述提交通过正常merge进入集成分支，业务源码与origin/main一致。

- 11模块提供22个短入口，README约500–640中文字，development35–88行；每个概览有流程图。TeamLab大手册拆为17个参考专题，对接指南拆为7个调用专题，原手册159条HTTP请求行保留，并转接原锚点。
- 共用规则、运维、进度和任务证据按已确认目录归位。旧设计稿与市场材料作为历史保留，生成OpenAPI/安装脚本路径维持既有消费者要求。节点/API/权限/就绪/实机验证范围在迁移中保留。
- 对照源码修正旧前端Token/组件路径（正式vNext使用yn-*、shared/design），以及TeamLab操作枚举、Stopped、镜像准备状态、流式文件操作与权限等明确差异；其他专业参考的语义正确性仍需随业务变化复核。
- 依据用户新增文风要求，概览和开发入口改为功能/流程的正面陈述，保留权限与适用条件，减少否定与自行设问。
- 独立审查发现嵌套列表链接漏检、Setext/引用式标题锚点误判，已修复并加回归。Windows管道输出显式UTF-8。PR数据只解析，不执行；权限contents:read，报告上传artifact。

## 验证与交付

- 29项自动化fixture/临时Git测试通过，包括目录/源码/链接/锚点、代码改名删除、未映射、PR复核、注释示例和UTF-8输出。
- 集成目录检查覆盖178份Markdown，通过且无篇幅警告；模块导航生成一致性通过，Git差异检查通过。
- 首次Draft PR的旧whitespace步骤因浅克隆将merge视作根提交而误扫历史空白；新版fetch-depth=0、基于merge-base比较，只检查实际变更。该失败记录保留，未为通过门禁改历史归档内容。
- [PR #28](https://github.com/Y3X1L2/newGZCTF/pull/28)集中审查本次结构与自动化。最终SHA由本记录Git历史/PR读取，避免自引用提交号。
- 本任务没有生产部署或实机业务操作。前后端全量结果引用PR实际CI，文档工具测试不能替代业务验收。
- GitHub定期流程在该配置进入默认分支后运行；手动/每周影响报告默认仅比较最新一次提交，静态目录检查覆盖全部现行文档。自动生成模块导航；正文与完成状态由代码及验收证据驱动并随PR审查。
- 原审计worktree中的未提交空白修改保持原处；本次四个工作区和任务分支保留审查，主线合入与交接确认后清理。
