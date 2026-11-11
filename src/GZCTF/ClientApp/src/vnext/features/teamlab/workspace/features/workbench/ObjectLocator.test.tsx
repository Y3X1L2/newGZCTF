import { MantineProvider } from '@mantine/core'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { afterAll, beforeAll, describe, expect, it, vi } from 'vitest'
import type { EnvironmentSpec } from '../../api/client'
import { ObjectLocator } from './ObjectLocator'

beforeAll(() =>
  vi.stubGlobal(
    'ResizeObserver',
    class {
      observe() {}
      unobserve() {}
      disconnect() {}
    }
  )
)
afterAll(() => vi.unstubAllGlobals())

const spec: EnvironmentSpec = {
  assets: [
    {
      id: 'web',
      name: 'unexpected-container-name',
      templateId: 'template',
      resources: { cpu: 2, memoryMiB: 2048, diskGiB: 20 },
      interfaces: [
        { id: 'a', networkId: 'entry', address: '10.0.0.2', mac: '', primary: true },
        { id: 'b', networkId: 'internal', address: '172.22.1.2', mac: '', primary: false },
      ],
    },
  ],
  networks: [
    { id: 'entry', name: 'Entry LAN', cidr: '10.0.0.0/24' },
    { id: 'internal', name: 'Internal LAN', cidr: '172.22.0.0/23' },
  ],
  routers: [{ id: 'router', name: 'Gateway', interfaces: [] }],
}

function setup() {
  const select = vi.fn()
  render(
    <MantineProvider env="test">
      <ObjectLocator spec={spec} templates={[]} states={new Map([['web', 'running']])} onSelect={select} />
    </MantineProvider>
  )
  fireEvent.click(screen.getByRole('button', { name: '定位对象' }))
  return select
}

describe('object locator', () => {
  it('offers selection immediately, lists each object once, and resets filters on reopen', async () => {
    const select = setup()
    const dialog = await screen.findByRole('dialog', { name: '定位对象' })
    expect(within(dialog).getAllByRole('button')).toHaveLength(4)
    expect(within(dialog).getByRole('button', { name: /unexpected-container-name/ })).toHaveTextContent(
      '10.0.0.2 · 172.22.1.2'
    )
    await waitFor(() => expect(screen.getByRole('textbox')).not.toHaveFocus())
    fireEvent.click(within(dialog).getByRole('radio', { name: '资产' }))
    expect(within(dialog).getAllByRole('button')).toHaveLength(1)
    fireEvent.click(within(dialog).getByRole('button', { name: /unexpected-container-name/ }))
    expect(select).toHaveBeenCalledWith('web')
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    fireEvent.click(screen.getByRole('button', { name: '定位对象' }))
    expect(within(await screen.findByRole('dialog')).getAllByRole('button')).toHaveLength(4)
  })

  it('finds a machine by its second address or network and keeps routers reachable', async () => {
    setup()
    const dialog = await screen.findByRole('dialog')
    const search = within(dialog).getByRole('textbox')
    fireEvent.change(search, { target: { value: '172.22.1.2' } })
    expect(within(dialog).getAllByRole('button')).toHaveLength(1)
    fireEvent.change(search, { target: { value: 'internal lan' } })
    expect(within(dialog).getByRole('button', { name: /unexpected-container-name/ })).toBeInTheDocument()
    fireEvent.change(search, { target: { value: '' } })
    fireEvent.click(within(dialog).getByRole('radio', { name: '网段' }))
    expect(within(dialog).getByRole('button', { name: /Gateway/ })).toBeInTheDocument()
    expect(within(dialog).queryByRole('button', { name: /unexpected-container-name/ })).not.toBeInTheDocument()
    fireEvent.change(search, { target: { value: 'missing' } })
    expect(within(dialog).getByText('没有匹配的对象')).toBeInTheDocument()
  })
})
