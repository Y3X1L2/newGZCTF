# AWDP (Attack with Defense Plus) 比赛模式 — 设计与调研文档

> 版本：v1.0  
> 日期：2026-06-06  
> 关联需求：在 NEWGZCTF 平台已有 AWD 模式基础上，新增 AWDP 比赛形式

---

## 1. AWDP 比赛形式调研

### 1.1 AWDP 的定义与起源

AWDP（Attack with Defense Plus，攻防增强模式）是由永信至诚 e 春秋未来安全研究院和春秋 Game 团队在 2019 年第十二届全国大学生信息安全竞赛创新实践能力赛全国总决赛上推出的 AWD 新版本。

**核心定位**：AWDP 是一种综合考核参赛团队攻击、防御技术能力、即时策略的攻防兼备比赛模式，本质上是"解题 + 加固"的静态攻防赛。

### 1.2 AWDP 与传统 AWD 的核心区别

| 维度 | AWD（传统攻防） | AWDP（攻防增强） |
|------|----------------|-----------------|
| **队伍间关系** | 互为攻防，直接攻击其他队伍靶机 | 队伍间互不干扰，独立环境 |
| **靶机环境** | 每队配置相同，可互相访问 | 每队完全独立，无法直接攻击其他队伍 |
| **攻击方式** | 直接攻击其他队伍的服务获取 flag | 类似解题赛，对平台提供的题目环境发起攻击提交 flag |
| **防御方式** | 修补自身服务漏洞，防止被攻击 | 上传修补包（tar.gz），平台自动验证修补是否成功 |
| **Flag 刷新** | 每轮刷新 flag | 攻击成功后每轮自动帮本队攻击其他战队获取动态积分 |
| **计分方式** | 零和博弈（攻击得分 = 被攻击失分） | 攻击分 + 修补分 + SLA 分，非零和 |
| **通用防御问题** | 存在 WAF 等通用防御导致攻击困难 | 消除了通用防御干扰，回归纯粹技术较量 |
| **资源消耗** | 每队需要多个容器，资源消耗大 | 资源消耗更小，每队独立环境 |
| **公平性** | 队伍间可能互相干扰 | 消除了队伍间干扰，更公平 |

### 1.3 AWDP 比赛全流程

```
比赛开始
  │
  ├─ 1. 服务发现阶段（约 30 分钟，不计分）
  │    └─ 选手获取题目信息、源码下载、环境访问
  │
  ├─ 2. 攻防对抗阶段（轮次制，每轮 20-30 分钟）
  │    │
  │    ├─ 攻击环节：
  │    │    ├─ 平台给出题目访问链接
  │    │    ├─ 选手按解题模式做题
  │    │    ├─ 提交正确 flag 完成攻击
  │    │    └─ 攻击成功后，每轮自动帮本队攻击其他战队获取动态积分
  │    │
  │    ├─ 防御（修补）环节：
  │    │    ├─ 选手下载题目附件包（含部分/完整源码）
  │    │    ├─ 本地修补漏洞
  │    │    ├─ 制作修补包（xxx.tar.gz，含 update.sh）
  │    │    ├─ 通过 FTP/平台上传修补包
  │    │    ├─ 点击"申请判定"按钮
  │    │    └─ 平台自动验证：解压执行 update.sh → 运行 checker → 运行 exp
  │    │
  │    └─ 轮次结算：
  │         ├─ 攻击成功：获得该题攻击分（动态积分）
  │         ├─ 修补成功（checker 通过 + exp 失败）：获得修补分
  │         ├─ 修补异常（checker 失败）：扣除异常分（如 200 分/轮）
  │         └─ 服务正常：获得 SLA 分
  │
  └─ 3. 比赛结束
       └─ 冻结排行榜，公布最终排名
```

### 1.4 AWDP 六种题目状态

| 状态 | 含义 |
|------|------|
| **未攻击** | 尚未提交该题的 flag |
| **已攻击** | 成功提交 flag，攻击成功 |
| **未防御** | 尚未上传修补包或修补未通过验证 |
| **已防御** | 修补包通过验证（checker 通过 + exp 失败） |
| **防御异常** | 修补导致服务异常（checker 失败） |
| **防御失败** | 修补后漏洞仍存在（exp 仍然成功） |

### 1.5 AWDP 修补包格式

```
update.tar.gz
├── update.sh          # 必须包含的可执行脚本
├── file1              # 修补后的文件
├── file2              # 修补后的文件
└── ...
```

`update.sh` 示例：
```bash
#!/bin/bash
cp index.php /var/www/html/index.php
cp config.php /var/www/html/config.php
```

### 1.6 AWDP 修补验证流程

```
选手上传修补包 → 平台创建干净环境 → 解压执行 update.sh
  │
  ├─ 运行 Checker（检查服务功能是否正常）
  │    ├─ Checker 失败 → 防御异常（扣分）
  │    └─ Checker 成功 → 继续
  │
  └─ 运行 Exp（检查漏洞是否被修补）
       ├─ Exp 成功 → 防御失败（不扣分，但不得修补分）
       └─ Exp 失败 → 防御成功（获得修补分）
```

