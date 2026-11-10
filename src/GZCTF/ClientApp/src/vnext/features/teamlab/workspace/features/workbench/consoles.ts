import type { Asset, Schema } from '../../api/client'

export type ConsoleTab = Pick<Asset, 'id' | 'name'> & {
  kind: Schema<'ConsoleKind'>
  revision?: number
}

export function consoleKey(tab: ConsoleTab) {
  return `${tab.id}:${tab.kind}`
}
