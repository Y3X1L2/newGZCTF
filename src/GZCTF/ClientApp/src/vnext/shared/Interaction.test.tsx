import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import { describe, expect, it, vi } from 'vitest'
import { VNextConfirmDialog, VNextDrawer } from './Interaction'

function DrawerHarness({ side = 'right' }: { side?: 'left' | 'right' }) {
  const [open, setOpen] = useState(false)
  return (
    <>
      <button onClick={() => setOpen(true)} type="button">
        打开抽屉
      </button>
      <VNextDrawer eyebrow="TEST" onClose={() => setOpen(false)} open={open} side={side} title="测试抽屉">
        抽屉内容
      </VNextDrawer>
    </>
  )
}

describe('VNextConfirmDialog', () => {
  it('requires exact confirmation text before invoking the action', async () => {
    const onConfirm = vi.fn()
    const user = userEvent.setup()
    render(
      <VNextConfirmDialog
        confirmationText="delete-me"
        message="不可恢复"
        onClose={() => undefined}
        onConfirm={onConfirm}
        open
        title="删除对象"
      />
    )

    const confirm = screen.getByRole('button', { name: '确认' })
    expect(confirm).toBeDisabled()
    await user.type(screen.getByRole('textbox'), 'delete-me')
    expect(confirm).toBeEnabled()
    await user.click(confirm)
    expect(onConfirm).toHaveBeenCalledOnce()
  })
})

describe('VNextDrawer', () => {
  it.each(['left', 'right'] as const)('animates and restores focus for a %s drawer', async (side) => {
    const user = userEvent.setup()
    render(<DrawerHarness side={side} />)
    const trigger = screen.getByRole('button', { name: '打开抽屉' })
    trigger.focus()
    await user.click(trigger)

    const dialog = screen.getByRole('dialog', { name: '测试抽屉' })
    expect(dialog).toHaveAttribute('open')
    await waitFor(() => expect(dialog.className).toMatch(/drawerOpening/))
    fireEvent.animationEnd(dialog.firstElementChild as Element)
    await user.click(within(dialog).getByRole('button', { name: '关闭' }))
    await waitFor(() => expect(dialog.className).toMatch(/drawerClosing/))
    fireEvent.animationEnd(dialog.firstElementChild as Element)

    await waitFor(() => expect(dialog).not.toHaveAttribute('open'))
    await waitFor(() => expect(trigger).toHaveFocus())
  })

  it('supports Escape and backdrop close paths', () => {
    vi.useFakeTimers()
    try {
      render(<DrawerHarness />)
      const trigger = screen.getByRole('button', { name: '打开抽屉' })

      fireEvent.click(trigger)
      let dialog = screen.getByRole('dialog', { name: '测试抽屉' })
      fireEvent(dialog, new Event('cancel', { bubbles: false, cancelable: true }))
      expect(dialog.className).toMatch(/drawerClosing/)
      act(() => vi.advanceTimersByTime(1000))
      expect(dialog).not.toHaveAttribute('open')

      fireEvent.click(trigger)
      dialog = screen.getByRole('dialog', { name: '测试抽屉' })
      fireEvent.click(dialog)
      expect(dialog.className).toMatch(/drawerClosing/)
      act(() => vi.advanceTimersByTime(1000))
      expect(dialog).not.toHaveAttribute('open')
    } finally {
      vi.useRealTimers()
    }
  })
})
