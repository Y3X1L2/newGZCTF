import { Button, FocusTrap, Popover, ScrollArea, SegmentedControl, TextInput } from '@mantine/core'
import { Box, ChevronDown, LocateFixed, Monitor, Network, Router, Search } from 'lucide-react'
import { useState } from 'react'
import type { EnvironmentSpec, Template } from '../../api/client'
import { Status } from '../../foundation/Status'
import styles from './ObjectLocator.module.css'

type Props = {
  spec: EnvironmentSpec
  templates: Template[]
  states: ReadonlyMap<string, string>
  selection?: string
  onSelect: (id: string) => void
}

/** Uses the workbench's current spec, including unsaved edits; no second inventory or request. */
export function ObjectLocator({ spec, templates, states, selection, onSelect }: Props) {
  const [opened, setOpened] = useState(false)
  const [query, setQuery] = useState('')
  const [kind, setKind] = useState('all')
  const networks = new Map(spec.networks.map((network) => [network.id, network]))
  const templateKinds = new Map(templates.map((template) => [template.id, template.kind]))
  const objects = [
    ...spec.assets.map((asset) => ({
      id: asset.id,
      name: asset.name,
      kind: 'assets',
      icon: templateKinds.get(asset.templateId) === 'vm' ? Monitor : Box,
      addresses: asset.interfaces
        .map((iface) => iface.address)
        .filter(Boolean)
        .join(' · '),
      networkIds: asset.interfaces.map((iface) => iface.networkId),
      state: states.get(asset.id) ?? 'draft',
    })),
    ...(spec.routers ?? []).map((router) => ({
      id: router.id,
      name: router.name,
      kind: 'networks',
      icon: Router,
      addresses:
        router.interfaces
          .map((iface) => iface.address)
          .filter(Boolean)
          .join(' · ') || '路由器',
      networkIds: router.interfaces.map((iface) => iface.networkId),
      state: undefined,
    })),
    ...spec.networks.flatMap((network) => [
      {
        id: network.id,
        name: network.name,
        kind: 'networks',
        icon: Network,
        addresses: network.cidr,
        networkIds: [network.id],
        state: undefined,
      },
      ...(network.devices ?? []).map((device) => ({
        id: device.id,
        name: device.name,
        kind: 'assets',
        icon: Router,
        addresses: device.address || '外部设备',
        networkIds: [network.id],
        state: undefined,
      })),
    ]),
  ]
  const search = query.trim().toLocaleLowerCase()
  const groups = new Map<string, typeof objects>()
  for (const object of objects) {
    const connected = object.networkIds.map((id) => networks.get(id)).filter((network) => network !== undefined)
    const searchable = [object.name, object.addresses, ...connected.map((network) => `${network.name} ${network.cidr}`)]
    if (kind !== 'all' && kind !== object.kind) continue
    if (!searchable.join(' ').toLocaleLowerCase().includes(search)) continue
    // Multi-interface objects appear once; every connected network remains searchable.
    const groupId = connected[0]?.id ?? ''
    const group = groups.get(groupId) ?? []
    group.push(object)
    groups.set(groupId, group)
  }
  const changeOpened = (value: boolean) => {
    setOpened(value)
    if (value) {
      setQuery('')
      setKind('all')
    }
  }
  return (
    <Popover
      opened={opened}
      onChange={changeOpened}
      position="bottom-end"
      width={360}
      middlewares={{ flip: true, shift: { padding: 12, crossAxis: true }, size: true }}
      trapFocus
      returnFocus
    >
      <Popover.Target>
        <Button
          variant="default"
          size="xs"
          leftSection={<LocateFixed size={15} />}
          rightSection={<ChevronDown size={13} />}
          aria-expanded={opened}
          aria-haspopup="dialog"
          onClick={() => changeOpened(!opened)}
        >
          定位对象
        </Button>
      </Popover.Target>
      <Popover.Dropdown className={styles.dropdown} role="dialog" aria-label="定位对象">
        <FocusTrap.InitialFocus />
        <TextInput
          aria-label="筛选名称或 IP"
          placeholder="筛选名称或 IP"
          leftSection={<Search size={15} />}
          value={query}
          onChange={(event) => setQuery(event.currentTarget.value)}
        />
        <SegmentedControl
          aria-label="定位对象类型"
          value={kind}
          onChange={setKind}
          data={[
            { value: 'all', label: '全部' },
            { value: 'assets', label: '资产' },
            { value: 'networks', label: '网段' },
          ]}
        />
        <ScrollArea.Autosize className={styles.results} type="never" scrollbars="y" overscrollBehavior="contain">
          {[...groups].map(([id, items]) => (
            <section key={id} aria-label={networks.get(id)?.name ?? '未连接网段'}>
              <h3>
                {networks.get(id)?.name ?? '未连接网段'}
                <span>{items.length}</span>
              </h3>
              {items.map(({ id, name, icon: Icon, addresses, state }) => (
                <button
                  key={id}
                  className={styles.object}
                  aria-current={selection === id}
                  onClick={() => {
                    setOpened(false)
                    onSelect(id)
                  }}
                >
                  <Icon size={17} />
                  <span className={styles.identity}>
                    <strong>{name}</strong>
                    <span>{addresses || '未分配地址'}</span>
                  </span>
                  {state && <Status value={state} />}
                </button>
              ))}
            </section>
          ))}
          {!groups.size && <p className={styles.empty}>没有匹配的对象</p>}
        </ScrollArea.Autosize>
      </Popover.Dropdown>
    </Popover>
  )
}
