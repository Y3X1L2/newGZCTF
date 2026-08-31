import { ChevronDown, LogIn, Maximize, Network } from 'lucide-react'
import { memo } from 'react'
import { NodeResizer, type Node, type NodeProps } from '@xyflow/react'
import {
  MAX_REGION_HEIGHT,
  MAX_REGION_WIDTH,
  MIN_REGION_HEIGHT,
  MIN_REGION_WIDTH,
} from '../../model/topologyGeometry'
import styles from './NetworkRegionNode.module.css'

/**
 * Region actions are one stable object owned by the canvas rather than per-render
 * closures, so an untouched region keeps its rendered element while dragging.
 */
export interface TeamLabRegionActions {
  toggleCollapsed: (networkKey: string, collapsed: boolean) => void
  resize: (networkKey: string, width: number, height: number) => void
  fitToMembers: (networkKey: string) => void
}

export interface TeamLabRegionData extends Record<string, unknown> {
  networkKey: string
  switchKey: string
  name: string
  addressPool: string
  isEntry: boolean
  memberCount: number
  readOnly: boolean
  active: boolean
  collapsed: boolean
  actions: TeamLabRegionActions
}

export type TeamLabRegionFlowNode = Node<TeamLabRegionData, 'region'>

export const networkRegionNodeId = (networkKey: string) => `region:${networkKey}`

/** Stops a header control from starting a region drag or a canvas pan. */
const swallowPointer = (event: React.PointerEvent) => event.stopPropagation()

export function NetworkRegionNode({ data }: NodeProps<TeamLabRegionFlowNode>) {
  const { actions, networkKey } = data
  const interactive = !data.readOnly
  return (
    <>
      <NodeResizer
        isVisible={data.active && interactive && !data.collapsed}
        maxHeight={MAX_REGION_HEIGHT}
        maxWidth={MAX_REGION_WIDTH}
        minHeight={MIN_REGION_HEIGHT}
        minWidth={MIN_REGION_WIDTH}
        onResizeEnd={(_event, params) => actions.resize(networkKey, params.width, params.height)}
      />
      <article
        aria-label={`网络区域 ${data.name}`}
        className={styles.region}
        data-active={data.active || undefined}
        data-collapsed={data.collapsed || undefined}
        data-entry={data.isEntry || undefined}
      >
        <header className={styles.header}>
          <span aria-hidden="true" className={styles.icon}>
            {data.isEntry ? <LogIn size={15} /> : <Network size={15} />}
          </span>
          <span className={styles.heading}>
            <strong title={data.name}>{data.name}</strong>
            <small title={data.addressPool}>{data.addressPool}</small>
          </span>
          <span className={styles.badges}>
            {data.isEntry ? <span className={styles.entryBadge}>入口</span> : null}
            <span className={styles.memberBadge}>{data.memberCount} 台</span>
          </span>
          <span className={styles.controls}>
            <button
              aria-label={`将区域 ${data.name} 收拢到成员大小`}
              className="nodrag nopan"
              disabled={!interactive || data.collapsed}
              onClick={(event) => {
                event.stopPropagation()
                actions.fitToMembers(networkKey)
              }}
              onPointerDown={swallowPointer}
              title="按成员数量重新收拢此区域"
              type="button"
            >
              <Maximize aria-hidden="true" size={14} />
            </button>
            <button
              aria-expanded={!data.collapsed}
              aria-label={data.collapsed ? `展开区域 ${data.name}` : `折叠区域 ${data.name}`}
              className={`nodrag nopan ${styles.collapseButton}`}
              disabled={!interactive}
              onClick={(event) => {
                event.stopPropagation()
                actions.toggleCollapsed(networkKey, !data.collapsed)
              }}
              onPointerDown={swallowPointer}
              title={data.collapsed ? '展开此网段中的资产' : '折叠此网段中的资产'}
              type="button"
            >
              <ChevronDown aria-hidden="true" size={15} />
            </button>
          </span>
        </header>
        {data.collapsed ? (
          <p className={styles.collapsedHint}>已折叠 · {data.memberCount} 台资产未显示</p>
        ) : null}
      </article>
    </>
  )
}

export const NetworkRegionNodeView = memo(NetworkRegionNode)
