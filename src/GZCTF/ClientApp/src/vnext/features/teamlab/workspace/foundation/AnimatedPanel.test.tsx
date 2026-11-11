import { render, screen, waitFor } from '@testing-library/react'
import { AnimatePresence, MotionConfig } from 'motion/react'
import { describe, expect, it } from 'vitest'
import { AnimatedPanel } from './AnimatedPanel'

describe('panel presence', () => {
  it('disables exiting content, allows reopening, and removes it after the exit completes', async () => {
    const view = (opened: boolean) => (
      <MotionConfig transition={{ duration: 0.02 }}>
        <AnimatePresence initial={false}>
          {opened && (
            <AnimatedPanel className="panel" label="对象详情">
              <button>控制台</button>
            </AnimatedPanel>
          )}
        </AnimatePresence>
      </MotionConfig>
    )
    const { rerender } = render(view(true))
    const panel = screen.getByRole('complementary', { name: '对象详情' })
    rerender(view(false))
    expect(panel).toHaveAttribute('inert')
    expect(panel).toHaveAttribute('aria-hidden', 'true')
    rerender(view(true))
    expect(screen.getByRole('complementary', { name: '对象详情' })).not.toHaveAttribute('inert')
    rerender(view(false))
    await waitFor(() => expect(panel).not.toBeInTheDocument())
  })
})
