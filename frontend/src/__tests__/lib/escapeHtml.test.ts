import { describe, it, expect } from 'vitest'
import { escapeHtml } from '@/lib/escapeHtml'

describe('escapeHtml', () => {
  it('escapes markup so a player name stays text', () => {
    expect(escapeHtml('<img src=x onerror="alert(1)">')).toBe(
      '&#60;img src=x onerror=&#34;alert(1)&#34;&#62;',
    )
  })

  it('escapes ampersands and single quotes', () => {
    expect(escapeHtml("Tom & Jerry's")).toBe('Tom &#38; Jerry&#39;s')
  })

  it('leaves plain names alone', () => {
    expect(escapeHtml('Shotgun Messiah')).toBe('Shotgun Messiah')
  })
})
