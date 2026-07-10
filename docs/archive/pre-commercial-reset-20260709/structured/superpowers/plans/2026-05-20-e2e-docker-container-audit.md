# Docker 容器全链路 E2E 检测与修复计划

> **For agentic workers:** 必须使用 Playwright 浏览器操作，禁止直接调用 API。每个 Task 需要先列出当前实际行为 vs 预期行为，再进行修复。

**Goal:** 全面检测 Docker 容器生命周期所有环节，修复发现的 Bug，确保从镜像上传→题目配置→容器创建→Flag 提交→容器销毁全链路可用

**Architecture:** 全链路分为 6 个阶段，每个阶段先通过 Playwright 检测当前实际行为，记录问题，修复，再验证

**测试原则:** 所有检测通过 Playwright 在 `http://<test-server-ip>:8080` 执行

---

### 检测范围总览

```
上传镜像  ─→  创建题目  ─→  创建容器实例  ─→  访问容器  ─→  提交 Flag  ─→  销毁容器
  │             │              │                │             │             │
  ├ 文件大小    ├ Dynamic     ├ 进度反馈      ├ 端口映射   ├ 动态 Flag  ├ 停止按钮
  ├ 构建日志    ├ 端口配置    ├ SignalR 更新  ├ 网络可达   ├ 排行榜    ├ 自动过期
  ├ 镜像列表    ├ 资源限制    ├ 多队伍隔离    ├ URL 展示   ├ 一血奖励  ├ 端口释放
  └ DB 记录     └ Flag 模板   └ 异常处理      └ 安全       └ 分数计算  └ 资源回收
```

---

### Task 1: Docker 镜像上传与管理 — 当前实际行为检测

**当前问题（用户报告）：**
- 镜像列表中所有镜像显示 `0.0 MB`（Docker 实际镜像 83.7MB，但 DB 记录的是 zip 大小 1108 字节）

**Files:**
- `src/GZCTF/Controllers/DockerController.cs:33-38` — UploadImage 中 `FileSize = file.Length` 用的是 zip 大小
- `src/GZCTF/ClientApp/src/pages/admin/DockerImages/Index.tsx:40` — 渲染 `(img.fileSize / 1024 / 1024).toFixed(1) + " MB"`
- `src/GZCTF/Services/Docker/DockerImageBuilder.cs` — BuildFromDirectoryAsync 没有返回镜像信息

- [ ] **Step 1: Playwright 检测 — 截图当前 Docker 镜像列表页**

```
操作: 打开 http://<test-server-ip>:8080/admin/dockerimages
检查: 每个镜像行显示的大小值、名称、标签、系统类型、删除按钮
预期: 显示实际 Docker 镜像大小（83.7MB），而非 0.0 MB
实际: 显示 0.0 MB ← Bug
```

- [ ] **Step 2: 修复 FileSize 为实际 Docker 镜像大小**

```csharp
// DockerImageBuilder.cs — BuildFromDirectoryAsync 返回镜像信息
public async Task<(string tag, long size)> BuildFromDirectoryAsync(
    string buildDir, string tag, CancellationToken token)
{
    // ... existing build logic ...
    
    // After successful build, get the actual image size
    var inspectProcess = new Process
    {
        StartInfo = new ProcessStartInfo
        {
            FileName = "docker",
            Arguments = $"inspect --format='{{{{.Size}}}}' \"{tag}\"",
            RedirectStandardOutput = true,
            UseShellExecute = false,
        }
    };
    inspectProcess.Start();
    var sizeStr = await inspectProcess.StandardOutput.ReadToEndAsync(token);
    await inspectProcess.WaitForExitAsync(token);
    long.TryParse(sizeStr.Trim(), out long imageSize);
    
    return (tag, imageSize);
}
```

```csharp
// DockerController.cs — UploadImage 使用实际镜像大小
var (tagResult, imageSize) = await _builder.BuildFromDirectoryAsync(
    dockerfileDir, tag, HttpContext.RequestAborted);
// ...
FileSize = imageSize > 0 ? imageSize : file.Length,
```

- [ ] **Step 3: Playwright 验证 — 上传新镜像并确认大小显示正确**

```
操作: 点击导入镜像 → 选择 zip → 导入
检查: 导入成功后列表中显示实际镜像大小（应为 ~83MB+）
```

