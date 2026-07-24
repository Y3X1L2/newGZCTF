import { ImageStatus, ImageType, NodeStatus, OSType } from '@Api'
import { describe, expect, it } from 'vitest'
import type { GlobalInstanceInventory, ImageTemplateSummary, NodeSummary } from '../api'
import { deriveDashboardMetrics } from './dashboardSelectors'

describe('dashboard selectors', () => {
  it('uses allocated plus reserved capacity and preserves inventory coverage', () => {
    const nodes = [
      {
        status: NodeStatus.Online,
        isSchedulable: true,
        maxContainers: 10,
        allocatedContainers: 4,
        reservedContainers: 2,
        maxVms: 5,
        allocatedVms: 1,
        reservedVms: 1,
      },
    ] as NodeSummary[]
    const images = [
      { id: 1, status: ImageStatus.Error, imageType: ImageType.Docker, osType: OSType.Linux },
    ] as ImageTemplateSummary[]
    const inventory = {
      items: [{}, {}],
      loadedNodes: 1,
      totalNodes: 2,
    } as GlobalInstanceInventory

    expect(deriveDashboardMetrics(nodes, images, inventory)).toMatchObject({
      onlineNodes: 1,
      schedulableNodes: 1,
      dockerAvailable: 4,
      vmAvailable: 3,
      activeInstances: 2,
      instanceCoverage: '1/2',
      imageErrors: 1,
    })
  })
})
