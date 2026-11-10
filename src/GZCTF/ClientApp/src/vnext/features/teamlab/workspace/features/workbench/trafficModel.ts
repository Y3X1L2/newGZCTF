import type { Environment, Schema } from '../../api/client'
import type { EndpointKind, TrafficSelection, TrafficLink } from './TrafficGraph'
import { networkContains as contains } from './networking'

type Spec = Environment['spec']
type Conversation = Pick<Schema<'CaptureFlow'>, 'source' | 'destination' | 'sourceAssetId' | 'destinationAssetId'> & {
  networkId?: string
}
const matchesAddress = (value: string, address: string) =>
  Boolean(value) && value.split('/')[0].toLowerCase() === address.toLowerCase()

export function describeEndpoint(spec: Spec, item: Conversation, side: 'source' | 'destination') {
  const address = item[side],
    assetId = item[`${side}AssetId`]
  const candidates = spec.assets.filter((asset) =>
    asset.interfaces.some(
      (iface) =>
        (!item.networkId || iface.networkId === item.networkId) &&
        (matchesAddress(iface.mac, address) || matchesAddress(iface.address, address))
    )
  )
  const asset =
    spec.assets.find((asset) => asset.id === assetId) ?? (candidates.length === 1 ? candidates[0] : undefined)
  if (asset) {
    const interfaces = asset.interfaces.filter(
      (iface) => matchesAddress(iface.address, address) || matchesAddress(iface.mac, address)
    )
    return {
      id: asset.id,
      name: asset.name,
      kind: 'asset' as EndpointKind,
      networkId:
        asset.interfaces.find((iface) => iface.networkId === item.networkId)?.networkId ??
        (interfaces.length === 1 ? interfaces[0].networkId : undefined),
    }
  }
  const peerId = side === 'source' ? item.destinationAssetId : item.sourceAssetId
  const peer = !item.networkId && peerId ? spec.assets.find((asset) => asset.id === peerId) : undefined
  const peerAddress = side === 'source' ? item.destination : item.source
  const peerInterfaces = peer?.interfaces.filter(
    (iface) =>
      (!item.networkId || iface.networkId === item.networkId) &&
      (matchesAddress(iface.address, peerAddress) || matchesAddress(iface.mac, peerAddress))
  )
  const peerInterface = peerInterfaces?.length === 1 ? peerInterfaces[0] : undefined
  const inferredNetwork = () => {
    const containing = spec.networks.filter((network) => contains(network.cidr, address))
    return containing.length === 1 ? containing[0] : undefined
  }
  const network =
    spec.networks.find((network) => network.id === item.networkId) ??
    spec.networks.find((network) => network.id === peerInterface?.networkId) ??
    inferredNetwork()
  const routers =
    spec.routers?.filter((router) =>
      router.interfaces.some(
        (iface) => (!network || iface.networkId === network.id) && matchesAddress(iface.address, address)
      )
    ) ?? []
  if (routers.length === 1)
    return {
      id: routers[0].id,
      name: routers[0].name,
      kind: 'router' as EndpointKind,
      networkId: routers[0].interfaces.find(
        (iface) => (!network || iface.networkId === network.id) && matchesAddress(iface.address, address)
      )!.networkId,
    }
  const devices = spec.networks.flatMap((network) =>
    (network.devices ?? []).filter(
      (device) =>
        (!item.networkId || network.id === item.networkId) &&
        (matchesAddress(device.address, address) || matchesAddress(device.mac, address))
    )
  )
  if (devices.length === 1)
    return {
      id: devices[0].id,
      name: devices[0].name,
      kind: 'device' as EndpointKind,
      networkId: spec.networks.find((network) => network.devices?.some((device) => device.id === devices[0].id))!.id,
    }
  return network
    ? {
        id: network.id,
        name: network.name,
        kind: 'network' as EndpointKind,
        networkId: network.id,
      }
    : undefined
}

export function selectionMatches(spec: Spec, selection: TrafficSelection | undefined, item: Conversation) {
  if (!selection) return true
  const source = describeEndpoint(spec, item, 'source')?.id,
    destination = describeEndpoint(spec, item, 'destination')?.id
  return 'asset' in selection
    ? [source, destination].includes(selection.asset)
    : (source === selection.source && destination === selection.destination) ||
        (source === selection.destination && destination === selection.source)
}

