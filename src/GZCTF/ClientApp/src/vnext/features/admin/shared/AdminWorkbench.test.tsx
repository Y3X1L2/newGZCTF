import { fireEvent, render, screen, within } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { DataTable, type AdminDataColumn } from './AdminWorkbench'

interface TestRow {
  id: number
  name: string
}

const rows: TestRow[] = [{ id: 1, name: 'worker-01' }]

describe('DataTable', () => {
  it('activates clickable rows with pointer and keyboard input', () => {
    const onRowClick = vi.fn()
    const columns: AdminDataColumn<TestRow>[] = [
      { id: 'name', header: '节点', render: (row) => row.name },
    ]
    render(
      <DataTable caption="节点列表" columns={columns} onRowClick={onRowClick} rowKey={(row) => row.id} rows={rows} />
    )

    const row = screen.getByRole('row', { name: 'worker-01' })
    fireEvent.click(row)
    fireEvent.keyDown(row, { key: 'Enter' })
    fireEvent.keyDown(row, { key: ' ' })

    expect(onRowClick).toHaveBeenCalledTimes(3)
    expect(onRowClick).toHaveBeenLastCalledWith(rows[0])
  })

  it('does not activate the row when an embedded control is clicked', () => {
    const onRowClick = vi.fn()
    const onAction = vi.fn()
    const columns: AdminDataColumn<TestRow>[] = [
      { id: 'name', header: '节点', render: (row) => row.name },
      {
        id: 'action',
        header: '操作',
        render: () => (
          <button onClick={onAction} type="button">
            查看
          </button>
        ),
      },
    ]
    render(
      <DataTable caption="节点列表" columns={columns} onRowClick={onRowClick} rowKey={(row) => row.id} rows={rows} />
    )

    const row = screen.getByRole('row', { name: 'worker-01 查看' })
    fireEvent.click(within(row).getByRole('button', { name: '查看' }))

    expect(onAction).toHaveBeenCalledOnce()
    expect(onRowClick).not.toHaveBeenCalled()
  })
})
