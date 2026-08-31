import { useStore } from '@xyflow/react'

/**
 * Zoom below which per-device detail rows stop being readable. At that scale the
 * mono resource text is sub-pixel noise, so cards drop it and show a bigger title.
 */
export const COMPACT_DETAIL_ZOOM = 0.55

/** Zoom below which link labels are hidden entirely. */
export const HIDDEN_LABEL_ZOOM = 0.4

export type CanvasDetailLevel = 'full' | 'compact' | 'minimal'

export function detailLevelForZoom(zoom: number): CanvasDetailLevel {
  if (zoom < HIDDEN_LABEL_ZOOM) return 'minimal'
  if (zoom < COMPACT_DETAIL_ZOOM) return 'compact'
  return 'full'
}

/** Global classes the canvas puts on its root so CSS Modules can react to zoom. */
export const detailLevelClass: Record<CanvasDetailLevel, string> = {
  full: '',
  compact: 'teamlab-lod-compact',
  minimal: 'teamlab-lod-compact teamlab-lod-minimal',
}

/**
 * Level-of-detail for the topology canvas.
 *
 * A large scene rendered at full detail while zoomed out produces overlapping
 * text and hundreds of unreadable labels. One level derived from live zoom lets
 * cards and links degrade together, and keeps that decision out of every node.
 *
 * The selector returns the *level*, not the zoom, so a pan/zoom gesture only
 * re-renders when the level actually changes rather than on every frame.
 */
export function useCanvasDetailLevel(): CanvasDetailLevel {
  return useStore((state) => detailLevelForZoom(state.transform[2]))
}
