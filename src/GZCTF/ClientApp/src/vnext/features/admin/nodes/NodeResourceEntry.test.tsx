import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { NodeResourceEntry } from './NodeResourceEntry'

describe('node resource entries', () => {
  it('does not expose a stale connection in historical rows or details', () => {
    render(<NodeResourceEntry resource={{ ip: null, port: null, isActive: false, entry: 'https://example.test/remote?token=expired' }} />)
    expect(screen.queryByRole('link')).not.toBeInTheDocument()
    expect(screen.getByText('暂无可用入口')).toBeInTheDocument()
  })
  it('opens host-port entries as absolute HTTP links', () => {
    render(<NodeResourceEntry resource={{ ip: null, port: null, isActive: true, entry: '10.0.0.1:30000' }} />)
    expect(screen.getByRole('link', { name: '打开入口' })).toHaveAttribute('href', 'http://10.0.0.1:30000/')
  })
  it('keeps an active opaque connection URL out of visible labels', () => {
    render(<NodeResourceEntry resource={{ ip: null, port: null, isActive: true, entry: 'https://example.test/remote?token=fixture' }} />)
    expect(screen.getByRole('link')).toHaveTextContent('打开入口')
    expect(screen.queryByText(/fixture/)).not.toBeInTheDocument()
  })
  it.each(['javascript:alert(1)', 'https://user:password@example.test', '/relative/entry'])(
    'rejects unsafe or relative entry %s',
    (entry) => {
      render(<NodeResourceEntry resource={{ ip: null, port: null, isActive: true, entry }} />)
      expect(screen.queryByRole('link')).not.toBeInTheDocument()
      expect(screen.getByText('入口暂不可用')).toBeInTheDocument()
    }
  )
})
