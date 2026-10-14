# 镜像从登记到回收

镜像模板是全局内容资产，节点缓存是为运行需求准备的执行副本。制作、登记、绑定、准备和回收各有事实，不能用一条 Ready 覆盖整条链路。

## 登记和绑定

Docker reference/archive 与 VM qcow2 经 Content 导入服务校验来源、大小、摘要及类型；文件上传进入受控 staging，持久化 image.import operation 由 worker 处理。模板保存不可变制品标识和能力 metadata，业务保存自己的显式绑定。TeamLab release 只保存模板 ID 和冻结摘要，不拥有源文件；当前模板摘要改变时，旧发布不能悄悄部署新内容。

[OpenImagesController](../../../../src/GZCTF/Modules/Content/Api/OpenImagesController.cs) 是外部入口，认证、scope、资源 grant、Idempotency-Key 和上传 Content-Digest 遵守 [API 标准](../../../architecture/external-api-standard.md)。服务端不能把第三方 Registry 凭据混入普通引用、日志或 operation 错误。

## 创建时准备选中节点

1. 业务完成授权和模板检查，Runtime 写统一部署票据并选择适配节点。
2. 运行需求登记到选中节点的分发引用，已有匹配摘要的 Ready 缓存复用。
3. 缺少副本时 Fleet 分发 worker 调用 Agent 下载、校验与容量保护。
4. 下载/校验失败保留分发错误；是否可重试遵守该记录的 Retryable，不能无条件重复。
5. 节点副本就绪后继续创建容器或 VM，运行状态和访问就绪另行推进。

显式 TeamLab 发布预热向全部适配节点建立可续期需求，普通发布和创建不调用这条预热路径。当前 preparation state 为 onDemand、readyToStart 或 blocked：onDemand 允许选中节点后下载；readyToStart 表示每个模板已有至少一个适配缓存，仍不保证本次节点；blocked 表示源模板、摘要或能力存在阻碍。planAvailable 不是容量预留，其他节点失败不自动阻断此次创建。

## 引用和删除

模板引用与分发引用分别检查。CTF、练习、课程、TeamLab 和运行资源通过公开 reference provider 提供有效使用事实；模板创建者或管理员请求删除时，存在有效引用返回 asset_in_use，不使用 cascade 删除消费者。主模板的 Deleting 与节点缓存的分发/清理状态仍各自表达。

runtime、rollout、课程、比赛和发布预热释放自己的引用，不删除其他消费者的引用。后台对账清理最后消费者释放后的节点缓存时，还要保护实际 VM backing 文件和在途工作；Registry 主副本不随一次比赛或 runtime 销毁而删除。源文件清理、模板退役与 Registry GC 分别处理，手工 GC 的一次成功不能作为自动 GC 或冷缓存 TTL 已实现的证据。

## 源码和证据

实现定位见 [开发地图](../development.md)。关键入口为 ImageTemplateReferenceService、IImageTemplateCatalog、ImageImportApplicationService 与 Fleet/ImageDistributionService；Agent 的 AgentImageStorageBudget、AgentImageDownloadWriter 和 ImageTransferSingleFlight 负责本机容量及传输收敛。

[10-09 第二批发布](../../../records/2026-10-09-image-lifecycle-second-rollout.md) 是限定环境回收证据；本页只解释职责。当前部署、冷缓存和自动回收缺口见 [当前状态](../../../current-state.md)，现场配置见 [Registry](../../../operations/registry-deployment.md) 与 [节点部署](../../../operations/node-deployment.md)。
