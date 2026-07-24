import { EnvironmentType, ImageStatus, ImageType, OSType } from '@Api'

interface RuntimeImageTemplate {
  status?: ImageStatus
  imageType?: ImageType
  osType?: OSType
  supportsInstanceCredentials?: boolean
}

export function runtimeTemplateAvailable(
  template: RuntimeImageTemplate,
  environment?: EnvironmentType
) {
  if (template.status !== ImageStatus.Ready) return false
  if (environment === EnvironmentType.Docker) return template.imageType === ImageType.Docker
  if (environment === EnvironmentType.WindowsVM) {
    return (
      template.imageType !== ImageType.Docker &&
      template.osType === OSType.Windows &&
      template.supportsInstanceCredentials === true
    )
  }
  return false
}
