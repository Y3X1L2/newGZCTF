import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import type { Schema } from '../../api/client'
import { PacketList } from './PacketWorkspace'

vi.mock('./TrafficGraph', () => ({ riskLevel: vi.fn() }))
vi.mock('./trafficModel', () => ({ packetAlerts: () => [] }))

describe('packet detail navigation', () => {
  it('lets arrow keys select a row without opening a dialog, then opens on Enter', () => {
    const packets = [1, 2].map((number) => ({
      id: String(number),
      number,
      time: '2026-11-11T00:00:00Z',
      source: '192.0.2.1',
      sourcePort: 12345,
      destination: '192.0.2.2',
      destinationPort: 443,
      protocol: 'TLS',
      length: 60,
      summary: 'Application Data',
    })) as Schema<'CapturePacket'>[]
    const onSelect = vi.fn(),
      onOpen = vi.fn()
    render(
      <PacketList
        packets={packets}
        alerts={[]}
        loading={false}
        fetching={false}
        live={false}
        onSelect={onSelect}
        onOpen={onOpen}
      />
    )
    fireEvent.keyDown(screen.getByRole('grid', { name: '报文列表' }), { key: 'ArrowDown' })
    expect(onSelect).toHaveBeenCalledWith('1')
    expect(onOpen).not.toHaveBeenCalled()
    fireEvent.keyDown(screen.getByRole('row', { name: /^1 / }), { key: 'Enter' })
    expect(onOpen).toHaveBeenCalledWith('1')
  })
})
