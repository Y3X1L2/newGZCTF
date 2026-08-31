export interface TeamLabFieldHelp {
  key: string
  title: string
  description: string
}

export const teamLabFieldHelp: Readonly<Record<string, TeamLabFieldHelp>> = {
  hostOffset: {
    key: 'hostOffset',
    title: '主机偏移',
    description:
      '作用：确定资产在所属网段内的 IPv4 地址末段，例如 10.10.0.10 中的 10。何时使用：为同一网段的资产固定地址时。如何操作：在同一交换机下填入不重复的数字，1 由网关保留。结果：平台按该偏移分配稳定地址。',
  },
  interfaceOrder: {
    key: 'interfaceOrder',
    title: '网卡顺序',
    description:
      '作用：确定资产内网卡的识别顺序，例如第一张网卡对应 eth0。何时使用：资产接入多个网段且启动配置依赖网卡顺序时。如何操作：从 0 开始排序，并保留一张主网卡。结果：主网卡获得默认网关，其余网卡只保留本网段通信。',
  },
  endpointObservation: {
    key: 'endpointObservation',
    title: '端点观测',
    description:
      '作用：决定是否采集该资产参与通信时的流量信息。何时使用：需要在运行后定位跨资产通信路径时。如何操作：普通资产选“可选”；必须留存观测证据的资产选“必需”；无需观测时选“禁用”。结果：必需模式在观测能力不可用时会阻断发布。',
  },
  healthChecks: {
    key: 'healthChecks',
    title: '健康检查',
    description:
      '作用：确认资产中的服务已经可访问，而不是只确认进程已启动。何时使用：入口服务、Web 服务或必须在依赖启动前就绪的资产。如何操作：开启后选择 TCP 或 HTTP 并填写端口。结果：检查通过后资产进入就绪；失败会记录明确原因，并按服务配置允许的重启次数处理。',
  },
  networkRegions: {
    key: 'networkRegions',
    title: '网络区域',
    description:
      '作用：把同一网段的交换机和资产集中显示，方便阅读大型拓扑。何时使用：编排多个网段、路由器或分片场景时。如何操作：点击区域查看网段属性，拖动区域整体移动成员，使用折叠按钮收起不需要查看的网段。结果：画布按网段清晰分组，自动排版会保留入口和路由关系。',
  },
}

export function teamLabFieldHelpOf(key: string): TeamLabFieldHelp | null {
  return teamLabFieldHelp[key] ?? null
}
