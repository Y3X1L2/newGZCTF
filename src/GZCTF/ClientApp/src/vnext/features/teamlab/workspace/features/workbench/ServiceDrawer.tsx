import { ActionIcon, Button, CopyButton, Drawer, Menu, NumberInput, SegmentedControl, Select } from '@mantine/core'
import { Check, Copy, MoreHorizontal, Plus, Trash2 } from 'lucide-react'
import { useState } from 'react'
import type { Asset, EnvironmentSpec, Schema } from '../../api/client'
import { ErrorMessage, Loading } from '../../foundation/Feedback'
import styles from './ServiceDrawer.module.css'
import { networkOptions } from './networking'

type ServiceInput = Omit<Schema<'CreateService'>, 'expectedRevision' | 'clientRequestId'>

export function ServiceDrawer({
  asset,
  spec,
  services,
  loading,
  pending,
  canManage,
  error,
  onCreate,
  onRevoke,
  onClose,
}: {
  asset: Asset
  spec: EnvironmentSpec
  services: Schema<'ServiceEndpoint'>[]
  loading: boolean
  pending: boolean
  canManage: boolean
  error: Error | null
  onCreate: (input: ServiceInput, onAccepted: () => void) => void
  onRevoke: (id: string) => void
  onClose: () => void
}) {
  const [creating, setCreating] = useState(false)
  const [protocol, setProtocol] = useState<ServiceInput['protocol']>('tcp')
  const [targetPort, setTargetPort] = useState<string | number>('')
  const [listenPort, setListenPort] = useState<string | number>('')
  const [interfaceId, setInterfaceId] = useState<string | undefined>(
    asset.interfaces.find((item) => item.primary)?.id ?? asset.interfaces[0]?.id
  )
  const endpoints = services.filter((item) => item.assetId === asset.id)
  const networks = new Map(networkOptions(spec).map((network) => [network.value, network.title]))

  return (
    <Drawer opened onClose={onClose} title={`${asset.name} · 服务`} position="right" size="md">
      <div className={styles.body}>
        {error && <ErrorMessage error={error} />}
        {loading ? (
          <Loading />
        ) : (
          endpoints.map((item) => {
            const address = `${item.address.includes(':') ? `[${item.address}]` : item.address}:${item.port}`
            return (
              <div key={item.id} className={styles.endpoint}>
                <div>
                  <span className={styles.protocol}>
                    {item.protocol.toUpperCase()} <strong>{item.targetPort}</strong>
                  </span>
                  <code>{address}</code>
                </div>
                <CopyButton value={address}>
                  {({ copied, copy }) => (
                    <ActionIcon
                      variant="subtle"
                      color={copied ? 'green' : 'gray'}
                      aria-label="复制服务地址"
                      onClick={copy}
                    >
                      {copied ? <Check size={16} /> : <Copy size={16} />}
                    </ActionIcon>
                  )}
                </CopyButton>
                {canManage && (
                  <Menu position="bottom-end">
                    <Menu.Target>
                      <ActionIcon
                        variant="subtle"
                        color="gray"
                        aria-label={`服务 ${item.protocol}/${item.targetPort} 操作`}
                      >
                        <MoreHorizontal size={18} />
                      </ActionIcon>
                    </Menu.Target>
                    <Menu.Dropdown>
                      <Menu.Item
                        color="red"
                        leftSection={<Trash2 size={15} />}
                        disabled={pending}
                        onClick={() => onRevoke(item.id)}
                      >
                        撤销入口
                      </Menu.Item>
                    </Menu.Dropdown>
                  </Menu>
                )}
              </div>
            )
          })
        )}
        {canManage && !creating && (
          <Button
            variant="default"
            leftSection={<Plus size={15} />}
            disabled={pending || !asset.interfaces.length}
            onClick={() => setCreating(true)}
          >
            开放服务
          </Button>
        )}
        {creating && (
          <form
            className={styles.form}
            onSubmit={(event) => {
              event.preventDefault()
              if (typeof targetPort !== 'number' || !interfaceId) return
              onCreate(
                {
                  protocol,
                  targetPort,
                  interfaceId,
                  ...(typeof listenPort === 'number' ? { listenPort } : {}),
                },
                () => {
                  setCreating(false)
                  setTargetPort('')
                  setListenPort('')
                }
              )
            }}
          >
            <SegmentedControl
              aria-label="服务协议"
              value={protocol}
              onChange={(value) => setProtocol(value as ServiceInput['protocol'])}
              data={[
                { label: 'TCP', value: 'tcp' },
                { label: 'UDP', value: 'udp' },
              ]}
            />
            <NumberInput
              label="资产端口"
              value={targetPort}
              onChange={setTargetPort}
              min={1}
              max={65535}
              allowDecimal={false}
              hideControls
              required
            />
            <details className={styles.options}>
              <summary>入口设置</summary>
              <div className={styles.form}>
                {asset.interfaces.length > 1 && (
                  <Select
                    label="网络接口"
                    value={interfaceId}
                    onChange={(value) => setInterfaceId(value ?? undefined)}
                    data={asset.interfaces.map((item) => ({
                      value: item.id,
                      label: `${networks.get(item.networkId)} · ${item.address}`,
                    }))}
                    allowDeselect={false}
                    required
                  />
                )}
                <NumberInput
                  label="入口端口"
                  placeholder="自动分配"
                  value={listenPort}
                  onChange={setListenPort}
                  min={1}
                  max={65535}
                  allowDecimal={false}
                  hideControls
                />
              </div>
            </details>
            <div className={styles.actions}>
              <Button variant="default" onClick={() => setCreating(false)}>
                取消
              </Button>
              <Button type="submit" loading={pending} disabled={!interfaceId || typeof targetPort !== 'number'}>
                开放服务
              </Button>
            </div>
          </form>
        )}
        {!loading && !endpoints.length && !creating && <div className={styles.empty}>暂无服务入口</div>}
      </div>
    </Drawer>
  )
}
