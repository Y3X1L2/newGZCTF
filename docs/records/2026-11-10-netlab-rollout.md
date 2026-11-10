# Netlab 接入上线

状态：已上线，场景启停通过；流量解析异常保留待办。

## 本次完成

- 用户授权合并 [PR #42](https://github.com/Y3X1L2/newGZCTF/pull/42) 并部署。主站 `.27` 于北京时间 11 月 10 日 21:34:29 切换，约 14 秒恢复服务。前后端来自合并提交 `8d9efc7c`，原 Agent 未更新。
- 按用户选择，在 `.40` 建立独立 `YINYU` 项目，迁入旧平台的 release-v2 四机场景并发布版本。四个镜像均从原 Registry 导入，来源摘要及大小核对通过，模板均为 ready；没有用 initial 版本替代。
- 原四台机器、两个网段和地址保留。原平台历史数据、镜像，以及 Netlab default 项目和原有 7 台运行虚拟机保持原样。
- 主站保存独立项目连接，普通服务令牌在服务端加密保存，临时明文文件已删除。

## 实际验证

- 主站点击运行，四台虚拟机全部 running，任务 succeeded；四台来宾代理均响应。随后点击停止，四台全部 stopped，任务 succeeded。场景及发布版本保留，当前不占用运行内存。
- 性能页显示 CPU、内存和网络历史记录；磁盘 I/O 当时无记录。流量页返回 409 报文解析错误，**流量查看未验收通过**，见 [NETLAB-02](../backlog.md)。
- 首页、配置接口、附件下载及摘要校验通过；3 个原节点在线，活跃队列为 0。530 个模板 Ready，分发 240 Ready / 1 既有 Failed。主站错误级别日志无新增项，健康检查保持既有 Degraded。
- PR 与 main 的 Quality、Documentation 均通过，详见 [主线检查](https://github.com/Y3X1L2/newGZCTF/actions/runs/42822192345)。本地完成后端发布，复用 main 前端制品，没有重复全量测试。恢复依赖时仍有既有 NuGet 漏洞告警。
- 本轮未重做比赛计分、选手访问、VPN、桌面登录、Docker 完整生命周期和图表滚轮交互验收；不能以场景启停替代这些验证。

## 备份与迁移

主站双库、附件和加密配置备份均通过校验。双库实际恢复用时 8 分 9 秒，177 个用户、76 个战队、3 个节点和原场景数据保留。新迁移先在恢复副本上通过，再用于生产；迁移由 152 条增加为 153 条，旧场景和发布版本各 1 条，旧运行实例和比赛组网绑定均为 0。恢复容器和临时数据已清理。

Netlab 数据库另有迁移前备份，已检查摘要、非空和备份目录；本轮没有做该库恢复演练。底座仍为 v1.2.2，虚拟机初始化文件补丁 [Netlab PR #1](https://github.com/scmypapa/netlab/pull/1) 没有合并或部署，VM 动态 Flag 文件注入未验收。后续统一见 [NETLAB-01 / NETLAB-02](../backlog.md)。

## 回退与证据

| 内容 | 位置或身份 |
| --- | --- |
| 当前版 | `/srv/yinyu-data/releases/netlab-8d9efc7c0fe39bed08f604913a460a221d1e70fd-20261110/publish` |
| 上一版 | `/srv/yinyu-data/releases/ui-polish-cf473cad3582dac8fd05e71f3cd0b1ad155fa979-20261109/publish` |
| 主站备份 | `/srv/yinyu-data/backups/netlab-8d9efc7c0fe39bed08f604913a460a221d1e70fd-20261110-pre-20261110T131459Z` |
| Netlab 备份 | `.40:/var/backups/netlab/yinyu-pr42-20261110/netlab.dump` |
| 新场景 | `e93930e3-ae19-4956-950d-2e0e518df0f8`；项目 `YINYU` |
| 发布版本 | `a700ff60-316f-47bc-ad25-1d47d4764bf9` |
| 启动 / 停止任务 | `093bd2cb-7513-4093-817b-af7bfa68cab5` / `baac4b27-7178-4887-a28c-83b1d944f4f9` |

发布包 SHA256 为 `89587fc235d6877324c721239b8a4947a5521f001e5528dbc541586078b88f81`。前端清单为 271 个文件，数据库头为 `20261110070457_IntegrateNetlabSceneBindings`。完整证据保存在主站发布目录上一级、Netlab 备份目录的 `evidence/`，以及本机 `D:/Work/netlab-rollout-20261110/`，不提交制品或凭据。

主站发布目录上一级保留 `rollback.sh`，语法检查通过，本次没有实际回退。它先停止主站、检查新增比赛绑定，再原子切回上一版；遇到旧程序无法读取的新绑定会停止并要求审查数据库恢复。新增数据库结构保留，不执行 EF 降级；完整恢复须结合备份和切换后新增数据另行判断。主站回退不删除 Netlab 项目和场景。
