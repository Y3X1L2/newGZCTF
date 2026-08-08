import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { FieldHelpButton } from './FieldHelpButton'

afterEach(() => vi.restoreAllMocks())

describe('FieldHelpButton', () => {
  it('renders the explanation above a control that has no room below it', async () => {
    vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockReturnValue({
      bottom: 760,
      height: 20,
      left: 900,
      right: 920,
      top: 740,
      width: 20,
      x: 900,
      y: 740,
      toJSON: () => ({}),
    })
    render(<FieldHelpButton fieldKey="stateless" />)

    fireEvent.click(screen.getByRole('button', { name: '关于无状态资产' }))

    await waitFor(() => expect(screen.getByRole('tooltip')).toHaveAttribute('data-placement', 'above'))
    expect(screen.getByRole('tooltip')).toHaveTextContent('无状态资产')
  })
})
