# TeamLab 比赛仿真框架

该工具通过平台 API 和资产真实地址运行，不读取数据库，也不直接操作 Docker、libvirt 或 Agent。运行结果保存在 `artifacts/teamlab-match/`，凭据只从环境变量读取。

## 启动

1. 从 `scenario.example.json` 复制一份本次测试配置，填入两个运行环境及服务入口对应的环境变量。
2. 设置 `GZCTF_BASE_URL` 和受限测试 Token。
3. 启动：

```bash
docker build -t yinyu/teamlab-match-runner scripts/validation/teamlab-match
docker run --rm -p 19090:19090 \
  --env-file /path/to/test.env \
  -v "$PWD/artifacts/teamlab-match:/app/artifacts/teamlab-match" \
  yinyu/teamlab-match-runner \
  --scenario scenario.example.json --listen 0.0.0.0:19090
```

打开 `http://localhost:19090` 查看实时进度。首次接线时可追加 `--duration 120` 做两分钟短流程检查。

高并发数据面测试必须显式指定 Worker 的内网地址，不使用临时公网网关：

```bash
python stress_run.py \
  --base-url http://10.24.0.27:8080 \
  --data-plane-host 10.24.0.27 \
  --phases 500x30,1000x30,1500x30,2000x210
```

## 场景配置

- `teams` 定义两队常态/突发并发人数和实际 HTTP、TCP、Modbus、API 动作。每 15 分钟的前 60 秒使用 `burstUsers`，其余时间使用 `virtualUsers`。
- `monitors` 定义节点、运行环境和平台状态采样接口。
- `stages` 定义裁判动作时间线。每个动作都是平台正式 API 请求，支持 `method`、`path` 和 `body`。
- `${NAME}` 必须由环境变量提供；缺失时框架会在发出请求前退出，避免在错误目标上运行。

监控项默认只记录采样成功和列表数量；需要展示状态字段时，在监控项中使用 `select` 明确列出。报告只记录聚合指标、状态变化和归并后的错误，不记录 Token、Cookie、Flag 或完整响应正文。
