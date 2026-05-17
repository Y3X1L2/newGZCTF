<!--
  Sync Impact Report
  ==================
  Version change: 0.0.0 (template) → 1.0.0 (initial ratification)
  Modified principles: N/A (initial creation, all 7 principles newly established)
  Added sections:
    - Core Principles (7 principles, expanded from template's 5 slots)
    - 附加约束 (Additional Constraints)
    - 开发工作流 (Development Workflow)
  Removed sections: None (template placeholders fully replaced)
  Templates requiring updates:
    - .specify/templates/plan-template.md: ✅ No changes required (Constitution Check is dynamically filled)
    - .specify/templates/spec-template.md: ✅ No structural changes required (testing emphasis is captured in User Scenarios & Testing section)
    - .specify/templates/tasks-template.md: ✅ Updated — test tasks changed from "OPTIONAL" to "MANDATORY E2E" per Principle II; sample test tasks now reference Playwright E2E tests
    - .specify/templates/checklist-template.md: ✅ No changes required
  Follow-up TODOs: None
-->

# 新GZ开发平台 Constitution

## Core Principles

### I. 生产级完整交付 (Production-Ready Delivery)

所有功能必须以生产级标准交付，严禁"半成品"思维或临时补丁。每项功能必须满足以下要求：

- 包含健壮的异常处理结构，覆盖可预见的错误路径
- 边界条件和极端输入必须经过验证并有明确反馈
- 用户体验设计必须无缝衔接，无断裂或不一致的交互流
- 关键操作节点必须提供清晰的上下文提示（如加载状态、空状态、错误提示）
- 交付即上线：功能完成即达到可部署至生产环境的质量标准

**理由**：本项目依赖具备自主长时工作能力的智能体进行开发，目标是在规定周期
内达到完全可用的生产级交付。任何低于上线标准的功能交付都将破坏项目的时间线
和信任基础。

### II. 强制端到端测试与防回归 (Strict E2E Testing via Playwright)

所有新特性以及 UI 变动必须使用 Playwright 编写自动化端到端测试。此测试承担
双重职责：

- **验证新功能正确性**：确保新实现的特性在所有目标浏览器环境中行为符合预期
- **防回归**：检查 UI 样式的完整性，确保新特性的合入不对系统现有功能造成任何
  破坏性回归（No Regression）

测试必须覆盖以下场景：
- 正常用户操作流程（Happy Path）
- 边界条件与异常状态（Edge Cases & Error States）
- 与现有功能的交互点（Integration Points）
- 响应式布局与视觉完整性（Visual Integrity）

所有测试必须在 CI/CD 流水线中自动执行，失败时阻断合并。

**理由**：在 AI 驱动的开发模式下，自动化端到端测试是保障系统稳定性的最后防线。
UI 回归是最常见且最容易被忽视的缺陷类型，必须由自动化测试而非人工审查来捕获。

### III. 架构一致性与平滑扩展 (Architecture Consistency & Smooth Extension)

所有设计与实现必须自然融入现有技术栈。具体要求：

- **优先复用**：优先应用系统已有的设计模式、组件库和通用扩展机制进行开发，
  避免为每个新功能引入独特的架构范式
- **数据兼容**：涉及底层数据模型调整时，必须保障数据演进的安全与向下兼容。
  禁止以破坏性方式修改已有数据结构和 API 契约
- **渐进式变更**：大型重构必须分阶段进行，每一阶段保持系统可运行状态，
  避免"大爆炸式"改造
- **无侵入性**：新功能不得对现有系统的稳定性、性能或行为产生侵入性破坏

**理由**：项目在持续演进中，架构一致性是保证多人/多智能体协作效率的前提。
任意引入新模式或破坏性变更将导致系统熵增，最终使维护成本失控。

### IV. 弹性的系统集成与异步处理 (Robust Integration & Asynchronous Processing)

针对高耗时计算、外部依赖调用或第三方服务（如大模型 API），必须采用
健壮的异步架构与可靠的任务模型。设计必须自带以下保障机制：

- **超时控制**：每个外部调用必须设定合理超时上限，防止资源泄漏与请求堆积
- **重试策略**：对可重试的失败（如网络抖动、临时不可用）实施指数退避重试
- **错误兜底（Fallback）**：外部服务不可用时提供降级方案，确保核心业务链路
  不被阻塞
- **任务可靠性**：异步任务必须具备持久化能力，防止进程崩溃导致任务丢失
- **资源隔离**：外部依赖的故障不得扩散至平台核心服务

**理由**：外部服务的波动是不可控变量。弹性架构确保平台在面对第三方故障时
仍能维持核心业务的可用性，避免级联故障。

### V. 严苛的安全底线与可观测性 (Security Boundaries & Observability)

安全与可观测性贯穿所有层级，具体要求：

**安全层面**：
- 严格遵循既有的基于角色的访问控制（RBAC）体系，每个 API 端点必须
  执行权限校验
- 防御越权行为（Horizontal & Vertical Privilege Escalation）
- 防御注入攻击（SQL Injection、XSS、命令注入等），遵循 OWASP 安全编码规范
- 敏感数据（密钥、凭证、用户隐私）必须加密存储，禁止明文记录于日志中

**可观测性层面**：
- 关键业务链路必须提供结构化日志（JSON 格式），包含 Trace ID 用于全链路追踪
- 核心指标（延迟、错误率、吞吐量）必须暴露监控锚点（Metrics）
- 异常与错误必须具备充足上下文信息，确保可追溯至根因
- 关键操作必须记录审计日志（Audit Log）

**理由**：在 RBAC 体系下，安全是系统的基础属性而非附加功能。可观测性是
保障系统行为全时可见的前提，尤其在 AI 驱动的开发模式下，结构化日志是
排查问题的核心手段。

### VI. 规范化的版本控制与保护策略 (Disciplined Version Control & Branch Protection)

版本控制纪律是协作的基础，必须严格遵守以下规则：

- **特性分支开发**：所有功能开发必须在特性（spec）分支上进行，严禁在主干
  分支（如 main/develop）上直接修改和构建
- **原子化提交**：在完成单一独立逻辑单元后必须及时执行 `git commit` 和
  `git push`，确保进度步步为营，避免大量未提交代码的堆积
- **主干保护**：如果用户明确指令在主干分支操作，必须在执行前发出明确的
  警告与提醒，确认后方可继续
- **提交信息规范**：提交信息必须清晰描述变更的动机与影响，便于后续审计
  和历史回溯
- **分支命名规范**：特性分支必须遵循 `###-feature-name` 的命名约定

**理由**：在 AI 辅助开发场景下，模型可能产生大量代码变更。严格的版本控制
纪律确保每一步变更都可追溯、可回滚，防止不可逆的破坏性操作。

### VII. 中文本地化原则 (Chinese Localization)

所有 Spec Kit 相关的文档必须以中文为主要编写语言。具体要求：

- Constitution、Spec、Plan、Tasks、Checklist 等核心文档必须使用中文撰写
- 技术术语可保留英文原文，但在首次出现时应附带中文说明
- 代码注释与 Git 提交信息推荐使用中文（不强制），以保持团队沟通的一致性
- 用户界面文本、错误提示、系统通知等面向用户的内容必须支持中文

**理由**：团队以中文为主要工作语言，强制中文本地化确保所有参与者能够
无障碍理解项目文档，降低沟通成本。

## 附加约束 (Additional Constraints)

### 技术栈要求

- 项目类型与语言/框架版本由各特性 spec 的 Technical Context 单独定义
- 引入新的第三方依赖前必须评估其许可证兼容性、安全记录与维护活跃度
- 禁止引入存在已知高危漏洞（Critical/High CVE）且无修复版本的依赖

### 性能标准

- 面向用户的操作（页面加载、接口响应）应在 3 秒内完成
- 异步/后台任务的执行状态必须对用户可见并可追踪
- 批量操作必须支持分页或流式处理，避免单次请求超时

### 部署与运维

- 所有配置必须通过环境变量或配置中心注入，禁止硬编码
- 部署流程必须支持灰度发布与快速回滚

## 开发工作流 (Development Workflow)

### 特性开发流程

1. **Specify**：基于自然语言需求创建或更新特性规格（spec.md）
2. **Clarify**（如需要）：针对规格中的模糊点进行澄清
3. **Plan**：生成技术实现方案（plan.md），包含架构设计与技术选型
4. **Tasks**：生成可执行的、依赖排序的任务列表（tasks.md）
5. **Implement**：按任务列表逐一实现，每个任务完成后执行原子化提交
6. **Checklist**：基于 feature 上下文生成自定义检查清单，确保交付质量

### 质量门禁

- 所有代码变更必须通过 Playwright E2E 测试套件
- Constitution Check 必须在 Plan 阶段通过，并在 Implement 阶段结束后复验
- Pull Request 必须经过代码审查后方可合并至主干
- 关键安全变更必须额外经过安全审查

### AI 智能体协作原则

- AI 智能体应具备自主长时工作能力，在特性分支上独立完成开发任务
- 智能体在做出关键架构决策前应记录理由（ADR 风格）于 plan.md 或 research.md
- 智能体不得在未经用户确认的情况下修改 Constitution、合并分支或部署至生产环境

## Governance

本 Constitution 是项目的最高指导文件，其权威性高于所有其他实践惯例和编码指南。

**修订流程**：
1. 提议修订需附带修订理由和影响分析
2. 修订内容需在 Constitution 文件中更新，并同步更新受影响的模板文件
3. 版本号按照语义化版本规则递增：
   - MAJOR：不兼容的原则移除或重新定义
   - MINOR：新增原则/章节或实质性扩展指导
   - PATCH：澄清、措辞修正、排版修复等非语义性优化

**合规审查**：
- 所有 Plan 必须包含 Constitution Check，逐条验证方案与 Constitution 的一致性
- 任何违反 Constitution 的设计必须在 Complexity Tracking 中记录并充分论证
- 定期（每个 release cycle）审查 Constitution 的适用性，根据需要提出修订

**版本**：1.0.0 | **批准日期**：2026-05-16 | **最近修订**：2026-05-16
