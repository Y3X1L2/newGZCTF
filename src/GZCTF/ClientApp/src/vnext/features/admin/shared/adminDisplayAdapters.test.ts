import { describe, expect, it } from 'vitest'
import { ImageStatus, NodeCapability, NodeStatus } from '@Api'
import { imageStatusMeta } from '../images/useAdminImages'
import { hasNodeCapability, nodeStatusMeta } from '../nodes/useAdminNodes'

describe('admin display adapters', () => {
  it('maps every image lifecycle state to a stable visual tone', () => {
    expect(imageStatusMeta(ImageStatus.Ready)).toMatchObject({ label: '可用', tone: 'success', active: false })
    expect(imageStatusMeta(ImageStatus.Importing)).toMatchObject({ label: '处理中', tone: 'info', active: true })
    expect(imageStatusMeta(ImageStatus.Error)).toMatchObject({ label: '异常', tone: 'danger', active: false })
    expect(imageStatusMeta(ImageStatus.Deleting)).toMatchObject({ label: '删除中', tone: 'warning', active: true })
  })

  it('maps node states without treating unknown values as healthy', () => {
    expect(nodeStatusMeta(NodeStatus.Online)).toMatchObject({ label: '在线', tone: 'success' })
    expect(nodeStatusMeta(NodeStatus.Busy)).toMatchObject({ label: '繁忙', tone: 'warning' })
    expect(nodeStatusMeta(NodeStatus.Error)).toMatchObject({ label: '异常', tone: 'danger' })
    expect(nodeStatusMeta(NodeStatus.Offline)).toMatchObject({ label: '离线', tone: 'neutral' })
    expect(nodeStatusMeta(NodeStatus.Unknown)).toMatchObject({ label: '未知', tone: 'neutral' })

  })

  it('checks bit-mask capabilities independently', () => {
    const capabilities = NodeCapability.Docker | NodeCapability.Kvm
    expect(hasNodeCapability(capabilities, NodeCapability.Docker)).toBe(true)
    expect(hasNodeCapability(capabilities, NodeCapability.Kvm)).toBe(true)
  })
})
