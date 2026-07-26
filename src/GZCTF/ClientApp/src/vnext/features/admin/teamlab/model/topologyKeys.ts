import type { TopologyDocument } from './topologyDocument'

const invalidKeyCharacters = /[^a-zA-Z0-9_.-]+/g

export function normalizeTopologyKey(value: string, fallback = 'item') {
  const normalized = value
    .trim()
    .replace(invalidKeyCharacters, '-')
    .replace(/^-+|-+$/g, '')
  return (normalized || fallback).slice(0, 63)
}

export function topologyKeys(document: TopologyDocument) {
  const keys = new Set<string>([...Object.keys(document.nodes), ...Object.keys(document.connections)])
  for (const node of Object.values(document.nodes)) {
    if (node.type === 'switch') keys.add(node.networkKey)
  }
  return keys
}

export function nextTopologyKey(preferred: string, occupied: ReadonlySet<string>) {
  const base = normalizeTopologyKey(preferred)
  if (!occupied.has(base)) return base

  for (let suffix = 2; ; suffix += 1) {
    const suffixText = `-${suffix}`
    const candidate = `${base.slice(0, 63 - suffixText.length)}${suffixText}`
    if (!occupied.has(candidate)) return candidate
  }
}

export function buildKeyRemap(keys: readonly string[], preferredSuffix: string, occupiedKeys: ReadonlySet<string>) {
  const occupied = new Set(occupiedKeys)
  const remap = new Map<string, string>()
  for (const key of [...keys].sort()) {
    const next = nextTopologyKey(`${key}${preferredSuffix}`, occupied)
    occupied.add(next)
    remap.set(key, next)
  }
  return remap
}

export function dependencyConnectionKey(assetKey: string, dependsOnKey: string, condition: string) {
  return normalizeTopologyKey(`dependency-${assetKey}-${dependsOnKey}-${condition}`)
}
