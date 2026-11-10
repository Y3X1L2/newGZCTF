import type { EnvironmentSpec, Network, Schema } from '../../api/client'

export function networkOptions(spec: Pick<EnvironmentSpec, 'networks' | 'routers'>) {
  const base = spec.networks.map((network) => `${network.name} · ${network.cidr}`)
  const counts = (labels: string[]) => {
    const result = new Map<string, number>()
    for (const label of labels) result.set(label, (result.get(label) ?? 0) + 1)
    return result
  }
  const duplicates = counts(base)
  const titles = spec.networks.map((network, index) => {
    if (duplicates.get(base[index]) === 1) return network.name
    const routers = (spec.routers ?? []).filter((router) =>
      router.interfaces.some((iface) => iface.networkId === network.id)
    )
    return `${network.name} · ${routers.map((router) => router.name).join(' / ') || '纯二层'}`
  })
  const labels = spec.networks.map((network, index) => `${titles[index]} · ${network.cidr}`)
  const ambiguous = counts(labels)
  return spec.networks.map((network, index) => {
    const title = ambiguous.get(labels[index])! > 1 ? `${index + 1}. ${titles[index]}` : titles[index]
    return { value: network.id, title, label: `${title} · ${network.cidr}` }
  })
}

function addressBits(address: string): string | undefined {
  if (!address.includes(':'))
    return /^\d+\.\d+\.\d+\.\d+$/.test(address)
      ? address
          .split('.')
          .map((part) => Number(part).toString(2).padStart(8, '0'))
          .join('')
      : undefined
  const halves = address.split('::').map((half) =>
    half
      ? half.split(':').flatMap((part) => {
          if (!part.includes('.')) return [part]
          const value = part.split('.').reduce((total, byte) => total * 256 + Number(byte), 0)
          return [Math.floor(value / 65536).toString(16), (value % 65536).toString(16)]
        })
      : []
  )
  const words =
    halves.length === 1
      ? halves[0]
      : [...halves[0], ...Array(8 - halves[0].length - halves[1].length).fill('0'), ...halves[1]]
  return words.length === 8 ? words.map((word) => parseInt(word, 16).toString(2).padStart(16, '0')).join('') : undefined
}

export function networkContains(cidr: string, address: string) {
  const [base, prefix] = cidr.split('/'),
    a = addressBits(base),
    b = addressBits(address)
  return (
    a !== undefined &&
    b !== undefined &&
    a.length === b.length &&
    a.slice(0, Number(prefix)) === b.slice(0, Number(prefix))
  )
}

export function networksOverlap(a: Network, b: Network) {
  return networkContains(a.cidr, b.cidr.split('/')[0]) || networkContains(b.cidr, a.cidr.split('/')[0])
}

export function routerGateways(
  networks: Network[],
  before: Schema<'LogicalRouter'>[],
  after: Schema<'LogicalRouter'>[]
): Network[] {
  return networks.map((network) => {
    if (
      network.external ||
      !network.gateway ||
      !before.some((router) =>
        router.interfaces.some((iface) => iface.networkId === network.id && iface.address === network.gateway)
      )
    )
      return network
    const remaining = after.flatMap((router) => router.interfaces.filter((iface) => iface.networkId === network.id))
    return remaining.some((iface) => iface.address === network.gateway)
      ? network
      : { ...network, gateway: remaining[0]?.address || undefined }
  })
}
