# TeamLab网卡就绪修正：已部署，Lab2四机基础网络验收通过

## 最终结果

本次正常发布新场景release01a10261-890a-718f-ae86-250e4db86746，runtime01a1022f-7e91-7ad3-8309-833af4771430 generation4已ready，留四机运行供用户验收。所有分片仅.27，Agent源码cc6c5737/实际SHA9b2142e9731d77495322e20a100432a728cf3d86f87dc67abebf85ed58a9b3e8。原PVE/快照/旧模板/旧release/备份保留，没有读或改题目业务。

独立QGA按域归属/原生UUID/当前硬件MAC读取真实网络，四机均verified：web01两卡ens18=10.66.0.15/24、ens19=172.22.1.15/23；OA单卡ens18=172.22.1.18/23；DC单卡Ethernet 2=172.22.1.2/23、DNS127.0.0.1；旧Windows单卡本地连接3=172.22.1.21/23、DNS172.22.1.2。Linux无DNS，四机无默认网关/路由。

web01/OA分别实测到内网web/OA SSH22、DC RPC135、旧Windows RDP3389均可达。OA到入口10.66.0.15:22不可达，平台关联Logical_Router计数0，保持原两网段关系，没有额外平台跨段路由。未执行图形登录、题目业务、AD账号/共享验收、第二套并行副本或从ready再次重置；本次generation4是正常平台reset从失败状态重建的冷实例，不宣称完整生命周期/长期运行已签收。

gen3写入失败已独立定位：模板520/DC同样原IP/DNS脚本在WMI适配器Put改名处COMException退出1，实际IP已写好；省略名字同样脚本退出0。模板522旧Windows诊断IP及原IP写入均退出0。因此只通过正常草稿Update/Validate/Publish/Reset省略两Windows的GuestInterfaceName，MAC/IP/DNS/网关/路由不变、Linux名字保留，无新平台部署。Windows显示名字不是网卡数量，Ethernet 2/本地连接3均只有一张当前业务卡。

两专用诊断runtime01a10252-7934-7d90-a11d-fad6e85e0a16、01a10258-055e-72f8-aab6-8d1209fb9b37均正常destroyed；.31最终API恢复true，final-platform/node31-restored.json通过。最终服务active，.27根盘约82GiB、大盘约81GiB可用。完整final-platform/result.json与platform/network-verification.json及节点恢复证明已取回；D:/Work/YINYU-Lab2-ReleaseV2-20261003/lab2-final-network-proof.json保存无凭据现场输出。

最终PVE实读pve/data436.66GiB、Data71.86%/Meta2.88%，约122.9GiB空闲，113running、118–121全部stopped。68.60%为仓库迁移完成时的数据点，后续写入后不能当作当前值；证据final-pve-capacity-and-source-state.json。

更新场景的首脚本曾错误读取draft.name（实际名称在definition.name），在发布前KeyError；已确认未产生release/reset才修正重试，原失败日志/启动回执（仅PID）保留，无凭据文件。候选准备/中间状态见下文，不能覆盖这里的最终现场结果。后续容量上报仍需单独修复：读取images实际所在文件系统，不能从GetPathRoot得到系统盘余量；本轮没有直接改数据库事实或降低资源额度绕过。

## 部署与中间状态历史

用户批准后.27已切换/opt/gzctf/releases/teamlab-nic-ready-cc6c5737-20261003/publish；实际与内置Agent SHA9b2142e9731d77495322e20a100432a728cf3d86f87dc67abebf85ed58a9b3e8，主站/Agent active、NRestarts0，首页/Config成功。主站fa97187c/前端4fccca67未更换，迁移头20261001145237_AddTeamLabManagedGuestNetwork不变。源码cc6c5737，源分支准备记录49451306已推，无main合并。

完整新备份/opt/gzctf/backups/teamlab-nic-ready-cc6c5737-20261003：gzctf.dump1613041012字节、guacamole.dump92146、files.tar.gz189116758。两个数据库dump完整内容可读，5项SHA通过；原release仍在，回退脚本保留。GitHub下载300秒仅12MB超时，使用PVE SSH管理中继46.1MiB/34.4秒实际传输，并验证解包SHA；发布预览制品tag teamlab-nic-ready-cc6c5737-20261003保留，不把GitHub下载记为成功。

第一次正常reset gen1→2未创建VM，在placement失败：single_network_capacity_exceeded。节点CPU/内存/slot均足够且本次新特征可用。只读源码/现场明确AgentCapabilityService.ReadAvailableVmImageStorage用Path.GetPathRoot得到/，上报根盘85797339136字节，差约100MB未到计划80GiB；实际bind-mounted images盘88470315008字节足够。此处是独立容量上报缺口，本次不另改代码。

仅本次新备份gzctf.dump迁到/srv/yinyu-data/image-authoring/lab2-release-v2-20261003/release-backup/gzctf.dump，原备份文件入口改为符号链接；逐文件SHA228d005831de0ed336a7e3bbfe149fa112d62e93c7d3d986e719b39dcfe9fe5ad及5项备份摘要再次通过，内容不变，旧其它备份保留。报告根盘87408648192、大盘86857261056字节，均超过80GiB。

