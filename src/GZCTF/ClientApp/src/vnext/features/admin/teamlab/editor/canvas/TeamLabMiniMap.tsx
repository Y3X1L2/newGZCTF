import { MiniMap } from '@xyflow/react'
import styles from './TeamLabCanvas.module.css'

export function TeamLabMiniMap() {
  return (
    <MiniMap
      ariaLabel="拓扑小地图"
      className={styles.miniMap}
      nodeClassName={(node) => styles[`miniMap_${node.type ?? 'docker'}`]}
      nodeStrokeWidth={2}
      pannable
      zoomable
    />
  )
}
