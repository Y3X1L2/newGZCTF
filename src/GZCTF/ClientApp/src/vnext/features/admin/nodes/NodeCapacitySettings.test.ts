import { describe, expect, it } from 'vitest'
import { validateNodeCapacitySettings } from './NodeCapacitySettings'

describe('validateNodeCapacitySettings', () => {
  it('accepts independent Docker and VM limits including zero for an unused workload', () => {
    expect(validateNodeCapacitySettings('12', '0', true, 3, 0)).toMatchObject({
      containerError: null,
      vmError: null,
      value: { isSchedulable: true, maxContainers: 12, maxVms: 0 },
    })
  })

  it('rejects non-integers and limits below allocated capacity', () => {
    expect(validateNodeCapacitySettings('2.5', '1', false, 2, 2)).toMatchObject({
      containerError: '容器上限必须是整数。',
      vmError: 'VM 上限不能低于当前已分配数量 2。',
      value: null,
    })
  })

  it('does not interpret an empty input as zero', () => {
    expect(validateNodeCapacitySettings('', '0', true, 0, 0)).toMatchObject({
      containerError: '容器上限必须是整数。',
      value: null,
    })
  })

  it('enforces the server maximums before submitting', () => {
    expect(validateNodeCapacitySettings('10001', '1001', true, 0, 0)).toMatchObject({
      containerError: '容器上限不能超过 10000。',
      vmError: 'VM 上限不能超过 1000。',
      value: null,
    })
  })
})
