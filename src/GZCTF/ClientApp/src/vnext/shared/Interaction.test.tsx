import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import { describe, expect, it, vi } from 'vitest'
import { ActionButton, VNextConfirmDialog, VNextDrawer } from './Interaction'

describe('ActionButton', () => {
  it('locks an asynchronous action until it settles and preserves its accessible name', async () => {
    let complete!: () => void
    const action = vi.fn(() => new Promise<void>((resolve) => { complete = resolve }))
    render(<ActionButton onClick={action}>刷新</ActionButton>)
    const button = screen.getByRole('button', { name: '刷新' })
    fireEvent.click(button)
    fireEvent.click(button)
    expect(action).toHaveBeenCalledOnce()
    expect(button).toHaveAttribute('aria-busy', 'true')
    expect(button).toBeDisabled()
    await act(async () => complete())
    expect(button).toBeEnabled()
    expect(button).not.toHaveAttribute('aria-busy')
  })

  it('allows retry after a rejected action and displays a safe error', async () => {
    const action = vi.fn().mockRejectedValueOnce(new Error('private server detail')).mockResolvedValueOnce(undefined)
    render(<ActionButton onClick={action}>重试</ActionButton>)
    const button = screen.getByRole('button', { name: '重试' })
    await act(async () => fireEvent.click(button))
    expect(screen.getByRole('alert')).toHaveTextContent('操作未完成，请重试。')
    expect(screen.queryByText('private server detail')).not.toBeInTheDocument()
    expect(button).toBeEnabled()
    await act(async () => fireEvent.click(button))
    expect(action).toHaveBeenCalledTimes(2)
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('keeps server-side work pending after the click request completes', async () => {
    const action = vi.fn().mockResolvedValue(undefined)
    const { rerender } = render(<ActionButton onClick={action}>停止</ActionButton>)
    await act(async () => fireEvent.click(screen.getByRole('button', { name: '停止' })))
    rerender(<ActionButton loading loadingLabel="正在停止" onClick={action}>停止</ActionButton>)
    const button = screen.getByRole('button', { name: '正在停止' })
    expect(button).toBeDisabled()
    expect(button).toHaveAttribute('aria-busy', 'true')
  })

  it('preserves synchronous form submission and keyboard activation', async () => {
    const submit = vi.fn((event: React.FormEvent) => event.preventDefault())
    render(<form onSubmit={submit}><ActionButton type="submit">保存</ActionButton></form>)
    screen.getByRole('button', { name: '保存' }).focus()
    await userEvent.setup().keyboard('{Enter}')
    expect(submit).toHaveBeenCalledOnce()
    expect(screen.getByRole('button', { name: '保存' })).toBeEnabled()
  })
})

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
