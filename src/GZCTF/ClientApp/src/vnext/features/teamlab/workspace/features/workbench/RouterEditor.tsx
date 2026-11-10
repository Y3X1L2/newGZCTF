import { ActionIcon, Button, Drawer, MultiSelect, Select, TextInput } from '@mantine/core'
import { ArrowRight, ChevronDown, Network, Plus, Trash2 } from 'lucide-react'
import { useState } from 'react'
import type { EnvironmentSpec, Schema } from '../../api/client'
import { randomUUID } from '../../foundation/id'
import styles from './RouterEditor.module.css'
import { networkOptions } from './networking'

type Router = Schema<'LogicalRouter'>
type Route = Schema<'Route'>

export function RouterEditor({
  router,
  spec,
  onSave,
  onClose,
}: {
  router?: Router
  spec: EnvironmentSpec
  onSave: (router: Router, routes: Route[]) => void
  onClose: () => void
}) {
  const [id] = useState(() => router?.id ?? randomUUID())
  const [name, setName] = useState(router?.name ?? `路由器 ${(spec.routers?.length ?? 0) + 1}`)
  const [interfaces, setInterfaces] = useState(router?.interfaces ?? [])
  const [routes, setRoutes] = useState(() => (spec.routes ?? []).filter((route) => route.routerId === id))
  const availableNetworks = networkOptions(spec)
  const connectedNetworks = availableNetworks.filter((network) =>
    interfaces.some((iface) => iface.networkId === network.value)
  )
  const updateRoute = (index: number, values: Partial<Route>) =>
    setRoutes((current) => current.map((item, i) => (i === index ? { ...item, ...values } : item)))
  return (
    <Drawer opened onClose={onClose} title={router ? '编辑路由器' : '添加路由器'} position="right" size={540}>
      <form
        className={styles.form}
        onSubmit={(event) => {
          event.preventDefault()
          onSave(
            { id, name: name.trim(), interfaces },
            routes.map((route) => ({ ...route, routerId: id }))
          )
        }}
      >
        <TextInput label="路由器名称" value={name} required onChange={(event) => setName(event.currentTarget.value)} />
        <MultiSelect
          label="连接网段"
          placeholder="选择网段"
          data={availableNetworks}
          value={interfaces.map((iface) => iface.networkId)}
          searchable
          onChange={(networkIds) => {
            setInterfaces((current) =>
              networkIds.map(
                (networkId) =>
                  current.find((iface) => iface.networkId === networkId) ?? {
                    id: randomUUID(),
                    networkId,
                    address: '',
                  }
              )
            )
            setRoutes((current) => current.filter((route) => networkIds.includes(route.networkId)))
          }}
        />
        {interfaces.length > 0 && (
          <section className={styles.interfaces} aria-label="路由器接口">
            {interfaces.map((iface, index) => (
              <div className={styles.interface} key={iface.id}>
                <span className={styles.network}>
                  <Network size={16} />
                  <strong>{availableNetworks.find((network) => network.value === iface.networkId)?.title}</strong>
                </span>
                <TextInput
                  aria-label={`接口 ${index + 1} 地址`}
                  placeholder="自动分配"
                  value={iface.address}
                  classNames={{ input: styles.address }}
                  onChange={(event) => {
                    const address = event.currentTarget.value
                    setInterfaces((current) =>
                      current.map((item) => (item.id === iface.id ? { ...item, address } : item))
                    )
                  }}
                />
              </div>
            ))}
          </section>
        )}
        <details className={styles.routes} open={routes.length > 0 || undefined}>
          <summary>
            <span>
              静态路由 <b>{routes.length}</b>
            </span>
            <ChevronDown size={16} />
          </summary>
          <div className={styles.routeList}>
            {routes.map((route, index) => (
              <div className={styles.route} key={index}>
                <div className={styles.routeHeading}>
                  <span>
                    <ArrowRight size={15} />
                    路由 {index + 1}
                  </span>
                  <ActionIcon
                    variant="subtle"
                    color="red"
                    aria-label={`移除路由 ${index + 1}`}
                    onClick={() => setRoutes((current) => current.filter((_, i) => i !== index))}
                  >
                    <Trash2 size={15} />
                  </ActionIcon>
                </div>
                <TextInput
                  label="目标网段"
                  placeholder="0.0.0.0/0"
                  value={route.destination}
                  required
                  classNames={{ input: styles.address }}
                  onChange={(event) => {
                    const destination = event.currentTarget.value
                    updateRoute(index, { destination })
                  }}
                />
                <div className={styles.columns}>
                  <TextInput
                    label="下一跳"
                    placeholder="网关地址"
                    value={route.nextHop}
                    required
                    classNames={{ input: styles.address }}
                    onChange={(event) => {
                      const nextHop = event.currentTarget.value
                      updateRoute(index, { nextHop })
                    }}
                  />
                  <Select
                    label="出口网段"
                    data={connectedNetworks}
                    value={route.networkId}
                    required
                    allowDeselect={false}
                    onChange={(networkId) => updateRoute(index, { networkId: networkId! })}
                  />
                </div>
              </div>
            ))}
            <Button
              variant="default"
              size="compact-sm"
              leftSection={<Plus size={14} />}
              disabled={!interfaces.length}
              onClick={() =>
                setRoutes((current) => [
                  ...current,
                  {
                    routerId: id,
                    networkId: interfaces[0].networkId,
                    destination: '',
                    nextHop: '',
                  },
                ])
              }
            >
              添加路由
            </Button>
          </div>
        </details>
        <div className={styles.footer}>
          <Button type="submit" fullWidth>
            {router ? '更新路由器' : '添加到环境'}
          </Button>
        </div>
      </form>
    </Drawer>
  )
}
