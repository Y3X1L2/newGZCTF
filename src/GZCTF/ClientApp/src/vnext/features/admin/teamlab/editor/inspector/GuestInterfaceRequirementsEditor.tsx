import { Plus, Trash2 } from 'lucide-react'
import { useEffect, useState } from 'react'
import type { TeamLabGuestNetworkRequirements, TeamLabGuestRoute } from '../../api/teamlabContracts'
import type { TopologyMembershipConnection } from '../../model/topologyDocument'
import { SelectInput, TextInput } from './InspectorFields'
import styles from './TeamLabInspector.module.css'

export function GuestInterfaceRequirementsEditor({
  connection,
  onChange,
  readOnly,
  preconfigured,
  managedStatic,
}: {
  connection: TopologyMembershipConnection
  onChange: (patch: TeamLabGuestNetworkRequirements) => void
  readOnly?: boolean
  preconfigured: boolean
  managedStatic: boolean
}) {
  const [customDns, setCustomDns] = useState(false)
  useEffect(() => {
    if (connection.dnsServers == null) setCustomDns(false)
  }, [connection.dnsServers])
  const disabled = readOnly || preconfigured
  const dnsMode =
    customDns || (connection.dnsServers?.length ?? 0) > 0
      ? 'custom'
      : connection.dnsServers == null
        ? 'inherit'
        : 'none'
  const routes = connection.staticRoutes ?? []
  const incompatibleFixedSettings =
    !managedStatic && (connection.guestInterfaceName != null || routes.some((route) => route.metric != null))
  const updateRoute = (index: number, patch: Partial<TeamLabGuestRoute>) =>
    onChange({
      staticRoutes: routes.map((route, routeIndex) => (routeIndex === index ? { ...route, ...patch } : route)),
    })
  return (
    <details className={styles.networkDetails}>
      <summary>接口网络要求</summary>
      <div className={styles.sectionBody}>
        {preconfigured ? (
          <p className={styles.muted}>镜像预配置模式不会应用这些要求。请先切换网络方式，再按需移除或修改已有设置。</p>
        ) : null}
        {incompatibleFixedSettings ? (
          <>
            <p className={styles.muted}>
              已保存的接口名称或路由跃点数只适用于平台固定配置。切换方式保留了这些值，请明确清除后再发布。
            </p>
            <button
              className={styles.addButton}
              disabled={disabled}
              type="button"
              onClick={() =>
                onChange({
                  ...(connection.guestInterfaceName != null ? { guestInterfaceName: null } : {}),
                  ...(routes.some((route) => route.metric != null)
                    ? {
                        staticRoutes: routes.map((route) => ({
                          ...route,
                          ...(route.metric != null ? { metric: null } : {}),
                        })),
                      }
                    : {}),
                })
              }
            >
              清除固定配置专用设置
            </button>
          </>
        ) : null}
        <SelectInput
          disabled={disabled}
          label="默认网关"
          value={connection.useDefaultGateway == null ? 'inherit' : connection.useDefaultGateway ? 'use' : 'none'}
          onChange={(value) => onChange({ useDefaultGateway: value === 'inherit' ? null : value === 'use' })}
        >
          <option value="inherit">按主网卡决定</option>
          <option value="use">使用本网段网关</option>
          <option value="none">不使用默认网关</option>
        </SelectInput>
        <p className={styles.muted}>每个资产最多一张网卡使用默认网关；明确不使用时，主网卡标记也不会添加网关。</p>
        <SelectInput
          disabled={disabled}
          label="DNS 配置"
          value={dnsMode}
          onChange={(value) => {
            setCustomDns(value === 'custom')
            onChange({
              dnsServers: value === 'custom' ? (connection.dnsServers ?? []) : value === 'inherit' ? null : [],
            })
          }}
        >
          <option value="inherit">继承网段 DNS</option>
          <option value="none">不配置 DNS</option>
          <option value="custom">自定义 DNS</option>
        </SelectInput>
        {dnsMode === 'custom' ? (
          <TextInput
            disabled={disabled}
            label="DNS 服务器"
            value={(connection.dnsServers ?? []).join(', ')}
            hint="最多 3 个 IPv4 地址，用逗号分隔。留空表示不配置 DNS。"
            onChange={(value) => {
              const addresses = value.split(/[,，\s]+/).filter(Boolean)
              if (addresses.length > 3) return false
              onChange({ dnsServers: addresses })
            }}
          />
        ) : null}
        {managedStatic || connection.guestInterfaceName != null ? (
          <TextInput
            disabled={disabled || !managedStatic}
            label="来宾接口名称"
            value={connection.guestInterfaceName ?? ''}
            hint="可选。字母开头，最多 15 位，支持字母、数字、下划线和短横线。平台固定配置按本次网卡身份匹配。"
            onChange={(value) => {
              const name = value.trim()
              if (name && !/^[A-Za-z][A-Za-z0-9_-]{0,14}$/.test(name)) return false
              onChange({ guestInterfaceName: name || null })
            }}
          />
        ) : null}
        <strong className={styles.smallHeading}>静态路由</strong>
        <p className={styles.muted}>
          最多 8 条。目标使用 IPv4 CIDR，默认路由请通过上方网关设置；下一跳须位于本网卡子网。
        </p>
        {routes.map((route, index) => (
          <div className={styles.routeCard} key={index}>
            <div className={styles.inlineHeading}>
              <strong>路由 {index + 1}</strong>
              <button
                className={styles.iconButton}
                type="button"
                aria-label={`删除路由 ${index + 1}`}
                disabled={disabled}
                onClick={() => onChange({ staticRoutes: routes.filter((_, routeIndex) => routeIndex !== index) })}
              >
                <Trash2 size={15} aria-hidden="true" />
              </button>
            </div>
            <TextInput
              disabled={disabled}
              label={`路由 ${index + 1} 目标网段`}
              value={route.destinationCidr}
              onChange={(destinationCidr) => updateRoute(index, { destinationCidr: destinationCidr.trim() })}
            />
            <TextInput
              disabled={disabled}
              label={`路由 ${index + 1} 下一跳`}
              value={route.nextHop}
              onChange={(nextHop) => updateRoute(index, { nextHop: nextHop.trim() })}
            />
            {managedStatic || route.metric != null ? (
              <TextInput
                disabled={disabled || !managedStatic}
                label={`路由 ${index + 1} 跃点数`}
                value={route.metric ?? ''}
                type="number"
                min={1}
                max={9999}
                hint="仅平台固定配置可设置，范围 1–9999。"
                onChange={(value) => {
                  const metric = value.trim() ? Number(value) : null
                  if (metric !== null && (!Number.isInteger(metric) || metric < 1 || metric > 9999)) return false
                  updateRoute(index, { metric })
                }}
              />
            ) : null}
          </div>
        ))}
        <button
          className={styles.addButton}
          disabled={disabled || routes.length >= 8}
          type="button"
          onClick={() => onChange({ staticRoutes: [...routes, { destinationCidr: '', nextHop: '' }] })}
        >
          <Plus aria-hidden="true" size={15} />
          添加路由
        </button>
      </div>
    </details>
  )
}
