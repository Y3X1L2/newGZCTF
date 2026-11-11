import { MantineProvider } from '@mantine/core'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterAll, beforeAll, describe, expect, it, vi } from 'vitest'
import type { EnvironmentSpec } from '../../api/client'
import { ObjectLocator } from './ObjectLocator'

beforeAll(() => {
  Object.defineProperty(HTMLElement.prototype, 'scrollIntoView', { configurable: true, value: vi.fn() })
  vi.stubGlobal(
    'ResizeObserver',
    class {
      observe() {}
      unobserve() {}
      disconnect() {}
    }
  )
})
afterAll(() => {
  Reflect.deleteProperty(HTMLElement.prototype, 'scrollIntoView')
  vi.unstubAllGlobals()
})

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
      <ObjectLocator spec={spec} templates={[]} onSelect={select} />
    </MantineProvider>
  )
  fireEvent.click(screen.getByRole('button', { name: '定位对象' }))
  return select
}

describe('object locator', () => {
  it('starts compact, locates using keyboard, and offers the recent object on reopen', async () => {
    const select = setup()
    expect(screen.queryByRole('option')).not.toBeInTheDocument()
    const search = await screen.findByRole('textbox', { name: '搜索拓扑对象' })
    fireEvent.change(search, { target: { value: '172.22.1.2' } })
    expect(screen.getAllByRole('option')).toHaveLength(1)
    fireEvent.keyDown(search, { key: 'ArrowDown', code: 'ArrowDown' })
    fireEvent.keyDown(search, { key: 'Enter', code: 'Enter' })
    expect(select).toHaveBeenCalledWith('web')
    await waitFor(() => expect(screen.queryByRole('option')).not.toBeInTheDocument())
    fireEvent.click(screen.getByRole('button', { name: '定位对象' }))
    expect(screen.getByText('最近定位')).toBeInTheDocument()
    expect(screen.getAllByRole('option')).toHaveLength(1)
  })
  it('matches every connected network without duplicate results and closes with Escape', async () => {
    setup()
    const search = await screen.findByRole('textbox', { name: '搜索拓扑对象' })
    fireEvent.change(search, { target: { value: 'internal lan' } })
    expect(screen.getAllByRole('option')).toHaveLength(2)
    expect(screen.getByRole('option', { name: /unexpected-container-name/ })).toHaveTextContent(
      'Entry LAN · Internal LAN'
    )
    fireEvent.change(search, { target: { value: 'Gateway' } })
    expect(screen.getAllByRole('option')).toHaveLength(1)
    fireEvent.change(search, { target: { value: 'missing' } })
    expect(screen.getByText('没有匹配的对象')).toBeInTheDocument()
    fireEvent.keyDown(search, { key: 'Escape' })
    await waitFor(() => expect(screen.queryByRole('textbox', { name: '搜索拓扑对象' })).not.toBeInTheDocument())
  })
})
