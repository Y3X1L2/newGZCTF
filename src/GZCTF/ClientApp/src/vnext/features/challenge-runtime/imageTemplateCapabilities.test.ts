import { describe, expect, it } from 'vitest'
import { EnvironmentType, ImageStatus, ImageType, OSType } from '@Api'
import { runtimeTemplateAvailable } from './imageTemplateCapabilities'

describe('runtimeTemplateAvailable', () => {
  it('accepts only ready Docker templates for Docker environments', () => {
    expect(
      runtimeTemplateAvailable(
        { status: ImageStatus.Ready, imageType: ImageType.Docker, osType: OSType.Linux },
        EnvironmentType.Docker
      )
    ).toBe(true)
    expect(
      runtimeTemplateAvailable(
        { status: ImageStatus.Error, imageType: ImageType.Docker, osType: OSType.Linux },
        EnvironmentType.Docker
      )
    ).toBe(false)
  })

  it('requires a ready, certified Windows VM template', () => {
    const template = {
      status: ImageStatus.Ready,
      imageType: ImageType.Qcow2,
      osType: OSType.Windows,
    }
    expect(runtimeTemplateAvailable(template, EnvironmentType.WindowsVM)).toBe(false)
    expect(
      runtimeTemplateAvailable(
        { ...template, supportsInstanceCredentials: true },
        EnvironmentType.WindowsVM
      )
    ).toBe(true)
    expect(
      runtimeTemplateAvailable(
        { ...template, status: ImageStatus.Error, supportsInstanceCredentials: true },
        EnvironmentType.WindowsVM
      )
    ).toBe(false)
  })
})