### 1.7 AWDP 计分规则（参考 CISCN 国赛标准）

1. **动态积分**：攻击分和修补分均采用动态积分，公式为：
   ```
   得分 = (x-1)² × [(50-a)/s²] + a
   ```
   其中 `a` 为初始分数（如 500），`s` 为参赛队伍数，`x` 为解题先后排名

2. **滚轮加分**：每轮按当轮该题攻击/修补成功团队数对应的得分进行加分

3. **前 20 名加成**：设置分数加成机制（第 1 名 5%，第 2 名 4.9%，...递减）

4. **服务异常扣分**：修补导致服务异常，每轮每题扣除固定分数（如 200 分）

5. **重置次数限制**：每题有有限的重置次数（如 10 次）和修补申请次数（如 15 次）

6. **一键恢复**：服务异常时可点击"一键恢复正常"消除异常状态（消耗恢复次数）

### 1.8 知名 AWDP 平台参考

| 平台 | 特点 | 技术栈 |
|------|------|--------|
| **永信至诚 e 春秋** | AWDP 原创者，CISCN 官方平台 | 私有 |
| **Cardinal** | Vidar-Team 开发，Go 编写 | Go |
| **H1ve** | D0g3-Lab 开发，支持解题+AWD | Python/Flask/CTFd |
| **CTF_AWD_Platform** | Django 框架 | Python/Django |
| **Traboda Arena** | 支持 Jeopardy/AD/KOH | 私有 |

---

## 2. 项目现有 AWD 实现分析

### 2.1 已有 AWD 代码清单

| 文件 | 路径 | 状态 |
|------|------|------|
| AwdService.cs | Models/Data/ | ✅ 已实现 |
| AwdServiceInstance.cs | Models/Data/ | ✅ 已实现 |
| AwdRound.cs | Models/Data/ | ✅ 已实现 |
| AwdFlag.cs | Models/Data/ | ✅ 已实现 |
| AwdCheckerTask.cs | Models/Data/ | ✅ 已实现 |
| AwdServiceModels.cs | Models/Request/Game/ | ✅ 已实现 |
| AwdAdminController.cs | Controllers/ | ✅ 已实现 |
| AwdPlayerController.cs | Controllers/ | ✅ 已实现 |
| AwdRepository.cs | Repositories/ | ✅ 已实现 |
| IAwdRepository.cs | Repositories/Interface/ | ✅ 已实现 |
| AwdInstanceService.cs | Services/ | ✅ 已实现 |
| AwdRoundService.cs | Services/ | ✅ 已实现 |
| AwdCheckerService.cs | Services/ | ✅ 已实现 |
| AwdScoreService.cs | Services/ | ✅ 已实现 |
| Awd.tsx | ClientApp/pages/games/[id]/ | ✅ 已实现 |
| AwdServices.tsx | ClientApp/pages/admin/games/ | ✅ 已实现 |
| AwdApi.ts | ClientApp/Api/ | ✅ 已实现 |

### 2.2 现有 AWD 架构总结

```
Game (GameType=AWD/Mixed)
  └── AwdService (题目/服务定义)
       ├── AwdServiceInstance (每队一个容器实例)
       ├── AwdRound (轮次记录)
       │    ├── AwdFlag (每轮每队每服务一个 flag)
       │    └── AwdCheckerTask (checker 执行结果)
       └── 关联到原生 Container 表

轮次驱动：AwdRoundService (IHostedService)
  → 每轮：生成 Flag → 注入容器 → 执行 Checker → 等待 → 结算得分

计分：AwdScoreService
  → 攻击分 + SLA 分 - 被攻击失分（零和博弈）
  → 写入 Submission + FirstSolve → 原生 GenScoreboard 自动计算

前端：Awd.tsx (选手) + AwdServices.tsx (管理员)
  → 轮次倒计时 + 服务状态矩阵 + Flag 提交 + 攻击日志 + 排行榜
```

---

## 3. AWDP 扩展所需的关键改造点

### 3.1 核心差异：AWDP vs AWD

| 功能 | AWD 现状 | AWDP 需求 |
|------|----------|-----------|
| **比赛模式** | GameType.AWD=1 | 需新增 GameType.AWDP=4 |
| **轮次内阶段** | 单一阶段（攻击） | 双阶段（攻击 + 修补） |
| **修补机制** | 无 | 上传修补包 → 平台验证 → 判定修补成功/失败 |
| **修补验证** | 无 | Checker(功能) + Exp(漏洞) 双重验证 |
| **计分维度** | 攻击分 + SLA - 防守失分 | 攻击分 + 修补分 + SLA - 异常扣分 |
| **Flag 生命周期** | 每轮刷新 | 攻击成功后持续有效，修补成功后 flag 不变 |
| **题目状态** | UP/DOWN/MUMBLE | 六种状态（未攻击/已攻击/未防御/已防御/防御异常/防御失败） |
| **重置机制** | 管理员手动重置 | 选手自助重置（有限次数） |
| **一键恢复** | 无 | 修补异常时可一键恢复（有限次数） |

