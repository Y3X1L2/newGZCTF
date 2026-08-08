import { ChevronDown, Network } from 'lucide-react'
import { memo } from 'react'
import { NodeResizer, type Node, type NodeProps } from '@xyflow/react'
import styles from './NetworkRegionNode.module.css'

export interface TeamLabRegionData extends Record<string, unknown> {
  networkKey: string
  switchKey: string
  name: string
  addressPool: string
  isEntry: boolean
  memberKeys: readonly string[]
  readOnly: boolean
  active: boolean
  collapsed: boolean
  onCollapseToggle?: () => void
  onResizeEnd?: (size: { width: number; height: number }) => void
}

export type TeamLabRegionFlowNode = Node<TeamLabRegionData, 'region'>

export const networkRegionNodeId = (networkKey: string) => `region:${networkKey}`

export function NetworkRegionNode({ data }: NodeProps<TeamLabRegionFlowNode>) {
  return (
    <>
      <NodeResizer
        isVisible={data.active && !data.readOnly}
        minHeight={198}
        minWidth={304}
        onResizeEnd={(_event, params) => data.onResizeEnd?.({ width: params.width, height: params.height })}
      />
      <article
        aria-label={`网络区域 ${data.name}`}
        className={`${styles.region} ${data.active ? styles.selected : ''} ${data.collapsed ? styles.collapsed : ''}`}
        data-selected={data.active || undefined}
      >
        <header>
          <Network aria-hidden="true" size={17} />
          <span>
            <strong title={data.name}>{data.name}</strong>
            <small>{data.addressPool} · {data.memberKeys.length} 个成员</small>
          </span>
          <button
            aria-expanded={!data.collapsed}
            aria-label={data.collapsed ? `展开区域 ${data.name}` : `折叠区域 ${data.name}`}
            className="nodrag nopan"
            disabled={data.readOnly}
            onPointerDown={(event) => event.stopPropagation()}
            onClick={(event) => {
              event.stopPropagation()
              data.onCollapseToggle?.()
            }}
            title={data.collapsed ? '展开此网段中的资产' : '折叠此网段中的资产'}
            type="button"
          >
            <ChevronDown aria-hidden="true" size={16} />
          </button>
        </header>
        {data.collapsed ? (
          <p className={styles.collapsedHint}>已折叠，成员资产暂不显示</p>
        ) : (
          <p className={styles.hint}>{data.isEntry ? '入口网段' : '内部网段'} · 交换机 {data.switchKey}</p>
        )}
      </article>
    </>
  )
}

export const NetworkRegionNodeView = memo(NetworkRegionNode)
