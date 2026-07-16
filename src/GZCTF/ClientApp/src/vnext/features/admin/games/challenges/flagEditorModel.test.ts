import { describe, expect, it } from 'vitest'
import { AnswerType, FileType, FlagScoreMode } from '@Api'
import {
  emptyFlagEditorDraft,
  flagCreatePayload,
  validateFlagEditorDraft,
} from './flagEditorModel'

describe('flag editor model', () => {
  it('normalizes a fixed-score local attachment flag', () => {
    const draft = {
      ...emptyFlagEditorDraft(3),
      flag: '  flag{checkpoint}  ',
      description: '  checkpoint  ',
      scoreMode: FlagScoreMode.FixedScore,
      fixedScore: 200,
      attachmentType: FileType.Local,
    }
    expect(
      validateFlagEditorDraft(draft, {
        dynamicAttachment: true,
        existingAttachment: false,
        hasLocalFile: true,
      })
    ).toEqual([])
    expect(flagCreatePayload(draft, 'asset-hash')).toMatchObject({
      flag: 'flag{checkpoint}',
      description: 'checkpoint',
      fixedScore: 200,
      fileHash: 'asset-hash',
      remoteUrl: null,
    })
  })

  it('requires an attachment for a new dynamic attachment flag', () => {
    const draft = { ...emptyFlagEditorDraft(), flag: 'flag{dynamic}' }
    expect(
      validateFlagEditorDraft(draft, {
        dynamicAttachment: true,
        existingAttachment: false,
        hasLocalFile: false,
      })
    ).toContain('动态附件 Flag 必须绑定本地文件或外部链接。')
  })

  it('requires a SHA256 for file answers', () => {
    const draft = { ...emptyFlagEditorDraft(), flag: 'file-answer', answerType: AnswerType.File }
    expect(
      validateFlagEditorDraft(draft, {
        dynamicAttachment: false,
        existingAttachment: false,
        hasLocalFile: false,
      })
    ).toContain('文件答案必须配置 64 位 SHA256。')
  })
})
