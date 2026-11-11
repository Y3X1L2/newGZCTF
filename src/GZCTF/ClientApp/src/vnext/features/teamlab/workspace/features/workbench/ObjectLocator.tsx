import { Button, Combobox, ScrollArea, useCombobox } from '@mantine/core'
import { useHotkeys, useReducedMotion } from '@mantine/hooks'
import { Box, ChevronDown, LocateFixed, Monitor, Network, Router, Search } from 'lucide-react'
import { useState } from 'react'
import type { EnvironmentSpec, Template } from '../../api/client'
import styles from './ObjectLocator.module.css'

type Props = {
  spec: EnvironmentSpec
  templates: Template[]
  onSelect: (id: string) => void
}

/** Uses the workbench's current spec, including unsaved edits; no second inventory or request. */
export function ObjectLocator({ spec, templates, onSelect }: Props) {
  const [query, setQuery] = useState('')
  const [recent, setRecent] = useState<string[]>([])
  const reducedMotion = useReducedMotion()
  const combobox = useCombobox({
    onDropdownOpen: () => {
      setQuery('')
      combobox.focusSearchInput()
    },
    onDropdownClose: () => combobox.resetSelectedOption(),
  })
  useHotkeys([['mod+k', () => combobox.toggleDropdown()]])
  const networks = new Map(spec.networks.map((network) => [network.id, network]))
  const templateKinds = new Map(templates.map((template) => [template.id, template.kind]))
  const objects = [
    ...spec.assets.map((asset) => ({
      id: asset.id,
      name: asset.name,
      icon: templateKinds.get(asset.templateId) === 'vm' ? Monitor : Box,
      addresses: asset.interfaces
        .map((iface) => iface.address)
        .filter(Boolean)
        .join(' · '),
      networkIds: asset.interfaces.map((iface) => iface.networkId),
    })),
    ...(spec.routers ?? []).map((router) => ({
      id: router.id,
      name: router.name,
      icon: Router,
      addresses:
        router.interfaces
          .map((iface) => iface.address)
          .filter(Boolean)
          .join(' · ') || '路由器',
      networkIds: router.interfaces.map((iface) => iface.networkId),
    })),
    ...spec.networks.flatMap((network) => [
      {
        id: network.id,
        name: network.name,
        icon: Network,
        addresses: network.cidr,
        networkIds: [network.id],
      },
      ...(network.devices ?? []).map((device) => ({
        id: device.id,
        name: device.name,
        icon: Router,
        addresses: device.address || '外部设备',
        networkIds: [network.id],
      })),
    ]),
  ]
  const search = query.trim().toLocaleLowerCase()
  const results = objects.map((object) => ({
    ...object,
    network: object.networkIds
      .filter((id) => id !== object.id)
      .map((id) => networks.get(id)?.name)
      .filter(Boolean)
      .join(' · '),
    keywords: object.networkIds.map((id) => networks.get(id)?.cidr).join(' '),
  }))
  const matches = search
    ? results.filter((item) =>
        `${item.name} ${item.addresses} ${item.network} ${item.keywords}`.toLocaleLowerCase().includes(search)
      )
    : recent.flatMap((id) => results.filter((item) => item.id === id))
  return (
    <Combobox
      store={combobox}
      position="bottom-start"
      width={340}
      middlewares={{ flip: true, shift: { padding: 12, crossAxis: true }, size: true }}
      transitionProps={{
        transition: {
          in: { opacity: 1, transform: 'translateY(0)' },
          out: { opacity: 0, transform: 'translateY(-6px)' },
          transitionProperty: 'opacity, transform',
        },
        duration: reducedMotion ? 0 : 140,
      }}
      onOptionSubmit={(id) => {
        setRecent((items) => [id, ...items.filter((item) => item !== id)].slice(0, 4))
        combobox.closeDropdown()
        combobox.focusTarget()
        onSelect(id)
      }}
    >
      <Combobox.Target withKeyboardNavigation={false}>
        <Button
          variant="default"
          size="xs"
          leftSection={<LocateFixed size={15} />}
          rightSection={<ChevronDown size={13} />}
          onClick={() => combobox.toggleDropdown()}
        >
          定位对象
        </Button>
      </Combobox.Target>
      <Combobox.Dropdown className={styles.dropdown}>
        <Combobox.Search
          aria-label="搜索拓扑对象"
          placeholder="名称、IP 或网段"
          leftSection={<Search size={15} />}
          value={query}
          onChange={(event) => {
            setQuery(event.currentTarget.value)
            combobox.resetSelectedOption()
          }}
        />
        {!search && matches.length > 0 && <div className={styles.recent}>最近定位</div>}
        <ScrollArea.Autosize className={styles.results} type="never" scrollbars="y" overscrollBehavior="contain">
          <Combobox.Options aria-label="定位结果">
            {matches.map(({ id, name, icon: Icon, addresses, network }) => (
              <Combobox.Option key={id} value={id} className={styles.object}>
                <Icon size={17} />
                <span className={styles.identity}>
                  <strong>{name}</strong>
                  <span>{[addresses, network].filter(Boolean).join(' · ')}</span>
                </span>
              </Combobox.Option>
            ))}
            {!matches.length && (
              <Combobox.Empty>{search ? '没有匹配的对象' : '输入名称、IP 或网段查找'}</Combobox.Empty>
            )}
          </Combobox.Options>
        </ScrollArea.Autosize>
      </Combobox.Dropdown>
    </Combobox>
  )
}