export function addressFilter(address: string) {
  if (/^(?:[\da-f]{2}:){5}[\da-f]{2}$/i.test(address)) return `eth.addr == ${address}`
  return `${address.includes(':') ? 'ipv6' : 'ip'}.addr == ${address.split('/')[0]}`
}
export function endpointFilter(spec: Spec, members: string[]) {
  const expressions = members.flatMap((member) => {
    const network = spec.networks.find((network) => network.id === member)
    if (network) {
      const interfaces = [
        ...spec.assets.flatMap((asset) => asset.interfaces),
        ...(spec.routers ?? []).flatMap((router) => router.interfaces),
        ...(network.devices ?? []).map((device) => ({
          ...device,
          networkId: network.id,
        })),
      ].filter((iface) => iface.networkId === network.id)
      const known = (side: 'src' | 'dst') => {
        const addresses = [...new Set(interfaces.map((iface) => iface.address.split('/')[0]).filter(Boolean))]
        const macs = [...new Set(interfaces.flatMap((iface) => ('mac' in iface && iface.mac ? [iface.mac] : [])))]
        const ip = addresses.map((address) => `${address.includes(':') ? 'ipv6' : 'ip'}.${side} == ${address}`)
        const ethernet = macs.map((mac) => `eth.${side} == ${mac}`)
        return (
          [...ip, ...(ethernet.length ? [`(!ip && !ipv6 && (${ethernet.join(' || ')}))`] : [])].join(' || ') ||
          'frame.number < 0'
        )
      }
      return [`(!((${known('src')}) && (${known('dst')})))`]
    }
    const asset = spec.assets.find((asset) => asset.id === member)
    const router = spec.routers?.find((router) => router.id === member)
    const device = spec.networks.flatMap((network) => network.devices ?? []).find((device) => device.id === member)
    return (asset?.interfaces ?? router?.interfaces ?? (device ? [device] : [])).flatMap((iface) => [
      ...(iface.address ? [addressFilter(iface.address)] : []),
      ...('mac' in iface && iface.mac ? [`eth.addr == ${iface.mac}`] : []),
    ])
  })
  return `(${[...new Set(expressions)].join(' || ')})`
}
export function selectionFilter(spec: Spec, selection: TrafficSelection | undefined) {
  if (!selection) return ''
  const managed = (id: string) => spec.assets.some((asset) => asset.id === id)
  const network = (id: string) => spec.networks.some((network) => network.id === id)
  return 'asset' in selection
    ? managed(selection.asset) || network(selection.asset)
      ? ''
      : endpointFilter(spec, [selection.asset])
    : managed(selection.source)
      ? endpointFilter(spec, [selection.destination])
      : managed(selection.destination)
        ? endpointFilter(spec, [selection.source])
        : `(${endpointFilter(spec, [selection.source])} && ${endpointFilter(spec, [selection.destination])})`
}
export function selectionAsset(spec: Spec, selection: TrafficSelection | undefined) {
  const ids = selection ? ('asset' in selection ? [selection.asset] : [selection.source, selection.destination]) : []
  return ids.find((id) => spec.assets.some((asset) => asset.id === id))
}
export function selectionNetwork(spec: Spec, selection: TrafficSelection | undefined) {
  const ids = selection ? ('asset' in selection ? [selection.asset] : [selection.source, selection.destination]) : []
  const network = ids.find((id) => spec.networks.some((network) => network.id === id))
  if (network) return network
  const memberships = ids.map(
    (id) =>
      new Set(
        [
          ...(spec.assets.find((asset) => asset.id === id)?.interfaces ?? []),
          ...(spec.routers?.find((router) => router.id === id)?.interfaces ?? []),
          ...spec.networks
            .filter((network) => network.devices?.some((device) => device.id === id))
            .map((network) => ({ networkId: network.id })),
        ].map((iface) => iface.networkId)
      )
  )
  const unique = [...(memberships[0] ?? [])].filter((id) => memberships.every((networks) => networks.has(id)))
  return unique.length === 1 ? unique[0] : undefined
}

export function packetAlerts(packet: Schema<'CapturePacket'>, alerts: Schema<'SecurityAlert'>[]) {
  const timestamp = Date.parse(packet.time)
  return alerts.filter((alert) => {
    const forward =
      packet.source === alert.source &&
      packet.destination === alert.destination &&
      packet.sourcePort === alert.sourcePort &&
      packet.destinationPort === alert.destinationPort
    const reverse =
      packet.source === alert.destination &&
      packet.destination === alert.source &&
      packet.sourcePort === alert.destinationPort &&
      packet.destinationPort === alert.sourcePort
    return (
      (!packet.networkId || !alert.networkId || packet.networkId === alert.networkId) &&
      (forward || reverse) &&
      (packet.transportProtocol ?? packet.protocol).toLowerCase().replace(/icmpv[46]|ipv6-icmp/g, 'icmp') ===
        alert.protocol.toLowerCase().replace(/icmpv[46]|ipv6-icmp/g, 'icmp') &&
      timestamp >= Date.parse(alert.firstSeen) - 1000 &&
      timestamp <= Date.parse(alert.lastSeen) + 1000
    )
  })
}