### Task 2: DynamicContainer 题目创建完整字段检查

**当前问题：**
- 创建 DynamicContainer 题目时需要设置容器镜像、端口、资源限制
- 从 Playwright 创建时 Mantine Select 组件难以操作
- 缺少容器镜像选择器 UI

- [ ] **Step 1: Playwright 检测 — 创建 DynamicContainer 题目全流程**

```
操作: 比赛管理 → 选择比赛 → 题目管理 → 新建题目
检查:
  □ 题目标题输入框
  □ 题目类别下拉可选 "Web"
  □ 题目类型下拉可选 "动态容器"
  □ 创建后能跳转到题目编辑页
  □ 题目编辑页有「容器配置」区块（容器镜像/内存/CPU/存储/端口）
  □ 容器配置中能输入镜像名称
  □ Flag 模板输入框
  □ 启用/禁用开关
  □ 保存配置按钮
```

- [ ] **Step 2: 修复容器镜像选择为下拉选择（从已注册镜像列表中选择）**

```
当前: 容器镜像输入框为自由文本输入
预期: 应显示为下拉选择框，选项来自已注册的 Docker 镜像列表
```

- [ ] **Step 3: Playwright 验证 — 创建完整 DynamicContainer 题目**

```
操作: 通过 Playwright 创建包含完整配置的 DynamicContainer 题目
验证: 题目创建后在列表中可见，状态为已启用
```

### Task 3: 容器实例创建全流程 — 浏览器端行为检测

**当前问题：**
- `创建实例` 按钮点击后页面没有反馈
- 容器创建成功后页面不更新
- 没有加载状态指示

- [ ] **Step 1: Playwright 检测 — 点击"创建实例"后的页面行为**

```
操作: 以队伍身份进入比赛 → 点击 DynamicContainer 题目
检查:
  □ 显示"本题为容器题目，解题需开启容器实例"
  □ 显示"容器默认有效期为 120 分钟"
  □ "创建实例"按钮可点击
  □ 点击后按钮状态变化（禁用/加载中）
  □ 点击后等待期间加载动画
  □ 创建成功后显示容器连接信息（IP:端口）
  □ 创建失败时显示错误信息
```

- [ ] **Step 2: 修复容器创建后的 UI 反馈**

```
问题: 点击"创建实例"后没有加载状态，成功/失败没有提示
修复: 
  - 添加加载状态 (LoadingOverlay)
  - 添加成功/失败通知 (notifications.show)
  - 添加 SignalR 实时状态更新
  - 刷新题目详情以显示容器信息
```

- [ ] **Step 3: Playwright 验证 — 容器创建完整流程**

```
操作: 在浏览器中点击"创建实例"→ 等待 → 查看结果
验证:
  □ 按钮显示加载状态
  □ 成功后容器连接信息出现
  □ Docker 中确实有容器运行
  □ 端口映射正确
  □ FLAG 环境变量已注入
```

### Task 4: 容器端口映射与访问检测

- [ ] **Step 1: 检测容器端口映射**

```
操作: Docker ps 查看容器端口
验证:
  □ 端口映射到主机端口 (32768-60999 范围)
  □ 端口不冲突
  □ 每次创建新容器分配不同端口
```

- [ ] **Step 2: 检测容器内服务可访问性**

```
操作: curl http://localhost:{port}/
验证:
  □ HTTP 200 响应
  □ 返回正确内容（含 Flag 的 HTML 页面）
  □ 页面中 Flag 显示动态注入的值（非"test_flag_not_set"）
```

- [ ] **Step 3: 修复 server.py 读取错误的 env var**

```python
# scripts/test-challenge/server.py
# 当前读取 FLAG，但平台注入 GZCTF_FLAG
FLAG = os.environ.get('GZCTF_FLAG') or os.environ.get('FLAG', 'flag{test_flag_not_set}')
```

### Task 5: Flag 提交流程深度检测

- [ ] **Step 1: Playwright 检测 — 提交正确 Flag**

```
操作: 打开题目详情 → 输入从容器获取的 Flag → 提交
检查:
  □ 输入框存在（占位符: "羽扇纶巾..."）
  □ "提交 flag" 按钮存在
  □ 提交后显示 "flag 正确"
  □ 显示 "已解出" 标记
  □ 排行榜分数更新
  □ 一血奖励计算正确
```

