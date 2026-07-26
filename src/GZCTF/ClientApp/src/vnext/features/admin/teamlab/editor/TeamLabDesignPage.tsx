import { AlertTriangle, Cable, Check, Cloud, LoaderCircle, Network, Rocket, Save, Workflow } from 'lucide-react'
import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { createPortal } from 'react-dom'
import type { TeamLabImageOption } from '../api'
import {
  addTopologyNode,
  bulkMoveTopologyNodes,
  copyTopologyFragment,
  deleteTopologyItems,
  duplicateTopologyNodes,
  moveTopologyNode,
  pasteTopologyFragment,
  type TopologyFragment,
} from '../model/topologyCommands'
import type { TopologyDocument, TopologyNodeType } from '../model/topologyDocument'
import { selectAllTopology } from '../model/topologySelection'
import styles from './TeamLabDesignPage.module.css'
import { TeamLabCanvas } from './canvas/TeamLabCanvas'
import { TeamLabInspector } from './inspector'
import { autoLayoutTopology } from './layout/autoLayoutTopology'
import { createTopologyNode } from './nodeFactory'
import { NodePalette } from './palette/NodePalette'
import { connectCanvasNodes, type CanvasConnectionMode } from './state/canvasCommands'
import { useEditorHistory } from './state/useEditorHistory'
import { useEditorSelection } from './state/useEditorSelection'
import { useEditorShortcuts, type EditorShortcutHandlers } from './state/useEditorShortcuts'

export interface TeamLabEditorFocusTarget {
  nodeKey: string | null
  connectionKey: string | null
  requestId: number
}

export interface TeamLabDesignPageProps {
  initialDocument: TopologyDocument
  readOnly?: boolean
  onDocumentChange?: (document: TopologyDocument) => void
  onSave?: (document: TopologyDocument) => void | Promise<void>
  onValidate?: () => void | Promise<void>
  onPublish?: () => void
  saveStatus?: 'saved' | 'dirty' | 'saving' | 'error' | 'conflict'
  validationIssueCount?: number
  publicationStatus?: string
  publicationState?: 'current' | 'changed' | 'unpublished' | 'loading'
  publishDisabled?: boolean
  publishing?: boolean
  focusTarget?: TeamLabEditorFocusTarget | null
  imageOptions?: readonly TeamLabImageOption[]
}

function useCompactEditor() {
  const [compact, setCompact] = useState(() =>
    typeof window === 'undefined' ? false : window.matchMedia('(max-width: 760px)').matches
  )
  useEffect(() => {
    const media = window.matchMedia('(max-width: 760px)')
    const update = () => setCompact(media.matches)
    update()
    media.addEventListener('change', update)
    return () => media.removeEventListener('change', update)
  }, [])
  return compact
}