const pairKey = (source: string, target: string) => JSON.stringify([source, target].sort())
export function buildTrafficLinks(
  spec: Spec,
  flows: Schema<'CaptureFlow'>[],
  alerts: Schema<'SecurityAlert'>[],
  packets?: Schema<'CapturePacket'>[],
  riskOnly = false
): TrafficLink[] {
  const detected = new Map<
    string,
    {
      source: NonNullable<ReturnType<typeof describeEndpoint>>
      target: NonNullable<ReturnType<typeof describeEndpoint>>
      severity: number
      count: number
    }
  >()
  for (const alert of alerts) {
    const source = describeEndpoint(spec, alert, 'source'),
      target = describeEndpoint(spec, alert, 'destination')
    if (!source || !target || source.id === target.id) continue
    const key = pairKey(source.id, target.id),
      previous = detected.get(key)
    if (previous) {
      previous.severity = Math.min(previous.severity, alert.severity)
      previous.count += alert.count
    } else
      detected.set(key, {
        source,
        target,
        severity: alert.severity,
        count: alert.count,
      })
  }
  const make = (item: Conversation, value: number, rate?: number) => {
    const source = describeEndpoint(spec, item, 'source'),
      target = describeEndpoint(spec, item, 'destination')
    if (!source || !target || source.id === target.id || value <= 0) return []
    return [
      {
        source: source.id,
        target: target.id,
        sourceName: source.name,
        targetName: target.name,
        sourceKind: source.kind,
        targetKind: target.kind,
        sourceNetworkId: source.networkId,
        targetNetworkId: target.networkId,
        value,
        rate,
        severity: detected.get(pairKey(source.id, target.id))?.severity,
      },
    ]
  }
  const links: TrafficLink[] = packets
    ? packets.flatMap((packet) =>
        make(
          {
            ...packet,
          },
          packet.length
        )
      )
    : flows.flatMap((flow) => make(flow, flow.bytes, flow.bytesPerSecond))
  for (const { source, target, severity, count } of detected.values())
    links.push({
      source: source.id,
      target: target.id,
      sourceName: source.name,
      targetName: target.name,
      sourceKind: source.kind,
      targetKind: target.kind,
      sourceNetworkId: source.networkId,
      targetNetworkId: target.networkId,
      value: 0,
      alerts: count,
      severity,
    })
  return riskOnly ? links.filter((link) => link.severity !== undefined) : links
}
export function trafficTopology(spec: Spec, links: TrafficLink[]): Spec {
  const ids = new Set(links.flatMap((link) => [link.source, link.target]))
  const interfaces = new Map<string, Set<string>>()
  for (const link of links)
    for (const side of ['source', 'target'] as const) {
      const network = link[`${side}NetworkId`]
      if (!network) continue
      const networks = interfaces.get(link[side]) ?? new Set<string>()
      networks.add(network)
      interfaces.set(link[side], networks)
    }
  const networks = new Set([...interfaces.values()].flatMap((set) => [...set]))
  return {
    networks: spec.networks
      .filter((network) => ids.has(network.id) || networks.has(network.id))
      .map((network) => ({
        ...network,
        devices: network.devices?.filter((device) => ids.has(device.id)),
      })),
    routers: spec.routers
      ?.filter((router) => ids.has(router.id))
      .map((router) => ({
        ...router,
        interfaces: router.interfaces.filter((iface) => networks.has(iface.networkId)),
      })),
    assets: spec.assets
      .filter((asset) => ids.has(asset.id))
      .map((asset) => ({
        ...asset,
        interfaces: asset.interfaces.filter((iface) => interfaces.get(asset.id)?.has(iface.networkId)),
      })),
  }
}

export function selectionLabel(spec: Spec, selection: TrafficSelection | undefined, links: TrafficLink[]) {
  const name = (id: string) =>
    spec.assets.find((asset) => asset.id === id)?.name ??
    spec.networks.find((network) => network.id === id)?.name ??
    spec.routers?.find((router) => router.id === id)?.name ??
    spec.networks.flatMap((network) => network.devices ?? []).find((device) => device.id === id)?.name ??
    links.find((link) => link.source === id)?.sourceName ??
    links.find((link) => link.target === id)?.targetName ??
    id
  return selection
    ? 'asset' in selection
      ? name(selection.asset)
      : `${name(selection.source)} ↔ ${name(selection.destination)}`
    : undefined
}
export function alertFilter(alert: Schema<'SecurityAlert'>) {
  const protocol = alert.protocol.toLowerCase(),
    portProtocol = protocol === 'tcp' || protocol === 'udp' ? protocol : undefined
  return [
    addressFilter(alert.source),
    addressFilter(alert.destination),
    portProtocol
      ? `${portProtocol}.port == ${alert.sourcePort} && ${portProtocol}.port == ${alert.destinationPort}`
      : protocol === 'icmpv4'
        ? 'icmp'
        : protocol === 'icmpv6'
          ? 'icmpv6'
          : protocol,
  ].join(' && ')
}
