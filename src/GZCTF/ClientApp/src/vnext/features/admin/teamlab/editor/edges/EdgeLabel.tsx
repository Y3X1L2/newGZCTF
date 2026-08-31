import styles from './TopologyEdge.module.css'

/** Approximate advance width per character, in flow units, at the label size. */
const WIDE_CHARACTER = 11
const NARROW_CHARACTER = 6.2
const PLATE_PADDING_X = 6
const PLATE_HEIGHT = 18

/**
 * CJK glyphs are full-width while Latin/digits are roughly half. Measuring via
 * `getBBox` is not an option here: it forces layout per label on every viewport
 * change and returns 0 under jsdom, so the plate would collapse in tests.
 */
function estimateLabelWidth(label: string) {
  let width = 0
  for (const character of label) {
    width += /[\u2E80-\uFFFD]/.test(character) ? WIDE_CHARACTER : NARROW_CHARACTER
  }
  return width
}

/**
 * Link label with an opaque backplate.
 *
 * A bare `<text>` (what `EdgeText` renders) landed directly on region fills and
 * on crossing links, which made labels unreadable as soon as a scene had real
 * density. The plate is sized from the label content and both plate and text
 * collapse together under the canvas level-of-detail class.
 */
export function EdgeLabel({ label, x, y }: { label: string; x: number; y: number }) {
  if (!label) return null
  const width = estimateLabelWidth(label) + PLATE_PADDING_X * 2
  return (
    <g className={styles.labelGroup} pointerEvents="none">
      <rect
        className={styles.labelPlate}
        height={PLATE_HEIGHT}
        rx={4}
        width={width}
        x={x - width / 2}
        y={y - PLATE_HEIGHT / 2}
      />
      <text className={styles.label} dominantBaseline="central" textAnchor="middle" x={x} y={y}>
        {label}
      </text>
    </g>
  )
}
