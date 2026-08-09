import { describe, expect, it } from 'vitest'
import { buildRdpFile } from './rdp'

describe('RDP file', () => {
  it('enables clipboard without embedding the image password', () => {
    const file = buildRdpFile('10.24.0.30', 46001, 'player')

    expect(file).toContain('full address:s:10.24.0.30:46001')
    expect(file).toContain('username:s:player')
    expect(file).toContain('redirectclipboard:i:1')
    expect(file).toContain('prompt for credentials:i:1')
    expect(file).not.toContain('password')
  })

  it('removes line breaks from server-provided values', () => {
    const file = buildRdpFile('10.24.0.30\r\nredirectdrives:i:1', 46001, 'player\nadmin')

    expect(file).not.toContain('\r\nredirectdrives:i:1:46001')
    expect(file).toContain('username:s:playeradmin')
  })
})