- [ ] **Step 2: Playwright 检测 — 提交错误 Flag**

```
操作: 输入错误的 flag → 提交
检查:
  □ 显示错误提示
  □ 尝试次数 +1
  □ 不扣分
```

- [ ] **Step 3: Playwright 检测 — 排行榜更新**

```
操作: 进入积分总榜页面
检查:
  □ 排名正确（第 1 名）
  □ 解题数正确
  □ 总分包含一血奖励（1000 + 5% = 1050）
  □ 分数变化曲线图
```

### Task 6: 容器销毁与生命周期管理

- [ ] **Step 1: Playwright 检测 — 手动销毁容器**

```
操作: 在题目详情页或实例管理页查找"销毁"按钮
检查:
  □ "销毁容器" / "停止" 按钮存在
  □ 点击后容器停止
  □ Docker 中容器被移除
  □ 端口释放
```

- [ ] **Step 2: 检测管理员实例管理页**

```
操作: 进入 管理面板 → 实例管理
检查:
  □ 显示所有运行中的容器
  □ 显示队伍/题目/生命周期/容器 ID/访问入口
  □ 可以批量销毁
```

- [ ] **Step 3: 检测容器自动过期**

```
操作: 检查容器到期行为
检查:
  □ 超过默认有效期（120 分钟）后自动销毁
  □ 到期前通知
  □ 可续期
```

### Task 7: DockerComposeDeployer 部署检测

- [ ] **Step 1: Playwright 检测 — Compose 部署弹窗**

```
操作: Docker 镜像页 → 点击 "Compose 部署"
检查:
  □ 弹窗显示
  □ Compose 文件路径输入框
  □ 执行部署按钮
```

### Task 8: 多队伍容器隔离测试

- [ ] **Step 1: 创建第二个队伍并加入比赛**

```
操作: 注册新用户 → 创建第二个队伍 → 加入比赛
检查:
  □ 第二个队伍也能创建容器实例
  □ 分配不同的动态 Flag
  □ 端口不冲突
  □ 容器相互隔离
```

### Task 9: 前端容器状态实时更新

- [ ] **Step 1: 检测 SignalR 连接状态**

```
操作: 打开浏览器控制台 → 检查 WebSocket 连接
检查:
  □ SignalR 连接到 /hub/scenario 或 /hub/user
  □ 容器状态变更时收到推送
  □ 排行榜实时更新
```

---

## 当前已发现的 Bug 清单

| ID | Bug | 位置 | 严重程度 |
|----|-----|------|---------|
| B1 | Docker 镜像列表显示 0.0 MB | `DockerController.cs:UploadImage` FileSize 只记录 zip 大小 | 中 |
| B2 | Docker 镜像页无容器镜像下拉选择（仅自由文本） | `ChallengeEditDetail.tsx` 容器镜像输入框 | 中 |
| B3 | 点击"创建实例"无加载反馈 | `ChallengeDetail.tsx` / 题目展开面板 | 高 |
| B4 | 容器创建后页面不自动更新显示容器信息 | SignalR 推送或轮询缺失 | 高 |
| B5 | server.py 读取 FLAG 而非 GZCTF_FLAG | `scripts/test-challenge/server.py` | 高 |
| B6 | 容器销毁按钮在 UI 中不可见 | 题目详情页缺少停止容器功能 | 高 |
| B7 | 实例管理页可能不显示运行中的容器 | 数据刷新机制 | 中 |

---

## 检测工具与命令

```bash
# Docker 操作
docker ps -a                    # 查看所有容器
docker images                   # 查看所有镜像
docker inspect --format='{{.Size}}' gzctf/docker-ctf-challenge:20260520  # 查看实际镜像大小

# 访问容器内服务
curl -sv http://localhost:{port}/

# 获取容器内 Flag
docker exec {container_name} sh -c 'echo $GZCTF_FLAG'

# 查看服务器日志
grep -i "container\|image\|size\|error\|instance" /tmp/gzctf.log | tail -20
```

---

## 执行说明

每完成一个 Task 需：
1. 截图保存 Playwright 检测结果
2. 记录发现的问题
3. 修复代码
4. 重新检测验证
5. 更新问题清单