export function TeamLabDesignPage({
  initialDocument,
  readOnly = false,
  onDocumentChange,
  onSave,
  onValidate,
  onPublish,
  saveStatus = 'saved',
  validationIssueCount = 0,
  publicationStatus,
  publicationState = 'loading',
  publishDisabled = true,
  publishing = false,
  focusTarget,
  imageOptions = [],
}: TeamLabDesignPageProps) {
  const { document, canUndo, canRedo, commit, undo, redo } = useEditorHistory(initialDocument)
  const { selection, select, clear } = useEditorSelection(document)
  const [connectionMode, setConnectionMode] = useState<CanvasConnectionMode>('network')
  const [leftPanelOpen, setLeftPanelOpen] = useState(true)
  const [rightPanelOpen, setRightPanelOpen] = useState(true)
  const [focusMode, setFocusMode] = useState(false)
  const [layoutRequest, setLayoutRequest] = useState(0)
  const [feedback, setFeedback] = useState<string | null>(null)
  const clipboard = useRef<TopologyFragment | null>(null)
  const lastNotified = useRef(initialDocument)
  const compact = useCompactEditor()
  const effectiveReadOnly = readOnly || compact

  useEffect(() => {
    if (lastNotified.current === document) return
    lastNotified.current = document
    onDocumentChange?.(document)
  }, [document, onDocumentChange])

  useEffect(() => {
    if (!focusTarget) return
    select(
      focusTarget.nodeKey ? [focusTarget.nodeKey] : [],
      focusTarget.connectionKey ? [focusTarget.connectionKey] : []
    )
    if (focusTarget.nodeKey || focusTarget.connectionKey) setRightPanelOpen(true)
  }, [focusTarget, select])

  useEffect(() => {
    if (!focusMode || typeof globalThis.document === 'undefined') return
    const previousOverflow = globalThis.document.body.style.overflow
    globalThis.document.body.style.overflow = 'hidden'
    return () => {
      globalThis.document.body.style.overflow = previousOverflow
    }
  }, [focusMode])

  const addNode = useCallback(
    (type: TopologyNodeType, position?: { x: number; y: number }) => {
      if (effectiveReadOnly) return
      const index = Object.keys(document.nodes).length
      const fallback = { x: 80 + (index % 4) * 240, y: 100 + Math.floor(index / 4) * 160 }
      const result = addTopologyNode(document, createTopologyNode(document, type, position ?? fallback))
      commit(result.document)
      select([result.value], [])
      setFeedback(null)
    },
    [commit, document, effectiveReadOnly, select]
  )
  const connectNodes = useCallback(
    (sourceKey: string, targetKey: string) => {
      if (effectiveReadOnly) return
      try {
        commit(connectCanvasNodes(document, sourceKey, targetKey, connectionMode))
        setFeedback(null)
      } catch (error) {
        setFeedback(error instanceof Error ? error.message : '无法创建连接。')
      }
    },
    [commit, connectionMode, document, effectiveReadOnly]
  )
  const moveNodes = useCallback(
    (positions: ReadonlyMap<string, { x: number; y: number }>) => {
      if (effectiveReadOnly) return
      let nextDocument = document
      let changed = false
      for (const [key, position] of [...positions].sort(([left], [right]) => left.localeCompare(right))) {
        const current = nextDocument.nodes[key]
        if (!current || (current.position.x === position.x && current.position.y === position.y)) continue
        nextDocument = moveTopologyNode(nextDocument, key, position.x, position.y).document
        changed = true
      }
      if (changed) commit(nextDocument)
    },
    [commit, document, effectiveReadOnly]
  )
  const deleteSelection = useCallback(() => {
    if (effectiveReadOnly || (selection.nodeKeys.size === 0 && selection.connectionKeys.size === 0)) return
    commit(deleteTopologyItems(document, selection).document)
    clear()
  }, [clear, commit, document, effectiveReadOnly, selection])
  const copySelection = useCallback(() => {
    if (selection.nodeKeys.size) clipboard.current = copyTopologyFragment(document, selection.nodeKeys)
  }, [document, selection.nodeKeys])
  const pasteSelection = useCallback(() => {
    if (effectiveReadOnly || !clipboard.current) return
    const result = pasteTopologyFragment(document, clipboard.current)
    commit(result.document)
    select(result.value, [])
  }, [commit, document, effectiveReadOnly, select])
  const duplicateSelection = useCallback(() => {
    if (effectiveReadOnly || selection.nodeKeys.size === 0) return
    const result = duplicateTopologyNodes(document, selection.nodeKeys)
    commit(result.document)
    select(result.value, [])
  }, [commit, document, effectiveReadOnly, select, selection.nodeKeys])
  const nudge = useCallback(
    (delta: { x: number; y: number }) => {
      if (effectiveReadOnly || selection.nodeKeys.size === 0) return
      commit(bulkMoveTopologyNodes(document, selection.nodeKeys, delta).document)
    },
    [commit, document, effectiveReadOnly, selection.nodeKeys]
  )
  const autoLayout = useCallback(() => {
    if (effectiveReadOnly || Object.keys(document.nodes).length < 2) return
    commit(autoLayoutTopology(document))
    setLayoutRequest((value) => value + 1)
    setFeedback('已完成自动排版，可使用撤销恢复原布局。')
  }, [commit, document, effectiveReadOnly])
  const save = useCallback(() => void onSave?.(document), [document, onSave])
  const validate = useCallback(() => void onValidate?.(), [onValidate])
  const undoDocument = useCallback(() => {
    if (!effectiveReadOnly) undo()
  }, [effectiveReadOnly, undo])
  const redoDocument = useCallback(() => {
    if (!effectiveReadOnly) redo()
  }, [effectiveReadOnly, redo])
  const selectAll = useCallback(() => {
    const all = selectAllTopology(document)
    select(all.nodeKeys, all.connectionKeys)
  }, [document, select])
  const toggleFocus = useCallback(() => setFocusMode((value) => !value), [])
  const toggleLeftPanel = useCallback(() => setLeftPanelOpen((value) => !value), [])
  const toggleRightPanel = useCallback(() => setRightPanelOpen((value) => !value), [])
  const addPaletteNode = useCallback((type: TopologyNodeType) => addNode(type), [addNode])
  const useNetworkConnections = useCallback(() => setConnectionMode('network'), [])
  const useDependencyConnections = useCallback(() => setConnectionMode('dependency'), [])
  const shortcutHandlers = useMemo<EditorShortcutHandlers>(
    () => ({
      undo: undoDocument,
      redo: redoDocument,
      copy: copySelection,
      paste: pasteSelection,
      duplicate: duplicateSelection,
      delete: deleteSelection,
      selectAll,
      save,
      nudge,
    }),
    [
      copySelection,
      deleteSelection,
      duplicateSelection,
      nudge,
      pasteSelection,
      redoDocument,
      save,
      selectAll,
      undoDocument,
    ]
  )
  useEditorShortcuts(!effectiveReadOnly, shortcutHandlers)

  const leftVisible = leftPanelOpen && !focusMode
  const rightVisible = rightPanelOpen && !focusMode
  const content = (
    <section className={`${styles.page} ${focusMode ? styles.focusMode : ''}`}>
      <header className={styles.header}>
        <div className={styles.title}>
          <span>TOPOLOGY DESIGN</span>
          <strong>{document.name}</strong>
        </div>
        <div aria-label="连接类型" className={styles.connectionModes} role="group">
          <button aria-pressed={connectionMode === 'network'} onClick={useNetworkConnections} type="button">
            <Network size={15} />
            网络连接
          </button>
          <button aria-pressed={connectionMode === 'dependency'} onClick={useDependencyConnections} type="button">
            <Workflow size={15} />
            启动依赖
          </button>
        </div>
        <div className={styles.metrics}>
          <span>
            <Cable size={14} />
            {Object.keys(document.connections).length} 条连接
          </span>
          <span>
            <Network size={14} />
            {Object.keys(document.nodes).length} 个节点
          </span>
        </div>
        <div className={styles.documentActions}>
          {publicationStatus ? (
            <span className={styles.publicationStatus} data-state={publicationState}>{publicationStatus}</span>
          ) : null}
          {onValidate ? (
            <button className={styles.validateButton} onClick={validate} type="button">
              <AlertTriangle size={16} />
              校验{validationIssueCount > 0 ? ` (${validationIssueCount})` : ''}
            </button>
          ) : null}
          {onPublish ? (
            <button
              className={styles.publishButton}
              disabled={publishDisabled || publishing || saveStatus !== 'saved'}
              onClick={onPublish}
              type="button"
            >
              {publishing ? <LoaderCircle className={styles.spin} size={16} /> : <Rocket size={16} />}
              {publishing ? '发布中' : '发布新版本'}
            </button>
          ) : null}
          {onSave ? (
            <button className={styles.saveButton} disabled={saveStatus === 'saving'} onClick={save} type="button">
              {saveStatus === 'saving' ? (
                <LoaderCircle className={styles.spin} size={16} />
              ) : saveStatus === 'saved' ? (
                <Check size={16} />
              ) : saveStatus === 'dirty' ? (
                <Cloud size={16} />
              ) : (
                <Save size={16} />
              )}
              {saveStatus === 'saving'
                ? '保存中'
                : saveStatus === 'saved'
                  ? '已保存'
                  : saveStatus === 'conflict'
                    ? '保存冲突'
                    : saveStatus === 'error'
                      ? '重试保存'
                      : '保存'}
            </button>
          ) : null}
        </div>
      </header>
      {feedback ? (
        <div aria-live="polite" className={styles.feedback}>
          {feedback}
        </div>
      ) : null}
      {compact ? <div className={styles.mobileNotice}>移动端以只读模式显示拓扑。</div> : null}
      <div className={styles.workspace}>
        {leftVisible ? <NodePalette disabled={effectiveReadOnly} onAdd={addPaletteNode} /> : null}
        <TeamLabCanvas
          canRedo={!effectiveReadOnly && canRedo}
          canUndo={!effectiveReadOnly && canUndo}
          connectionMode={connectionMode}
          document={document}
          focusMode={focusMode}
          focusNodeKey={focusTarget?.nodeKey}
          leftPanelOpen={leftPanelOpen}
          layoutRequest={layoutRequest}
          onAddNode={addNode}
          onAutoLayout={autoLayout}
          onConnectNodes={connectNodes}
          onMoveNodes={moveNodes}
          onRedo={redoDocument}
          onSelectionChange={select}
          onToggleFocus={toggleFocus}
          onToggleLeftPanel={toggleLeftPanel}
          onToggleRightPanel={toggleRightPanel}
          onUndo={undoDocument}
          readOnly={effectiveReadOnly}
          rightPanelOpen={rightPanelOpen}
          selection={selection}
        />
        {rightVisible ? (
          <TeamLabInspector
            document={document}
            imageOptions={imageOptions}
            onDocumentChange={commit}
            readOnly={effectiveReadOnly}
            selection={selection}
          />
        ) : null}
      </div>
    </section>
  )
  return focusMode && typeof globalThis.document !== 'undefined'
    ? createPortal(content, globalThis.document.body)
    : content
}
