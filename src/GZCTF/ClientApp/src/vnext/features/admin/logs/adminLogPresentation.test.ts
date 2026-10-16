import { createInstance } from 'i18next'
import { describe, expect, it } from 'vitest'
import en from '../../../../locales/en-US/admin.json'
import zh from '../../../../locales/zh-CN/admin.json'
import type { AdminLogEntry } from '../api'
import {
  adminLogActor,
  adminLogHasDiagnostics,
  adminLogIp,
  adminLogKey,
  adminLogMatches,
  adminLogNode,
  adminLogObject,
  adminLogResult,
  adminLogTitle,
  mergeAdminLogs,
} from './adminLogPresentation'

const log: AdminLogEntry = {
  id: 1,
  time: 100,
  name: 'admin',
  level: 'Information',
  ip: '10.24.0.27',
  msg: 'Runtime started.',
  status: 'Success',
}

describe('admin log presentation', () => {
  it('prefers durable ids and creates a stable fallback key', () => {
    expect(adminLogKey(log)).toBe('id:1')
    expect(adminLogKey({ ...log, id: undefined })).toBe(adminLogKey({ ...log, id: undefined }))
  })

  it('deduplicates and sorts merged history and realtime logs', () => {
    expect(mergeAdminLogs([log], [{ ...log }, { ...log, id: 2, time: 200 }]).map((item) => item.id)).toEqual([2, 1])
  })
})

const translations = createInstance()
await translations.init({
  lng: 'zh-CN',
  fallbackLng: 'en-US',
  resources: { 'zh-CN': { admin: zh }, 'en-US': { admin: en } },
})
const text = (key: string) => translations.t(`logs.${key}`, { ns: 'admin' })

