import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { commonAdminApi } from '../../api'
import { teamLabRuntimeApi, type TeamLabRuntime } from '../api'
import { RuntimeGrantPanel } from './RuntimeGrantPanel'

const runtime: TeamLabRuntime = {
  id: 'runtime-1', releaseId: 'release-1', generation: 1, status: 'running', stage: 'runtime-ready',
  openForAccess: true, shards: [], networks: [], assets: [], createdAt: 1, updatedAt: 1, error: null,
}

describe('RuntimeGrantPanel', () => {
  it('updates permissions when a checkbox is clicked', async () => {
    vi.spyOn(teamLabRuntimeApi, 'listGrants').mockResolvedValue([])
    vi.spyOn(teamLabRuntimeApi, 'listGrantTokens').mockResolvedValue([])
    vi.spyOn(commonAdminApi, 'users').mockResolvedValue({ items: [], total: 0, page: 1, pageSize: 100 })

    render(<RuntimeGrantPanel runtime={runtime} />)
    await screen.findByText('当前没有额外授权')

    const checkbox = screen.getByRole('checkbox', { name: '查看完整信息' })
    fireEvent.click(checkbox)
    expect(checkbox).toBeChecked()
    fireEvent.click(checkbox)
    expect(checkbox).not.toBeChecked()
  })
})
