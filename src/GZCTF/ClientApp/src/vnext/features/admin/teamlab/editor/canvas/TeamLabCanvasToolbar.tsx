import { Panel, useReactFlow } from '@xyflow/react'
import {
  Focus,
  Maximize2,
  PanelLeftClose,
  PanelRightClose,
  Redo2,
  Undo2,
  WandSparkles,
  ZoomIn,
  ZoomOut,
} from 'lucide-react'
import { useCallback, type ButtonHTMLAttributes, type ReactNode } from 'react'
import type { TeamLabFlowEdge } from '../edges/edgeTypes'
import type { TeamLabFlowNode } from '../nodes/nodeTypes'
import styles from './TeamLabCanvas.module.css'

function ToolButton({
  label,
  children,
  ...props
}: ButtonHTMLAttributes<HTMLButtonElement> & { label: string; children: ReactNode }) {
  return (
    <button aria-label={label} title={label} type="button" {...props}>
      {children}
    </button>
  )
}

export function TeamLabCanvasToolbar({
  canUndo,
  canRedo,
  focusMode,
  readOnly,
  leftPanelOpen,
  rightPanelOpen,
  onUndo,
  onRedo,
  onAutoLayout,
  onToggleFocus,
  onToggleLeftPanel,
  onToggleRightPanel,
}: {
  canUndo: boolean
  canRedo: boolean
  focusMode: boolean
  readOnly: boolean
  leftPanelOpen: boolean
  rightPanelOpen: boolean
  onUndo: () => void
  onRedo: () => void
  onAutoLayout: () => void
  onToggleFocus: () => void
  onToggleLeftPanel: () => void
  onToggleRightPanel: () => void
}) {
  const flow = useReactFlow<TeamLabFlowNode, TeamLabFlowEdge>()
  const zoomIn = useCallback(() => void flow.zoomIn(), [flow])
  const zoomOut = useCallback(() => void flow.zoomOut(), [flow])
  const fitView = useCallback(
    () =>
      void flow.fitView({
        padding: 0.18,
        duration: window.matchMedia('(prefers-reduced-motion: reduce)').matches ? 0 : 240,
      }),
    [flow]
  )
  return (
    <Panel className={styles.toolbar} position="top-left">
      <ToolButton disabled={!canUndo} label="撤销" onClick={onUndo}>
        <Undo2 size={17} />
      </ToolButton>
      <ToolButton disabled={!canRedo} label="重做" onClick={onRedo}>
        <Redo2 size={17} />
      </ToolButton>
      <span aria-hidden="true" />
      <ToolButton label="放大" onClick={zoomIn}>
        <ZoomIn size={17} />
      </ToolButton>
      <ToolButton label="缩小" onClick={zoomOut}>
        <ZoomOut size={17} />
      </ToolButton>
      <ToolButton label="适配视图" onClick={fitView}>
        <Maximize2 size={17} />
      </ToolButton>
      <ToolButton disabled={readOnly} label="一键自动排版" onClick={onAutoLayout}>
        <WandSparkles size={17} />
      </ToolButton>
      <span aria-hidden="true" />
      <ToolButton aria-pressed={leftPanelOpen} label="切换节点库" onClick={onToggleLeftPanel}>
        <PanelLeftClose size={17} />
      </ToolButton>
      <ToolButton aria-pressed={rightPanelOpen} label="切换检查器" onClick={onToggleRightPanel}>
        <PanelRightClose size={17} />
      </ToolButton>
      <ToolButton aria-pressed={focusMode} label="专注模式" onClick={onToggleFocus}>
        <Focus size={17} />
      </ToolButton>
    </Panel>
  )
}
