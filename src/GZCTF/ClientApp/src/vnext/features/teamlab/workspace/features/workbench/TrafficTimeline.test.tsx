import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import type { History } from '../../foundation/metrics'
import { TrafficTimeline } from './TrafficTimeline'

vi.mock('../../foundation/MetricChart', () => ({
  MetricChart: ({ focusedAsset }: { focusedAsset?: string }) => (
    <div role="img" aria-label={focusedAsset ?? '全部资产'} />
  ),
}))

const history: History = { start: '2026-11-11T00:00:00Z', end: '2026-11-11T01:00:00Z', stepSeconds: 60, series: [] }

describe('traffic asset focus', () => {
  it('starts with all machines, isolates a clicked machine, switches directly and restores all on a second click', () => {
    render(
      <TrafficTimeline
        history={history}
        names={
          new Map([
            ['web', 'Web'],
            ['db', 'Database'],
          ])
        }
        colors={['blue', 'purple']}
        unit="KiB/s"
      />
    )
    expect(screen.getByRole('img', { name: '全部资产' })).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Web' }))
    expect(screen.getByRole('img', { name: 'web' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Web' })).toHaveAttribute('aria-pressed', 'true')
    fireEvent.click(screen.getByRole('button', { name: 'Database' }))
    expect(screen.getByRole('img', { name: 'db' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Web' })).toHaveAttribute('aria-pressed', 'false')
    fireEvent.click(screen.getByRole('button', { name: 'Database' }))
    expect(screen.getByRole('img', { name: '全部资产' })).toBeInTheDocument()
  })
})
