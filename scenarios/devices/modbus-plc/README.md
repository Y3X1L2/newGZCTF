# Modbus TCP 训练设备

这是可构建的有状态 Modbus 设备包源代码，使用固定版本 pymodbus 处理实际协议。它模拟寄存器和线圈，不模拟水箱、温度、泵或电机的物理过程，不代表 IEC 104、S7 或 OPC UA 已实现。

## 行为

- 保持寄存器初始值可配置，支持读、单写和多写，后续连接读取实际写入状态。
- 线圈支持读写；离散输入和输入寄存器提供 32 个只读零值。
- 地址使用报文中的 **零起始偏移**。SCADA 文档中的 40001 对应这里的偏移 0，不将 40001 直接填入报文地址。
- 只响应配置的 Unit ID；越界地址和非法数量返回 Modbus 异常。
- 每个容器独立保存内存状态。停止/重启后回到初始值，不承诺持久化或快照恢复。
- 默认监听 TCP 1502，SCADA 客户端必须配置这个端口；不能只按标准端口 502 筛选流量。

## 构建及平台接入

```sh
docker build -t yinyu/modbus-plc:1.0.0 scenarios/devices/modbus-plc
```

通过既有镜像导入/Registry 流程登记真实制品摘要，再登记设备包并绑定同摘要 Docker 镜像；不能手填虚构摘要。建议最低 1 CPU / 64 MiB，协议服务端口为 TCP 1502；健康与计数端口为 HTTP 1503。参数 schema 使用本目录 `parameters.schema.json`。schema 固定协议端口，避免配置与实际监听不一致。

场景参数通过已接入的 `GZCTF_DEVICE_PARAMETERS` 传入，例如：

```json
{"unitId":7,"holdingRegisters":[12,34,56,78]}
```

SCADA 客户端连接资产的场景 IP、1502 端口、Unit ID 7，读取偏移 0 的两个保持寄存器应得到 12、34。写入偏移 0 后再次读取应得到新值；另一实例应保留自己的初始值。初始寄存器数组长度就是可访问范围，越界不能静默补零。

设备包健康声明配置为 `{"kind":"http","port":1503,"path":"/health","intervalSeconds":30}`，协议事件类型为 `["modbus.read","modbus.write"]`。HTTP 健康端点与 Modbus 服务共享事件循环，返回每次进程启动的 `bootId` 和成功处理的请求累计计数 `counters`，不返回寄存器值。平台后台通过 Agent 读取端点，保存健康及计数，在“事件与日志”记录新增次数；失败请求和健康检查自身不计作 Modbus 操作。

这是按采样周期汇总的协议活动，不是每条报文的完整事件记录。进程重启由 bootId 区分，主站重启从数据库计数继续；进程在两次采样之间退出时，尚未采到的操作可能丢失。需要逐报文取证时使用平台抓包。TCP 健康仅证明监听可达；要启用自动协议活动采集，必须提供上述 HTTP 计数契约。

## 可重复验证

```powershell
dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj -c Release --filter FullyQualifiedName~TeamLabModbusDeviceTests
```

测试使用独立原始 TCP 客户端校验 MBAP 头、分片请求、寄存器读写、线圈读写、异常响应、双实例隔离和重启语义，不以服务器自身日志或 TCP 端口存在代替正确性。
