import type { TeamLabVmNetworkMode } from '../../api/teamlabContracts'
import { SelectInput } from './InspectorFields'
import styles from './TeamLabInspector.module.css'

export const networkModeLabels: Record<TeamLabVmNetworkMode, string> = {
  dhcp: 'DHCP 自动获取',
  'managed-static': '平台固定配置',
  preconfigured: '镜像预配置（待迁移）',
}

export function GuestNetworkModeEditor({
  mode,
  inheritedMode,
  onChange,
  readOnly,
  windows,
}: {
  mode?: TeamLabVmNetworkMode | null
  inheritedMode?: TeamLabVmNetworkMode | null
  onChange: (mode: TeamLabVmNetworkMode | null) => void
  readOnly?: boolean
  windows: boolean
}) {
  const effective = mode ?? inheritedMode
  return (
    <>
      <SelectInput
        disabled={readOnly}
        label="网络配置方式"
        onChange={(value) => onChange(value ? (value as TeamLabVmNetworkMode) : null)}
        value={mode ?? ''}
      >
        <option value="">继承镜像默认</option>
        <option value="dhcp">DHCP 自动获取</option>
        <option value="managed-static">平台固定配置</option>
        {mode === 'preconfigured' ? (
          <option disabled value="preconfigured">
            镜像预配置（待迁移）
          </option>
        ) : null}
      </SelectInput>
      <p className={styles.muted}>
        {mode == null ? `镜像默认：${inheritedMode ? networkModeLabels[inheritedMode] : '尚未返回，发布时校验'}。` : ''}
        {effective === 'preconfigured'
          ? '当前由镜像自行维护网络，平台不自动应用接口要求。可明确改为 DHCP 或平台固定配置；切换不会自动清除已保存的接口要求。'
          : effective === 'managed-static'
            ? windows
              ? '平台按本次网卡配置并验证固定地址。镜像须安装并运行 QEMU Guest Agent（QGA），并具备兼容网卡驱动。'
              : '平台按本次网卡配置并验证固定地址。镜像须运行 QGA，并支持 cloud-init / Netplan 和 NoCloud 配置盘；生成配置不代表来宾已应用。'
            : effective === 'dhcp'
              ? '由来宾 DHCP 客户端获取地址；请确保镜像已启用 DHCP 和兼容网卡驱动。'
              : '可明确选择 DHCP 或平台固定配置。镜像默认和所需组件将在发布时校验。'}
      </p>
    </>
  )
}
