import { fireEvent, render, screen } from '@testing-library/react'
import { useState } from 'react'
import { describe, expect, it, vi } from 'vitest'
import { KeyValueEditor, TextInput } from './InspectorFields'

describe('TeamLab inspector fields', () => {
  it('commits a text editing session once on blur', () => {
    const commit = vi.fn()
    render(<TextInput label="名称" onChange={commit} value="旧名称" />)

    const input = screen.getByLabelText('名称')
    fireEvent.change(input, { target: { value: '新' } })
    fireEvent.change(input, { target: { value: '新名' } })
    fireEvent.change(input, { target: { value: '新名称' } })
    expect(commit).not.toHaveBeenCalled()
    fireEvent.blur(input)

    expect(commit).toHaveBeenCalledOnce()
    expect(commit).toHaveBeenCalledWith('新名称')
  })

  it('keeps key-value rows attached to their semantic key while editing', () => {
    function Harness() {
      const [values, setValues] = useState<Readonly<Record<string, string>>>({ FIRST: 'one', SECOND: 'two' })
      return <KeyValueEditor label="参数" onChange={setValues} values={values} />
    }
    render(<Harness />)

    const keys = screen.getAllByLabelText('参数键')
    fireEvent.change(keys[0], { target: { value: 'RENAMED' } })
    expect(screen.getAllByLabelText('参数值')[1]).toHaveValue('two')
    fireEvent.blur(keys[0])

    expect(screen.getAllByLabelText('参数键').map((input) => (input as HTMLInputElement).value)).toEqual([
      'SECOND',
      'RENAMED',
    ])
    expect(screen.getAllByLabelText('参数值').map((input) => (input as HTMLInputElement).value)).toEqual(['two', 'one'])
  })
})
