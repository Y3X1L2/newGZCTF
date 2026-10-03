# TeamLab新网卡就绪等待：待部署修正

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
