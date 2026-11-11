import { useMediaQuery } from '@mantine/hooks'
import { motion, useIsPresent } from 'motion/react'
import type { ReactNode } from 'react'
import styles from './AnimatedPanel.module.css'

export const workspaceMotionDuration = 180

/** Presence owns exit timing; business selection remains the only open/close state. */
export function AnimatedPanel({
  children,
  className,
  label,
}: {
  children: ReactNode
  className: string
  label: string
}) {
  const present = useIsPresent()
  const stacked = useMediaQuery('(max-width: 760px)')
  const dimension = stacked ? 'height' : 'width'
  return (
    <motion.div
      className={styles.slot}
      initial={{ opacity: 0, [dimension]: 0 }}
      animate={{ opacity: 1, width: stacked ? '100%' : 'auto', height: 'auto' }}
      exit={{ opacity: 0, [dimension]: 0 }}
    >
      <motion.div className={styles.spacing} initial={{ x: 12 }} animate={{ x: 0 }} exit={{ x: 12 }}>
        <aside className={className} aria-label={label} aria-hidden={!present} inert={!present}>
          {children}
        </aside>
      </motion.div>
    </motion.div>
  )
}
