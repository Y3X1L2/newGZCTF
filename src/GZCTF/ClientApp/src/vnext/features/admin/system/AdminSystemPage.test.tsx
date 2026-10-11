import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { AdminSystemPage } from './AdminSystemPage'

const mocks = vi.hoisted(() => ({
  config: {
    globalConfig: { title: 'YINYU' },
    accountPolicy: { allowRegister: true },
    containerPolicy: { maxExerciseContainerCountPerUser: 1, autoDestroyOnLimitReached: true },
    trainingContainerPolicy: { maxContainerCountPerUser: 3 },
  },
  update: vi.fn(),
  mutate: vi.fn(),
}))
vi.mock('./useAdminSystem', () => ({ useAdminSystem: () => ({ config: mocks.config, mutate: mocks.mutate }) }))
vi.mock('../../../shared/useVNextPageTitle', () => ({ useVNextPageTitle: vi.fn() }))
vi.mock('../api', () => ({ commonAdminApi: { updateSystemConfig: mocks.update } }))

beforeEach(() => {
  vi.clearAllMocks()
  mocks.config.trainingContainerPolicy.maxContainerCountPerUser = 3
  mocks.update.mockResolvedValue(undefined)
  mocks.mutate.mockResolvedValue(undefined)
})

describe('training quota settings', () => {
  it('saves zero independently and preserves unrelated drafts when rereading', async () => {
    const view = render(<AdminSystemPage />)
    fireEvent.change(screen.getByLabelText('平台标题'), { target: { value: 'unsaved title' } })
    fireEvent.change(screen.getByRole('spinbutton', { name: /每名用户培训容器上限/ }), { target: { value: '0' } })
    fireEvent.click(screen.getByRole('button', { name: '保存培训额度' }))
    await waitFor(() => expect(mocks.mutate).toHaveBeenCalledOnce())
    expect(mocks.update).toHaveBeenCalledExactlyOnceWith({ trainingContainerPolicy: { maxContainerCountPerUser: 0 } })
    // SWR returns fresh objects for all sections, although only training changed.
    mocks.config = { ...mocks.config, globalConfig: { ...mocks.config.globalConfig },
      trainingContainerPolicy: { maxContainerCountPerUser: 0 } }
    view.rerender(<AdminSystemPage />)
    expect(screen.getByLabelText('平台标题')).toHaveValue('unsaved title')
    expect(screen.getByRole('spinbutton', { name: /每名用户培训容器上限/ })).toHaveValue(0)
    expect(screen.getByRole('button', { name: '保存培训额度' })).toBeDisabled()
  })

  it.each(['', '-1', '1.5', '2147483648'])('blocks invalid quota %j', (value) => {
    render(<AdminSystemPage />)
    fireEvent.change(screen.getByRole('spinbutton', { name: /每名用户培训容器上限/ }), { target: { value } })
    expect(screen.getByRole('button', { name: '保存培训额度' })).toBeDisabled()
    expect(screen.getByRole('spinbutton', { name: /每名用户培训容器上限/ })).toHaveAttribute('aria-invalid', 'true')
    expect(mocks.update).not.toHaveBeenCalled()
  })

  it('keeps edits and reports save errors without claiming success', async () => {
    mocks.update.mockRejectedValueOnce(new Error('save unavailable'))
    render(<AdminSystemPage />)
    fireEvent.change(screen.getByRole('spinbutton', { name: /每名用户培训容器上限/ }), { target: { value: '5' } })
    fireEvent.click(screen.getByRole('button', { name: '保存培训额度' }))
    await waitFor(() => expect(screen.getByText('save unavailable')).toBeVisible())
    expect(screen.getByRole('spinbutton', { name: /每名用户培训容器上限/ })).toHaveValue(5)
    expect(mocks.mutate).not.toHaveBeenCalled()
    expect(screen.queryByText('培训容器额度已保存，新建请求立即生效。')).not.toBeInTheDocument()
  })
})
