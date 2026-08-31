/**
 * Explicit stacking contract for the topology canvas.
 *
 * React Flow renders edges and nodes into two sibling containers and derives the
 * `z-index` of each from its `zIndex` field. When every object leaves `zIndex`
 * at the default 0, the outcome is decided purely by DOM order — and the edge
 * container is emitted *before* the node container, so links were painted
 * underneath the network region rectangles. A CSS rule on `.react-flow__edge`
 * cannot repair that, because the effective `z-index` lives on the parent `svg`.
 *
 * The canvas therefore runs React Flow in `zIndexMode="manual"` and assigns
 * every object a layer from this table, so the reading order is a tested
 * invariant instead of an accident of render order:
 *
 *   region (background container)
 *     < edge (links must be visible *over* the region they cross)
 *       < selected edge
 *         < node (devices stay the foremost clickable objects)
 *           < selected node
 */
export const topologyLayers = {
  /** Network region containers: the backdrop every other object draws over. */
  region: 1,
  /** Links, above region backdrops so a route crossing a network stays visible. */
  edge: 10,
  /** A selected link rises above its siblings but stays below every device. */
  edgeSelected: 20,
  /** Device nodes: always above links so their hit areas win. */
  node: 30,
  /** A selected device rises above its neighbours. */
  nodeSelected: 40,
} as const

export type TopologyLayer = keyof typeof topologyLayers

/** Layer for a network region container. */
export const regionLayer = () => topologyLayers.region

/** Layer for a link, raised while selected. */
export const edgeLayer = (selected: boolean) =>
  selected ? topologyLayers.edgeSelected : topologyLayers.edge

/** Layer for a device node, raised while selected. */
export const nodeLayer = (selected: boolean) =>
  selected ? topologyLayers.nodeSelected : topologyLayers.node
