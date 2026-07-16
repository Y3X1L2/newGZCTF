import { describe, expect, it } from 'vitest'
import { ChallengeCategory, ChallengeType, EnvironmentType, NetworkMode } from '@Api'
import { challengeUpdatePayload, validateChallengeEditorDraft, type ChallengeEditorDraft } from './challengeEditorModel'

const draft: ChallengeEditorDraft = {
  title: '  SSTI  ',
  content: 'content',
  category: ChallengeCategory.Web,
  hintsText: 'first\n\n second ',
  flagTemplate: 'flag{[TEAM_HASH]}',
  isEnabled: false,
  fileName: '',
  deadline: '',
  submissionLimit: 0,
  containerImage: 'registry.internal/ssti:latest',
  memoryLimit: 128,
  cpuCount: 1,
  storageLimit: 256,
  exposePort: 80,
  networkMode: NetworkMode.Open,
  enableTrafficCapture: false,
  disableBloodBonus: false,
  originalScore: 1000,
  minScoreRate: 0.25,
  difficulty: 5,
  environment: EnvironmentType.Docker,
  imageTemplateId: 7,
}

describe('challenge editor model', () => {
  it('normalizes the complete Docker update payload', () => {
    expect(validateChallengeEditorDraft(draft, ChallengeType.DynamicContainer)).toEqual([])
    expect(challengeUpdatePayload(draft, ChallengeType.DynamicContainer)).toMatchObject({
      title: 'SSTI',
      hints: ['first', 'second'],
      environment: EnvironmentType.Docker,
      containerImage: 'registry.internal/ssti:latest',
      imageTemplateId: null,
      flagTemplate: 'flag{[TEAM_HASH]}',
    })
  })

  it('removes runtime fields from attachment challenges', () => {
    expect(challengeUpdatePayload(draft, ChallengeType.StaticAttachment)).toMatchObject({
      environment: EnvironmentType.None,
      containerImage: null,
      imageTemplateId: null,
      exposePort: null,
      networkMode: null,
    })
  })

  it('requires a ready environment identity for container challenges', () => {
    expect(
      validateChallengeEditorDraft(
        { ...draft, environment: EnvironmentType.WindowsVM, imageTemplateId: null },
        ChallengeType.StaticContainer
      )
    ).toContain('请选择已就绪的 Windows 镜像模板。')
  })
})
