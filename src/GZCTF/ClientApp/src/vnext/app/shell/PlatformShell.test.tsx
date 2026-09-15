import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { describe, expect, it, vi } from 'vitest'
import { ModuleDrawer } from './PlatformShell'

vi.mock('../../features/account/useCurrentAccount', () => ({
  roleLabel: () => '管理员',
  useAccountLogout: () => vi.fn(),
  useCurrentAccount: () => ({ isAdmin: true }),
}))

describe('ModuleDrawer', () => {
  it('highlights only TeamLab on a TeamLab deep link', () => {
    render(
      <MemoryRouter initialEntries={['/admin/teamlab/scenes/scene-a/design']}>
        <ModuleDrawer onClose={vi.fn()} open />
      </MemoryRouter>
    )

    const currentLinks = screen.getAllByRole('link').filter((link) => link.getAttribute('aria-current') === 'page')
    expect(currentLinks).toHaveLength(1)
    expect(currentLinks[0]).toHaveTextContent('TeamLab')
    expect(screen.getByRole('link', { name: /平台管理/ })).not.toHaveAttribute('aria-current')
  })
})
