# 附件资产：上传不等于绑定

内容文件按 SHA-256 Hash 复用；附件把业务对象与文件关联。两个题目可以引用同一 Blob，但上传一个文件本身不创建题目附件引用，也不授权其他业务读取它。

## 外部上传与读取

[OpenAssetsController](../../../../src/GZCTF/Modules/Content/Api/OpenAssetsController.cs) 提供 `POST /api/open/v1/assets` 和 `GET /api/open/v1/assets/{hash}`。上传使用 multipart/form-data，必须有 assets:write、资源授权、Idempotency-Key 和 `Content-Digest: sha-256=:base64:`；文件内容 1 byte–1 GiB，名称 1–255 字符，不含路径分隔符或控制字符。

AssetApplicationService 校验声明长度和实际摘要，在事务中提交 Blob 与已完成的 asset.upload operation。首次上传和同幂等键复用都返回 201 与资产描述；Operation-Location 指向对应 operation。该任务在事务内直接标 Succeeded，不会让后台 worker 看到尚未完成的 Pending 上传。

重复上传相同内容保留已有文件的名称和引用计数；同 key 不同规范化请求仍是 idempotency_conflict。同 key 原任务虽成功但文件已删除则返回 asset_gone，不能伪造上传成功。描述包含 hash、name、size、remoteUrl，不表示文件已绑定到某个题目。

读取需要 assets:read 及对应资源授权，服务还检查当前 actor 的成功上传记录或显式 grant；没有访问资格或文件不存在返回 404。不能仅因 Hash 可猜或同用户持有其他 token 就跳过授权检查。

## 业务绑定与解绑

题目、课程等导入/修改用例负责把文件资产变成显式附件，更新引用计数与业务拥有关系；Content 不把运行实例、计分或报名当作文件所有权。删除课程快照或题目时只释放本对象引用，不能误删其他对象使用的文件或全局镜像模板。

主要模型在 [Attachment](../../../../src/GZCTF/Models/Data/Attachment.cs)，上传与 Blob 复用在 [AssetApplicationService](../../../../src/GZCTF/Modules/Content/Application/AssetApplicationService.cs)。对应业务绑定入口由 CTF、Exercise、Training 的 application 使用，后续实现调整应同时核对这些消费者。

文件内容、Flag 或凭据不得进入日志、审计快照与文档。上传协议、认证及幂等的公共要求见 [API 标准](../../../architecture/external-api-standard.md)，代码/测试选择见 [开发地图](../development.md)。自动 Blob GC 的覆盖范围见 [当前状态](../../../current-state.md)，不得从一次模板 GC 推导附件自动清理已经闭环。
