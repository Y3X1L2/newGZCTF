import { ActionIcon, Badge, Button, Drawer, Menu, MultiSelect, SegmentedControl, TextInput } from '@mantine/core'
import { Check, ChevronDown, Copy, Download, MoreHorizontal, Plus, Trash2 } from 'lucide-react'
import { useState } from 'react'
import type { EnvironmentSpec } from '../../api/client'
import { ErrorMessage, Loading } from '../../foundation/Feedback'
import styles from './VPNDrawer.module.css'
import { networkOptions, networksOverlap } from './networking'
import type { useVPNAccess, VPNInput } from './useVPNAccess'

const states = {
  pending: { label: '等待配置', color: 'gray' },
  active: { label: '可用', color: 'green' },
  revoking: { label: '撤销中', color: 'yellow' },
  failed: { label: '失败', color: 'red' },
}

export function VPNDrawer({
  vpn,
  spec,
  busy,
  onClose,
}: {
  vpn: ReturnType<typeof useVPNAccess>
  spec: EnvironmentSpec
  busy: boolean
  onClose: () => void
}) {
  const accessible = new Set(spec.assets.flatMap((asset) => asset.interfaces.map((iface) => iface.networkId)))
  for (const network of spec.networks) {
    if (network.devices?.length) accessible.add(network.id)
  }
  const [creating, setCreating] = useState(false)
  const [name, setName] = useState('')
  const [networkIds, setNetworkIds] = useState<string[]>(
    spec.networks.filter((network) => accessible.has(network.id)).map((network) => network.id)
  )
  const [mode, setMode] = useState<VPNInput['mode']>('original')
  const selectedNetworks = spec.networks.filter(
    (network) => accessible.has(network.id) && networkIds.includes(network.id)
  )
  const selectedIds = selectedNetworks.map((network) => network.id)
  const overlapping = selectedNetworks.some((network, index) =>
    selectedNetworks.slice(index + 1).some((other) => networksOverlap(network, other))
  )
  const accessMode = overlapping ? 'translated' : mode
  const pending = busy || vpn.create.isPending || vpn.revoke.isPending
  const error = vpn.access.error ?? vpn.create.error ?? vpn.revoke.error ?? vpn.download.error ?? vpn.copy.error
  const options = networkOptions(spec)
  const networkName = (id: string) => options.find((item) => item.value === id)?.title

  return (
    <Drawer opened onClose={onClose} title="VPN" position="right" size="md">
      <div className={styles.body}>
        <ErrorMessage error={error} />
        {vpn.access.isLoading ? (
          <Loading />
        ) : (
          vpn.access.data?.map((item) => (
            <section key={item.id} className={styles.connection}>
              <div className={styles.heading}>
                <strong>{item.name}</strong>
                <Badge variant="light" color={states[item.state].color}>
                  {states[item.state].label}
                </Badge>
                <Menu position="bottom-end">
                  <Menu.Target>
                    <ActionIcon variant="subtle" color="gray" aria-label={`${item.name} 操作`}>
                      <MoreHorizontal size={18} />
                    </ActionIcon>
                  </Menu.Target>
                  <Menu.Dropdown>
                    {item.state === 'active' && (
                      <Menu.Item
                        leftSection={
                          vpn.copy.isSuccess && vpn.copy.variables?.id === item.id ? (
                            <Check size={15} />
                          ) : (
                            <Copy size={15} />
                          )
                        }
                        onClick={() => vpn.copy.mutate(item)}
                      >
                        复制连接参数
                      </Menu.Item>
                    )}
                    <Menu.Item
                      color="red"
                      leftSection={<Trash2 size={15} />}
                      disabled={pending || item.state === 'revoking'}
                      onClick={() => vpn.revoke.mutate(item)}
                    >
                      撤销连接
                    </Menu.Item>
                  </Menu.Dropdown>
                </Menu>
              </div>
              <details className={styles.routes}>
                <summary>
                  <span>{[...new Set(item.routes.map((route) => networkName(route.networkId)))].join(' · ')}</span>
                  <ChevronDown size={15} />
                </summary>
                <div className={styles.routeList}>
                  {item.routes.map((route) => (
                    <div key={`${route.networkId}:${route.cidr}`} className={styles.route}>
                      <span>{networkName(route.networkId)}</span>
                      <code>
                        {route.cidr === route.accessCidr ? route.cidr : `${route.cidr} → ${route.accessCidr}`}
                      </code>
                    </div>
                  ))}
                </div>
              </details>
              {item.error && <div className={styles.error}>{item.error}</div>}
              {vpn.canDownload(item) && (
                <Button
                  size="xs"
                  variant="light"
                  leftSection={<Download size={14} />}
                  loading={vpn.download.isPending && vpn.download.variables?.id === item.id}
                  onClick={() => vpn.download.mutate(item)}
                >
                  下载 WireGuard 配置
                </Button>
              )}
            </section>
          ))
        )}
        {!creating && (
          <Button
            variant="default"
            leftSection={<Plus size={15} />}
            disabled={pending || !accessible.size}
            onClick={() => setCreating(true)}
          >
            新建连接
          </Button>
        )}
        {creating && (
          <form
            className={styles.form}
            onSubmit={(event) => {
              event.preventDefault()
              vpn.create.mutate(
                {
                  name: name.trim(),
                  networkIds: selectedIds,
                  mode: accessMode,
                },
                {
                  onSuccess: () => {
                    setCreating(false)
                    setName('')
                  },
                }
              )
            }}
          >
            <TextInput
              label="连接名称"
              value={name}
              onChange={(event) => setName(event.currentTarget.value)}
              required
              autoFocus
            />
            <MultiSelect
              label="授权网段"
              value={selectedIds}
              onChange={setNetworkIds}
              data={options.filter((option) => accessible.has(option.value))}
              required
            />
            <SegmentedControl
              aria-label="地址模式"
              value={accessMode}
              onChange={(value) => setMode(value as VPNInput['mode'])}
              data={[
                { label: '原地址', value: 'original', disabled: overlapping },
                { label: '独立访问地址', value: 'translated' },
              ]}
            />
            <div className={styles.actions}>
              <Button variant="default" onClick={() => setCreating(false)}>
                取消
              </Button>
              <Button
                type="submit"
                loading={vpn.create.isPending}
                disabled={pending || !name.trim() || !selectedIds.length}
              >
                创建连接
              </Button>
            </div>
          </form>
        )}
        {!vpn.access.isLoading && !vpn.access.data?.length && !creating && (
          <div className={styles.empty}>暂无 VPN 连接</div>
        )}
      </div>
    </Drawer>
  )
}
