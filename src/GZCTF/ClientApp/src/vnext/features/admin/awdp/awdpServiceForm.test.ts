import { describe, expect, it } from 'vitest'
import {
  awdpServiceWarnings,
  emptyAwdpServiceDraft,
  toAwdpServiceWriteModel,
  validateAwdpService,
} from './awdpServiceForm'

describe('AWDP service form', () => {
  it('blocks invalid required and numeric values', () => {
    const draft = { ...emptyAwdpServiceDraft(), exposePort: 70_000, totalRounds: 0 }
    expect(validateAwdpService(draft)).toEqual(
      expect.arrayContaining([
        '服务名称不能为空。',
        '容器镜像不能为空。',
        '暴露端口必须为 1 到 65535 的整数。',
        '总轮数必须为大于 0 的整数。',
      ])
    )
  })

  it('allows saving a draft while reporting missing checker and exploit scripts', () => {
    const draft = { ...emptyAwdpServiceDraft(), name: 'web', imageName: 'registry/web:latest' }
    expect(validateAwdpService(draft)).toHaveLength(0)
    expect(awdpServiceWarnings(draft)).toHaveLength(2)
    expect(toAwdpServiceWriteModel(draft)).toMatchObject({ name: 'web', imageName: 'registry/web:latest' })
  })
})
