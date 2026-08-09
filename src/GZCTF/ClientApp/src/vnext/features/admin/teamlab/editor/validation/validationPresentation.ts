import type { TeamLabValidationIssue } from '../../api'

const messages: Readonly<Record<string, string>> = {
  topology_schema_unsupported: '当前拓扑版本不受支持。',
  topology_schema_mismatch: '当前拓扑包含了不属于该版本的配置。',
  topology_name_invalid: '名称不能为空，且不能超过 128 个字符。',
  network_count_invalid: '场景至少需要 1 个网段，且网段数量不能超过 32 个。',
  asset_count_invalid: '场景至少需要 1 个资产，且资产数量不能超过 128 个。',
  entry_network_invalid: '场景必须且只能设置 1 个选手入口网段。',
  address_pool_invalid: '地址池必须填写有效的 IPv4 网段。',
  address_pool_not_private: '地址池必须使用 10、172.16 至 172.31 或 192.168 私有地址。',
  runtime_prefix_invalid: '运行时网段必须小于地址池范围，且不能小于 /29。',
  address_pool_overlap: '该地址池与其他网段重叠。',
  image_template_invalid: '请选择有效的镜像模板。',
  image_template_unavailable: '所选镜像模板尚未就绪或与资产类型不匹配。',
  image_template_digest_changed: '镜像模板内容已变更，请重新选择并保存模板。',
  asset_resources_invalid: '处理器、内存和存储容量必须大于 0。',
  scenario_bake_kind_invalid: '发布时预制当前仅支持虚拟机资产。',
  environment_key_invalid: '环境变量名称格式不正确。',
  bootstrap_reference_invalid: '服务注入配置的标识或版本无效。',
  bootstrap_parameter_invalid: '服务注入参数名称格式不正确。',
  managed_switch_network_invalid: '交换机必须关联且只能关联 1 个已存在的网段。',
  managed_switch_interfaces_invalid: '交换机不能配置路由接口。',
  managed_router_network_invalid: '路由器应通过网络接口连接网段。',
  managed_router_interfaces_invalid: '路由器必须连接至少 2 个不同网段。',
  connection_network_missing: '连线两端的网段必须存在。',
  connection_self_reference: '连线不能连接同一个网段。',
  connection_path_invalid: '网络连线必须指定 1 个路由器或已开启路由能力的资产。',
  connection_router_invalid: '连线指定的路由节点无效。',
  connection_router_not_attached: '路由节点必须同时连接该连线两端的网段。',
  interface_count_invalid: '每个节点至少需要 1 个网络接口，且不能超过 8 个。',
  primary_interface_invalid: '每个节点必须且只能设置 1 个主网络接口。',
  interface_network_duplicate: '同一个节点不能重复连接同一网段。',
  interface_network_missing: '网络接口关联的网段不存在。',
  interface_host_offset_reserved: '主机地址偏移超出可用范围或占用了平台保留地址。',
  topology_key_duplicate: '标识不能重复。',
  topology_key_invalid: '标识必须以小写字母开头，且只能包含小写字母、数字和连字符。',
  dependency_asset_missing: '启动依赖两端的资产必须存在。',
  dependency_self_reference: '资产不能依赖自身启动。',
  dependency_duplicate: '相同的启动依赖不能重复添加。',
  dependency_cycle: '启动依赖中存在循环，请调整依赖顺序。',
}

const fieldNames: Readonly<Record<string, string>> = {
  schemaVersion: '拓扑版本',
  name: '名称',
  key: '标识',
  addressPool: '地址池',
  poolCidr: '网段范围',
  runtimePrefixLength: '运行时掩码',
  imageTemplateId: '镜像模板',
  resources: '资源配置',
  bakeAtPublish: '发布时预制',
  environment: '环境变量',
  bootstrap: '服务注入',
  parameters: '注入参数',
  networkKey: '所属网段',
  interfaces: '网络接口',
  viaNodeKey: '路由节点',
  viaAssetKey: '路由资产',
  hostOffset: '主机地址偏移',
}

const collectionNames: Readonly<Record<string, string>> = {
  networks: '网段',
  assets: '资产',
  infrastructure: '基础设施',
  connections: '网络连线',
  dependencies: '启动依赖',
  nodes: '节点',
}

export function formatValidationMessage(issue: TeamLabValidationIssue) {
  return messages[issue.code] ?? '当前配置不符合发布要求，请检查对应配置。'
}

export function formatValidationPath(path: string) {
  if (!path) return '场景配置'
  return path
    .split('.')
    .map((segment) => {
      const indexed = /^(\w+)\[(\d+)]$/.exec(segment)
      if (indexed) {
        const [, collection, rawIndex] = indexed
        return `${collectionNames[collection] ?? fieldNames[collection] ?? '配置项'} ${Number(rawIndex) + 1}`
      }
      return collectionNames[segment] ?? fieldNames[segment] ?? segment
    })
    .join(' / ')
}
