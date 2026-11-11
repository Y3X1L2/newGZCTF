import { Network, Router } from 'lucide-react'
import type { Environment } from '../../api/client'
import styles from './WorkbenchPage.module.css'

export function NetworkList({
  spec,
  query,
  selection,
  onSelect,
}: {
  spec: Environment['spec']
  query: string
  selection?: string
  onSelect: (id: string) => void
}) {
  const rows = [
    ...spec.networks.map((network) => ({
      id: network.id,
      name: network.name,
      kind: network.external ? '外部网段' : '网段',
      address: network.cidr,
      connected: [
        ...spec.assets
          .filter((asset) => asset.interfaces.some((iface) => iface.networkId === network.id))
          .map((asset) => asset.name),
        ...(network.devices ?? []).map((device) => device.name),
        ...(spec.routers ?? [])
          .filter((router) => router.interfaces.some((iface) => iface.networkId === network.id))
          .map((router) => router.name),
      ].join('、'),
      icon: Network,
    })),
    ...(spec.routers ?? []).map((router) => ({
      id: router.id,
      name: router.name,
      kind: '路由器',
      address: router.interfaces
        .map((iface) => iface.address)
        .filter(Boolean)
        .join('、'),
      connected: router.interfaces
        .map((iface) => spec.networks.find((network) => network.id === iface.networkId)?.name)
        .filter(Boolean)
        .join('、'),
      icon: Router,
    })),
  ].filter((row) =>
    `${row.name} ${row.address} ${row.connected}`.toLocaleLowerCase().includes(query.toLocaleLowerCase())
  )
  return (
    <div className={styles.tableScroll}>
      <table className={styles.assetTable} aria-label="网段与路由器">
        <thead>
          <tr>
            <th>名称</th>
            <th>类型</th>
            <th>地址</th>
            <th>连接对象</th>
          </tr>
        </thead>
        <tbody>
          {rows.map((row) => (
            <tr key={row.id} aria-selected={selection === row.id} onClick={() => onSelect(row.id)}>
              <td>
                <button className={styles.assetName} onClick={() => onSelect(row.id)}>
                  <row.icon size={17} />
                  {row.name}
                </button>
              </td>
              <td>{row.kind}</td>
              <td>{row.address || '—'}</td>
              <td>{row.connected || '—'}</td>
            </tr>
          ))}
        </tbody>
      </table>
      {!rows.length && <div className={styles.empty}>没有匹配的网段或路由器</div>}
    </div>
  )
}
