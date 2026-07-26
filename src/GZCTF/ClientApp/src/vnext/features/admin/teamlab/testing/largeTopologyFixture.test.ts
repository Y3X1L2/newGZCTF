import { describe, expect, it } from 'vitest'
import { compileTopologyDocument } from '../model/topologyCompiler'
import { mapTopologyDetailToDocument } from '../model/topologyMapper'
import { createLargeTopologyFixture } from './largeTopologyFixture'

describe('large TeamLab topology fixture', () => {
  it('compiles deterministically with routers, multi-NIC assets and dependencies', () => {
    const document = createLargeTopologyFixture()
    const first = compileTopologyDocument(document)
    const second = compileTopologyDocument(createLargeTopologyFixture())

    expect(first.networks).toHaveLength(32)
    expect(first.assets).toHaveLength(128)
    expect(first.infrastructure).toHaveLength(40)
    expect(first.connections).toHaveLength(24)
    expect(first.dependencies).toHaveLength(96)
    expect(JSON.stringify(first)).toBe(JSON.stringify(second))
  })

  it('maps the persisted projection back without losing its structural scale', () => {
    const compiled = compileTopologyDocument(createLargeTopologyFixture())
    const document = mapTopologyDetailToDocument(
      {
        id: '019f-large-topology',
        revision: 1,
        schemaVersion: 2,
        definition: compiled,
        editor: compiled.editor,
        createdAt: 1,
        updatedAt: 1,
      },
      {
        resolveVmDeviceType: (item) => item.imageTemplateId === 3001 ? 'windows-vm' : 'linux-vm',
      }
    )

    expect(Object.keys(document.nodes)).toHaveLength(168)
    expect(Object.values(document.connections).filter((item) => item.type === 'membership')).toHaveLength(191)
    expect(compileTopologyDocument(document).assets).toHaveLength(128)
  })
})