随后正常reset gen2→3已进入deploying、四个新VM定义运行，真实网络验收尚在执行；证据startup-after-capacity-recovery，运行ID仍01a1022f-7e91-7ad3-8309-833af4771430。只看系统/网络，不看业务。脚本结束将恢复.31原true，成功后留四机运行供用户验收。

后文是候选准备时的记录，不能用其“未部署”覆盖上述新现场事实。

## 现场问题与范围

Lab2四机release-v2导入519–522完成，首次及一次正常重试均在guest-network-verify以guest_network_interface_missing失败。重试runtime01a1022f-7e91-7ad3-8309-833af4771430：按1秒间隔限定QGA网络读取，web01首次QGA/全部网卡30.8/30.8秒，oa01为43.6/43.6秒，dc01为82.2/82.2秒，Server2008R2为43.6/62.8秒。旧Windows执行链49.3秒已经结束，而实际网卡要62.8秒才出现；清理前四机网卡最终均可见。旧Windows独立DHCP启动的e1000e驱动Code0和取得地址也通过。

Agent当前只等guest-ping/guest-info返回，随后立即读WMI/IP快照；第一次缺MAC就失败。QGA回应不能证明Windows PnP/WMI网卡枚举已经完成。现场证据支持设备初始化时序缺口，不应要求用户每次重装QGA/驱动或手动配网。原返回主站的错误没有保留资产身份，未直接取得首次失败的逐资产响应，不把“第一轮只有哪台失败”当作独立已证实事实。

## 最小修正与步骤

延用codex/teamlab-managed-network、同一worktree。先记录现场→修正Agent等待→定向/单元门禁与linux-x64制品→请求本次部署确认→批准后备份/独立release/切换→正常重新启动同一四机release→真实IP/DNS/路由/限定通信验收。

仅TeamLabVmNetworkService第一次MAC读取改为有上限的只读轮询。沿用已有ReadyTimeout总预算3分钟，QGA等待、可选宿主探测和MAC就绪共同使用，未额外扩大HTTP计划期限。所有声明MAC各出现一次才写入，多卡缺一张就继续等；重复MAC立即失败；永久缺卡仍返回原错误码；取消继续传播，每次读前后仍验证VM原生身份。

不添加镜像自启动配网脚本、不更改网卡硬件、不改主站/前端/API/数据库。已正常销毁先前诊断和第一轮失败runtime；重试失败后Agent现场VM/实例盘清理，.31已恢复isSchedulable=true。最后失败runtime保留以便后续正常重置，原PVE/快照/模板/导出和存储回退副本保留。

## 验证与发布状态

新增延迟单卡（Linux/Windows）、双卡一张延迟、永久缺卡、重复MAC、等待取消及原生身份变化测试。首次定向81/82，失败源于测试助手把所有就绪期限设为100ms，正常测试并发调度超过期限；已改成普通测试5秒，只有永久缺卡用100ms，复核82/82通过（包括计划期限回归）。全量单元1282/1282通过，Agent linux-x64自包含发布通过，git diff --check通过；既有编译/依赖警告保留。完整solution构建、数据库集成及前端门禁未执行，改动仅Agent就绪等待，无DTO/迁移/前端变更；真实四机验收待部署。

源码cc6c5737afeb843971d1510bb1023b39d2b4f4b0。制品55,643,222字节，SHA2569b2142e9731d77495322e20a100432a728cf3d86f87dc67abebf85ed58a9b3e8；仓库外nic-readiness-release保存发布包、manifest、已通过bash -n的备份/独立release切换/回退脚本。拟目标仅.27，拟目录/opt/gzctf/releases/teamlab-nic-ready-cc6c5737-20261003/publish，拟备份/opt/gzctf/backups/teamlab-nic-ready-cc6c5737-20261003；这些是准备路径，尚未在服务器创建或执行。主站fa97187c、前端4fccca67继续复用。部署前现场再核验并完成新备份，不把已准备脚本写成已备份。

平台后续正常reset脚本reset-after-nic-fix.py已准备并py_compile通过，使用本次失败runtime01a1022f-7e91-7ad3-8309-833af4771430，预期generation1→2、沿用原地址、仅.27；成功后独立QGA网络回读和限定管理TCP/跨段隔离核验。此脚本尚未执行，失败runtime状态Failed，真实运行VM为0，.31true。没有为了绕过检查而把正式模板改成DHCP。

本修正未部署，服务器仍Agent8637e087；真实四机静态网络/访问/重置仍待验收，不以本地测试替代。部署前按用户明确要求确认。无数据库迁移，完整数据库集成和前端门禁是否执行在最终结果另列。

原始系统/网络证据及无凭据制作脚本保存在仓库外D:/Work/YINYU-Lab2-ReleaseV2-20261003。服务器证据startup-retry1/readiness-summary.json、boot-network-timeline.json及runtime.json。存储迁移事实见同日release-v2存储交接；教学笔记只本地提交，不推远端。
