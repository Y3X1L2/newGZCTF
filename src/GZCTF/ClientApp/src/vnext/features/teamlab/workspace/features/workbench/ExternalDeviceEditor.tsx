import { Button, Checkbox, Drawer, Select, TextInput } from '@mantine/core'
import { useMutation } from '@tanstack/react-query'
import { Cable, Plus, Radar, RefreshCw, Router } from 'lucide-react'
import { useState } from 'react'
import { useWorkspaceApi, type Asset, type Network, type Schema } from '../../api/client'
import { Empty, ErrorMessage } from '../../foundation/Feedback'
import { randomUUID } from '../../foundation/id'
import styles from './ExternalDeviceEditor.module.css'
import { networkOptions } from './networking'

export function ExternalDeviceEditor({
  networks,
  assets,
  selectedNetwork,
  device,
  onSave,
  onConnectNetwork,
  onClose,
}: {
  networks: Network[]
  assets: Asset[]
  selectedNetwork?: string
  device?: Schema<'ExternalDevice'>
  onSave: (network: Network) => void
  onConnectNetwork: () => void
  onClose: () => void
}) {
  const external = networks.filter((network) => network.external)
  const [networkId, setNetworkId] = useState(selectedNetwork ?? external[0]?.id ?? '')
  const network = external.find((item) => item.id === networkId)
  const [name, setName] = useState(device?.name ?? '')
  return (
    <Drawer opened position="right" size={560} title={device ? '编辑外部设备' : '添加外部设备'} onClose={onClose}>
      {device && network ? (
        <form
          className={styles.form}
          onSubmit={(event) => {
            event.preventDefault()
            onSave({
              ...network,
              devices: network.devices?.map((item) => (item.id === device.id ? { ...item, name: name.trim() } : item)),
            })
          }}
        >
          <div className={styles.identity}>
            <Router size={24} />
            <div>
              <strong className="mono">{device.address}</strong>
              <span className="mono">{device.mac}</span>
            </div>
          </div>
          <TextInput label="设备名称" required value={name} onChange={(event) => setName(event.currentTarget.value)} />
          <div className={styles.network}>
            <Cable size={16} />
            {network.name}
          </div>
          <Button type="submit">保存设备</Button>
        </form>
      ) : (
        <div className={styles.form}>
          {external.length ? (
            <>
              <div className={styles.networkPicker}>
                <Select
                  label="现场网络"
                  value={networkId}
                  onChange={(value) => setNetworkId(value ?? '')}
                  allowDeselect={false}
                  data={networkOptions({ networks: external }).map(({ value, title }) => ({ value, label: title }))}
                />
                <Button variant="default" aria-label="接入其他现场网络" onClick={onConnectNetwork}>
                  <Plus size={16} />
                </Button>
              </div>
              {network && <Discovery key={network.id} network={network} assets={assets} onSave={onSave} />}
            </>
          ) : (
            <Empty
              icon={<Cable size={32} />}
              title="接入设备所在的网络"
              action="接入现场网络"
              onAction={onConnectNetwork}
            />
          )}
        </div>
      )}
    </Drawer>
  )
}

function Discovery({
  network,
  assets,
  onSave,
}: {
  network: Network
  assets: Asset[]
  onSave: (network: Network) => void
}) {
  const api = useWorkspaceApi()
  const [range, setRange] = useState(network.cidr)
  const [selected, setSelected] = useState(new Set<string>())
  const [names, setNames] = useState<Record<string, string>>({})
  const discovery = useMutation({
    mutationFn: () =>
      api.discoverExternal(network.external!.nodeId, {
        interface: network.external!.interface,
        vlan: network.external!.vlan,
        cidr: range,
      }),
  })
  const keyOf = (device: { address: string; mac: string }) => `${device.address}/${device.mac}`
  const registered = new Set(network.devices?.map(keyOf))
  const managed = new Set(
    assets.flatMap((asset) => asset.interfaces.filter((iface) => iface.networkId === network.id).map(keyOf))
  )
  const devices = (discovery.data ?? []).filter((item) => !managed.has(keyOf(item)))
  const add = () =>
    onSave({
      ...network,
      devices: [
        ...(network.devices ?? []),
        ...devices
          .filter((item) => selected.has(keyOf(item)) && !registered.has(keyOf(item)))
          .map((item) => ({
            ...item,
            id: randomUUID(),
            name: names[keyOf(item)]?.trim() || item.address,
          })),
      ],
    })
  return (
    <>
      <div className={styles.network}>
        <Cable size={16} />
        <span className="mono">{network.external!.interface}</span>
        {network.external!.vlan && <span>VLAN {network.external!.vlan}</span>}
      </div>
      <div className={styles.discoveryTools}>
        <TextInput label="发现范围" required value={range} onChange={(event) => setRange(event.currentTarget.value)} />
        <Button
          leftSection={discovery.data ? <RefreshCw size={15} /> : <Radar size={15} />}
          loading={discovery.isPending}
          disabled={!range.trim()}
          onClick={() => {
            setSelected(new Set())
            discovery.mutate()
          }}
        >
          {discovery.data ? '重新发现' : '发现设备'}
        </Button>
      </div>
      <ErrorMessage error={discovery.error} />
      {discovery.isPending ? (
        <div className={styles.scan} role="status">
          <Radar size={32} />
          <strong>正在发现设备</strong>
        </div>
      ) : discovery.isSuccess && !devices.length ? (
        <Empty icon={<Radar size={30} />} title="没有发现响应设备" />
      ) : !discovery.data ? (
        <div className={styles.scan}>
          <Router size={32} />
          <strong>发现设备并加入画布</strong>
        </div>
      ) : (
        <div className={styles.results} aria-label="发现的设备">
          <div className={styles.resultHeading}>
            <strong>{devices.length} 台设备</strong>
            <span>{selected.size} 台已选</span>
          </div>
          {devices.map((item) => {
            const key = keyOf(item),
              added = registered.has(key)
            return (
              <div className={styles.device} key={key} data-selected={selected.has(key)}>
                <Checkbox
                  aria-label={`选择 ${item.address}`}
                  disabled={added}
                  checked={added || selected.has(key)}
                  onChange={(event) => {
                    const checked = event.currentTarget.checked
                    setSelected((current) => {
                      const next = new Set(current)
                      if (checked) next.add(key)
                      else next.delete(key)
                      return next
                    })
                  }}
                />
                <Router size={20} />
                <div className={styles.deviceIdentity}>
                  <strong className="mono">{item.address}</strong>
                  <span className="mono">{item.mac}</span>
                  {!added && selected.has(key) && (
                    <TextInput
                      size="xs"
                      aria-label={`${item.address} 的设备名称`}
                      placeholder="设备名称"
                      value={names[key] ?? ''}
                      onChange={(event) => setNames({ ...names, [key]: event.currentTarget.value })}
                    />
                  )}
                </div>
                <span className={styles.deviceState}>{added ? '已加入' : '已响应'}</span>
              </div>
            )
          })}
        </div>
      )}
      <div className={styles.footer}>
        <Button fullWidth disabled={!selected.size || discovery.isPending} onClick={add}>
          加入画布{selected.size ? ` · ${selected.size}` : ''}
        </Button>
      </div>
    </>
  )
}
