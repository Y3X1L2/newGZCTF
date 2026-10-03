# Lab2 release-v2：共享存储恢复、仓库迁移与启动诊断

## 最新已证实事实（后文保留事故时的记录）

用户批准113迁移后已完成：新增scsi1，sdb-storage:113/vm-113-disk-0.qcow2，500GiB，串号YINYU113REG20261003；原256GiB系统盘保留。来宾/dev/sdb/ext4，UUID9f56fbd9-520c-4af3-868b-98000c445994，挂载/srv/yinyu-registry-data。实际工作目录registry，独立回退目录rollback/registry-20261003；服务原目录/var/lib/gzctf-registry/registry通过bind继续使用，地址10.24.0.28:5000及标签/引用不变。

25,049文件、111,092,947,185字节，原目录/工作副本/回退副本分别SHA256与uid/gid/mode和属性比对通过。tree SHA256415d57b047464cc181293a45463afbf25cd6c5ca805303a2c34dbe759606e8f9。Registry/v2、既有manifest摘要、blob HEAD长度通过，冷开机自动挂载与manifest再次读取通过。旧根盘副本仅在独立校验和读取通过后释放；系统盘120→16GiB，共享池92.35%→68.60%。配置备份留新盘migration-20261003受保护目录；回退副本不包含迁移后新导入数据，回退前必须停止写入并处理增量，不能覆盖现工作目录。

四图正常导入ManagedStatic模板519/520/521/522，分别对应118/119/120/121；临时API Token已撤销。新拓扑01a1021a-b3b3-7fd2-96f5-cc9aa22a2ea5，release01a1021a-b760-7d01-87d2-a3da4a190dae。首次runtime01a1021a-b9ed-73bb-b1e6-75c8acd71aad在guest-network-verify失败，ticket01a1021a-bbed-72ed-a0fd-23c428822705错误guest_network_interface_missing；Agent清理四VM/实例盘，不能列为网络签收。返回到主站的错误未标明资产，需要单独定位。

独立正常DHCP诊断模板522：runtime01a1022b-8228-7515-a1ba-4ebb5ffa66ba到ready，QGA回读e1000e的MAC02:42:da:47:5d:99，WMI实际Intel82574L/ConfigManagerErrorCode0/NetEnabledTrue，192.168.240.10/24。PVE源用e1000，存在硬件差异，但实测平台e1000e驱动可用，不能以硬件差异直接判为根因。只读系统/网络，未看题目业务。该专用诊断已在下一次四机重试前通过正常API销毁。

原首失败四机runtime也通过正常API销毁以释放原IP，再创建一次相同release的限定重试，记录每台QGA网卡就绪时间。服务器证据startup-retry1；.31在诊断/导入期间临时禁调度，脚本finally恢复原true。尚未宣布四机网络验收通过，无新平台代码或部署。

## 事故与应急恢复历史

- 用户要求先检查PVE118–121的release-v2，再导入/启动，只做系统/网络，不研究题目业务。四机快照存在、QGA实际脚本前提通过；正常关机冷导出4份qcow2，结构检查/与源盘比较/摘要通过，总14561310208字节，内网SSH直传.27后再次hash/check通过。原PVE/快照/旧模板保留。
- 为沿用原IP，用户明确批准销毁旧Lab2 runtime01a0ecc3-a1ae-7f50-979b-2aff13862b31，正常DELETE后destroyed/assets为空。其余环境保留。新模板将用ManagedStatic、两交换机、web双卡/其它只内网，无平台跨段路由；内网172.22.0.0/23用于避开偏移2保留地址，来宾须同时/23。
- 预分发只按Online/IsSchedulable/KVM/容量筛选，不检查ManagedStatic特征或剩余存储；Agent下载没有磁盘预留。为避免.31复制本批镜像，已正常API暂置节点c08073af-56d7-4b54-b338-04f64ac92bd0的isSchedulable=false，原值true。完成/退出本任务后必须恢复；重新预热可能又触发该边界，不假称已修复。
- 首VM118的正常导入operation01a1010d-c392-74f2-8394-a2affece54f3已Failed/errorCode=operation_failed，没有resourceId；没有取到新模板ID、没有创建四机新runtime。临时导入身份01a1010d-17fd-7cfa-8680-62c69ee7473c已通过正常API撤销204。用户提供PVE另一个LAN入口192.168.30.122，同一主机密钥匹配，现使用PVE SSH管理中继，不改公网网关。
- 实测PVE pve/data428.66GiB Data100%、Meta3.75%，VG空闲16GiB；112/113/114均io-error、115running。宿主根盘66GiB、/data约13TiB可用；来宾内部空闲并不代表共享薄池还有实际块。内核out-of-data-space明确确认本次后段断线的存储原因。
- 已备份VG布局至独立export目录，lvextend+8GiB得到436.66GiB池，VG仍余8GiB；依次恢复112/114/113运行。112根ext4仍rw，导入进程已退出，没有向无关PID发信号；主站短停阻止新写入后已恢复。112/113/114执行fstrim，实际PVE池92.28%、Meta3.60%，四服务VM running。主站/Agent active、Config200，Registry原生docker-registry.service active且/v2/200；既有教学容器Up。未删除业务文件、日志、快照、旧镜像或备份。
- 现场findmnt确认112 images/teamlab仍bind在500GiB大盘；新上传files/tmp/aspnetcore及files/staging/image-imports仍位于根盘（现已无暂存文件）。Registry .28正式数据/var/lib/gzctf-registry/registry约104GiB，在113根盘/local-lvm；113无PVE快照。Docker列表为空，因为Registry是原生服务，不能误判为仓库不存在。
- 新上传链路不走旧ImageStorage上传路径：multipart临时副本→ContentRoot暂存→.28 OCI Registry→Agent缓存。此前500GiB只覆盖112镜像/实例目录，不会自动迁Registry或上传暂存；不能仅凭112 df允许继续大批导入。本轮没有修改源代码、再部署服务、数据迁移或直接数据库写入（只读了指定operation终态）。
- 仓库迁移具体方案已落仓库外REGISTRY-STORAGE-MIGRATION.md，并请求用户批准：113新增来自sdb-storage的500GiB盘，复制/校验104GiB，保留大盘回退副本后释放旧根盘副本，原路径bind以保持仓库地址/标签/引用。涉及新增盘/停仓库/迁移，未获答复前没有执行。批准后先完成这个容量治理，再新操作正常重试导入/发布/启动。
- 证据与脚本根D:/Work/YINYU-Lab2-ReleaseV2-20261003；PVE导出/data/exports/yinyu-lab2-release-v2-20261003；主站原件/srv/yinyu-data/image-authoring/lab2-release-v2-20261003。PLAN.md保存当前任务入口/会话/待恢复设置。教学54笔记只在学习分支本地提交，不推送，凭据/大文件不进Git。

本轮无代码改动，验证为真实镜像/基础设施检查及git diff --check，未重复全量前后端/集成测试。新场景联网、访问、重置、隔离还未验收，不能用镜像文件校验通过代替平台完成。
