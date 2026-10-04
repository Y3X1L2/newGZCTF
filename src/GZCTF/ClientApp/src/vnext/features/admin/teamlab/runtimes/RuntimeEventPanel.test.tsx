import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { RuntimeEventPanel } from './RuntimeEventPanel'

describe('RuntimeEventPanel', () => {
  it('shows managed guest network stages in filters and event details', () => {
    render(
      <RuntimeEventPanel
        currentGeneration={4}
        events={[
          {
            cursor: 8,
            generation: 4,
            stage: 'guest-network-verify',
            level: 'success',
            message: '来宾网络配置已验证',
            objectType: 'asset',
            objectId: 'dc01',
            createdAt: 1_791_062_400_000,
          },
        ]}
        filters={{ generation: null, stage: '' }}
        loading={false}
        onFiltersChange={vi.fn()}
      />
    )

    expect(screen.getByRole('option', { name: '验证来宾网络' })).toBeInTheDocument()
    expect(screen.getByRole('option', { name: '第 4 代（当前）' })).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: /来宾网络配置已验证/ }))
    expect(screen.getByText('事件详情')).toBeInTheDocument()
    expect(screen.getByText('第 4 代')).toBeInTheDocument()
    expect(screen.getAllByText('验证来宾网络')).toHaveLength(2)
  })
})