describe('readable log facts', () => {
  it('labels names resolved from current records separately from recorded names', () => {
    const current = {
      ...log,
      actorKind: 'User',
      actorDisplayName: '林老师',
      workerNodeName: '训练节点一',
      resourceType: 'image-template',
      resourceDisplayName: '培训镜像',
      currentDisplayNames: ['actor', 'node', 'resource'],
    }
    expect(adminLogActor(current, text)).toBe('林老师 (当前名称)')
    expect(adminLogNode(current, text)).toBe('训练节点一 (当前名称)')
    expect(adminLogObject(current, text)).toBe('镜像 · 培训镜像 (当前名称)')
    expect(adminLogActor({ ...current, currentDisplayNames: [] }, text)).toBe('林老师')
    expect(adminLogObject({ ...current, resourceType: 'worker-node', resourceDisplayName: null }, text)).toBe(
      '节点 · 训练节点一 (当前名称)'
    )
  })
  it('distinguishes known actor kinds from missing historical identity', () => {
    expect(adminLogActor({ ...log, name: 'Anonymous' }, text)).toBe('操作人未记录')
    expect(adminLogActor({ ...log, name: 'Anonymous', actorKind: 'Anonymous' }, text)).toBe('未登录请求')
    expect(adminLogActor({ ...log, actorKind: 'System', workerNodeName: 'node-one' }, text)).toBe('平台系统')
    expect(adminLogActor({ ...log, actorKind: 'Agent' }, text)).toBe('节点 Agent')
    expect(adminLogActor({ ...log, actorKind: 'User', actorDisplayName: '林老师' }, text)).toBe('林老师')
    expect(adminLogActor({ ...log, actorKind: 'Unknown' }, text)).toBe('admin · 类型未记录')
    expect(adminLogActor({ ...log, actorKind: 'ApiToken', actorDisplayName: '林老师' }, text)).toBe(
      '林老师 · API 调用方'
    )
  })
  it('never treats Observed or a historical default Success as a successful operation', () => {
    expect(adminLogResult({ ...log, eventCode: 'node.capability.changed' }, text)).toMatchObject({
      label: '结果未记录',
      tone: 'neutral',
    })
    expect(adminLogResult({ ...log, eventCode: 'node.capability.changed', outcome: 'Observed' }, text)).toMatchObject({
      label: '已观测',
      tone: 'neutral',
    })
    expect(adminLogResult({ ...log, outcome: 'Blocked' }, text)).toMatchObject({ label: '已阻止', tone: 'danger' })
    expect(adminLogResult(log, text).label).toBe('成功')
    expect(adminLogResult({ ...log, status: null }, text).label).toBe('结果未记录')
  })
  it('does not turn old SystemLog defaults into evidence of a successful operation', () => {
    expect(adminLogResult({ ...log, name: 'System', actorKind: 'Unknown' }, text)).toMatchObject({
      label: '结果未记录',
      tone: 'neutral',
    })
    expect(adminLogResult({ ...log, name: 'System', actorKind: null }, text).label).toBe('结果未记录')
    expect(adminLogResult({ ...log, name: 'System', actorKind: 'Unknown', outcome: 'Succeeded' }, text).label).toBe(
      '成功'
    )
    expect(adminLogResult({ ...log, name: 'System', actorKind: 'User' }, text).label).toBe('成功')
    expect(adminLogResult({ ...log, name: 'Anonymous', actorKind: 'Unknown' }, text)).toMatchObject({
      label: '结果未记录',
      tone: 'neutral',
    })
    expect(adminLogResult({ ...log, name: 'Anonymous', actorKind: null, outcome: 'Succeeded' }, text).label).toBe(
      '成功'
    )
    expect(adminLogResult({ ...log, name: 'Anonymous', actorKind: 'User' }, text).label).toBe('成功')
  })
  it('uses exact event and operation names while preserving an honest unknown fallback', () => {
    expect(adminLogTitle({ ...log, eventCode: 'agent.sync.succeeded' }, text)).toBe('节点 Agent 更新完成')
    expect(
      adminLogTitle(
        { ...log, eventCode: 'audit.admin.mutation.succeeded', detail: { operation: 'Admin.UpdateUserInfo' } },
        text
      )
    ).toBe('修改用户信息')
    expect(adminLogTitle({ ...log, eventCode: 'future.new.failed' }, text)).toBe('未识别的运行事件')
    expect(adminLogTitle({ ...log, eventCode: null }, text)).toBe(log.msg)
  })
  it('keeps UUIDs and event codes out of missing object labels and matches localized text', () => {
    const event = {
      ...log,
      eventCode: 'agent.sync.succeeded',
      resourceId: '92b8fd25-066b-418e-b3df-024843a20972',
      resourceDisplayName: '92b8fd25-066b-418e-b3df-024843a20972',
    }
    expect(adminLogObject(event, text)).toBe('未记录对象名称')
    expect(adminLogMatches(event, 'Agent 更新完成', '', text)).toBe(true)
    expect(adminLogMatches({ ...event, actorKind: 'System' }, '', '平台系统', text)).toBe(true)
    expect(adminLogMatches({ ...event, resourceDisplayName: '课程实验一' }, '课程实验', '', text)).toBe(true)
    expect(adminLogObject({ ...event, resourceType: 'worker-node' }, text)).toBe('节点名称未记录')
    expect(adminLogObject({ ...event, resourceType: 'worker-node', workerNodeName: '节点一' }, text)).toBe(
      '节点 · 节点一'
    )
  })
  it('distinguishes absent request IP from explicit background events', () => {
    expect(adminLogIp({ ...log, ip: null }, text)).toBe('IP 未记录')
    expect(adminLogIp({ ...log, ip: null, actorKind: 'System' }, text)).toBe('后台事件，无请求 IP')
    expect(adminLogIp({ ...log, ip: null, actorKind: 'Agent' }, text)).toBe('IP 未记录')
    expect(adminLogIp({ ...log, ip: null, actorKind: 'User' }, text)).toBe('IP 未记录')
    expect(adminLogIp(log, text)).toBe('10.24.0.27')
  })
  it('opens diagnostics only when there is failure evidence', () => {
    expect(adminLogHasDiagnostics({ ...log, outcome: 'Observed' })).toBe(false)
    expect(adminLogHasDiagnostics({ ...log, outcome: 'Blocked', retryable: false })).toBe(true)
    expect(adminLogHasDiagnostics({ ...log, errorCode: 'agent.timeout' })).toBe(true)
  })
  it('handles multiple unpersisted live events and reconciles their later persisted records', () => {
    const first = { ...log, id: 0 }
    const second = { ...log, id: 0, time: 200 }
    expect(adminLogKey(first)).not.toBe(adminLogKey(second))
    expect(
      mergeAdminLogs(
        [first, second],
        [
          { ...first, id: 10 },
          { ...second, id: 11 },
        ]
      ).map((row) => row.id)
    ).toEqual([11, 10])
    expect(
      mergeAdminLogs([
        { ...first, id: 10 },
        { ...first, id: 12 },
      ])
    ).toHaveLength(2)
  })
})
