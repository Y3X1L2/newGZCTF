# 任务交接记录

本目录保存仍在进行或刚完成的跨会话、跨人员任务记录。文件命名格式：

```text
YYYY-MM-DD-<task-name>.md
```

使用 `../task-handoff-template.md` 创建记录。任务结束后保留最终提交、测试和部署证据；已经没有后续动作的历史记录可以移入 `../../archive/implementation-records/`。

每条记录必须明确：

- 目标、范围和明确不做的内容；
- 起始提交、任务分支和 worktree；
- `VERIFIED`、`IMPLEMENTED`、`NOT_RUN`、`BLOCKED`、`OPERATOR_ONLY` 状态；
- 已完成、下一步和阻塞原因；
- 测试结果、发布物、备份和回滚方式。

禁止写入密码、Token、Cookie、私钥、Flag、完整连接串和未脱敏的运行日志。
