# 培训课程动态 Flag 隔离失效修复说明

## 与重复 Flag 标签问题的关系

本问题与“动态题显示多个 `Flag 0` 标签”没有直接因果关系。

- 重复标签问题发生在课程挑战详情模型错误返回实例级 Flag；
- 本问题发生在课程动态 Flag 的生成逻辑；
- 两个问题会同时出现在动态课程题上，但修复文件、行为和回归测试相互独立。

## 问题范围

- 题型：培训课程 `DynamicContainer`；
- 模板：包含 `[TEAM_HASH]` 的动态 Flag 模板；
- 现象：不同学员加载同一道课程题目时，实例 Flag 相同，动态隔离失效。

## 根因

培训课程通过无参数的 `Challenge.GenerateDynamicFlag()` 创建实例 Flag。该重载原先将
`[TEAM_HASH]` 固定替换为 `TestTeamHash`。

`TestTeamHash` 应只用于管理员测试 Flag 预览。正式课程实例使用该固定值后，同一模板每次都会
生成相同结果。

## 修复内容

无参数的正式动态 Flag 生成现在为每个实例生成一个 12 位小写十六进制加密随机值，并用于替换
`[TEAM_HASH]`。

以下行为保持不变：

- 同一个 Flag 模板内多次出现 `[TEAM_HASH]` 时，仍复用同一个值；
- 管理员 `GenerateTestFlag()` 仍使用 `TestTeamHash`，便于稳定预览；
- 比赛参赛队伍使用 `Participation` 计算团队哈希的逻辑不变；
- 不修改数据库结构。

## 已有实例说明

课程实例的 Flag 会持久化到 `ExerciseInstance.FlagContext`。升级后按以下规则处理：

- 从未加载过题目的账号：首次访问时直接生成新的独立 Flag；
- 已加载、当前没有容器且仍保存旧测试哈希的账号：下次访问题目时自动重新生成；
- 当前仍有容器的账号：保持原 Flag，避免容器中的 `GZCTF_FLAG` 与数据库判题值不一致；
- 运行中的旧容器停止后：再次打开题目时自动重新生成。

该升级过程不需要手动修改数据库，也不会在容器运行期间改写判题值。

## 本地 Docker 完整验证

### 前置条件

- Docker Desktop 或 Docker Engine 正在运行；
- 当前目录是仓库根目录；
- 检出的分支包含本次修复。

### PowerShell

```powershell
$repoPath = (Get-Location).Path
docker run --rm `
  --mount "type=bind,source=$repoPath,target=/workspace" `
  --workdir /workspace/src `
  mcr.microsoft.com/dotnet/sdk:10.0-alpine `
  dotnet test GZCTF.Test/GZCTF.Test.csproj `
  --filter "FullyQualifiedName~ChallengeFlagGenerationTests|FullyQualifiedName~ExerciseInstanceLegacyFlagTests" `
  --logger "console;verbosity=minimal"
```

### Bash

```bash
docker run --rm \
  --mount "type=bind,source=$PWD,target=/workspace" \
  --workdir /workspace/src \
  mcr.microsoft.com/dotnet/sdk:10.0-alpine \
  dotnet test GZCTF.Test/GZCTF.Test.csproj \
  --filter "FullyQualifiedName~ChallengeFlagGenerationTests|FullyQualifiedName~ExerciseInstanceLegacyFlagTests" \
  --logger "console;verbosity=minimal"
```

首次运行会拉取官方 `.NET 10 Alpine SDK` 镜像并恢复 NuGet 依赖。验证通过时应看到：

```text
Passed!  - Failed: 0, Passed: 44, Skipped: 0, Total: 44
```

这组测试完整覆盖本次修复依赖的生成规则：

1. 两次正式课程 `[TEAM_HASH]` 生成结果格式均为 12 位小写十六进制；
2. 两次生成结果不相同；
3. 同一模板中的多个 `[TEAM_HASH]` 使用同一个随机值；
4. `[GUID]`、`[LEET]` 和 `[CLEET]` 组合行为保持不变；
5. 管理员测试 Flag 仍使用稳定测试值；
6. 比赛队伍哈希生成行为保持不变。
7. 无运行容器的旧实例会自动升级；
8. 运行中的实例、新格式实例和动态附件实例不会被误改。

## 自动化验证结果

```text
本机定向 Flag 生成与旧实例升级测试：44/44 通过
Docker Alpine SDK 定向测试：44/44 通过
GZCTF.Test 全量单元测试：511/511 通过
GZCTF.Integration.Test：251/251 通过
```

## 页面级验收

1. 部署包含本次修复的版本。
2. 准备两个学员账号；如账号已有运行中的旧容器，先停止容器并刷新题目页面。
3. 分别登录两个独立浏览器会话并打开同一道 `DynamicContainer` 课程题。
4. 分别启动容器，从题目镜像中读取其注入的 Flag。
5. 确认两个 Flag 格式正确且内容不同。
6. 分别向各自账号提交自己的 Flag，确认两次判题均成功。
7. 交叉提交另一个账号的 Flag，确认判题不通过。
8. 在管理端打开测试 Flag 预览，确认预览仍可稳定生成。
