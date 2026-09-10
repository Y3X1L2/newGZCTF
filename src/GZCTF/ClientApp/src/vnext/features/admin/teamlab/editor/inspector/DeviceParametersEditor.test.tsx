import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { DeviceParametersEditor } from './DeviceParametersEditor'

const schema = { type: 'object', required: ['unit'], properties: {
  unit: { type: 'integer', title: '站号', minimum: 1, maximum: 247 }, enabled: { type: 'boolean', title: '启用' },
} }
describe('DeviceParametersEditor', () => {
  it('edits declared parameters through normal fields while preserving other values', () => {
    const change = vi.fn()
    render(<DeviceParametersEditor schema={schema} value={'{"unit":1,"enabled":true}'} onChange={change} />)
    const input = screen.getByLabelText('站号（必填）')
    fireEvent.change(input, { target: { value: '7' } })
    fireEvent.blur(input)
    expect(JSON.parse(change.mock.calls[0][0])).toEqual({ unit: 7, enabled: true })
    expect(input).toHaveAttribute('min', '1')
    expect(screen.getByLabelText('启用')).toHaveValue('true')
  })
  it('does not round fractional station numbers into a different command', () => {
    const change = vi.fn()
    render(<DeviceParametersEditor schema={schema} value={'{"unit":1}'} onChange={change} />)
    const input = screen.getByLabelText('站号（必填）')
    fireEvent.change(input, { target: { value: '1.5' } })
    fireEvent.blur(input)
    expect(change).not.toHaveBeenCalled()
  })
  it('keeps malformed imported JSON editable instead of discarding it', () => {
    render(<DeviceParametersEditor schema={schema} value={'{"unit":'} onChange={vi.fn()} />)
    expect(screen.getByLabelText(/设备参数 JSON/)).toHaveValue('{"unit":')
  })
})
