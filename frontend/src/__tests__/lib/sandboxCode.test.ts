import { describe, it, expect } from 'vitest'
import {
  decodeSandboxCode,
  encodeSandboxCode,
  sandboxDefaults,
  sandboxDisabledBy,
  sandboxPresetMatching,
  sandboxSelectionFromPreset,
  type SandboxTable,
} from '@/lib/sandboxCode'
import fixture from '../fixtures/sandbox-table-3.3.json'
import appliedText from '../fixtures/sandbox-3.3-applied.txt?raw'

// The V 3.3.0 (b18) table as SandboxTableService reads it from the game.
const table = fixture as unknown as SandboxTable & { presetCodes: { name: string; code: string }[] }

/**
 * Boot-log dump from a live V 3.3.0 (b18) dedicated server of what a code
 * applied: "Option <Name>: <valueIndex>/<label> (default: ...)". The game's own
 * reading of the code, independent of this codec.
 */
function appliedFixture() {
  const text = appliedText
  const code = text.match(/^# code: (\w+)/m)![1]
  const applied: Record<string, number> = {}
  for (const m of text.matchAll(/^Option (\w+): (\d+)\//gm)) applied[m[1]] = Number(m[2])
  return { code, applied }
}

describe('sandboxCode against the live 3.3 table', () => {
  it('decodes a code exactly as a live 3.3 server applied it', () => {
    const { code, applied } = appliedFixture()
    const decoded = decodeSandboxCode(code, table)
    expect(decoded.ok).toBe(true)
    if (!decoded.ok) return
    expect(decoded.skipped).toEqual([])
    const changed = Object.fromEntries(
      table.options.filter((o) => decoded.selection[o.name] !== o.defaultIndex).map((o) => [o.name, decoded.selection[o.name]]),
    )
    expect(changed).toEqual(applied)
  })

  it('re-encodes that code byte for byte (the game writes in category order)', () => {
    const { code } = appliedFixture()
    const decoded = decodeSandboxCode(code, table)
    if (!decoded.ok) throw new Error('decode failed')
    expect(encodeSandboxCode(decoded.selection, table)).toBe(code)
  })

  it('decodes every built-in preset cleanly', () => {
    // Written by the game's designers against the real option table, so a
    // stray triple here would mean the table is wrong.
    expect(table.presetCodes.length).toBeGreaterThan(10)
    for (const preset of table.presetCodes) {
      if (preset.code === '') continue // Nomad: the defaults
      const decoded = decodeSandboxCode(preset.code, table)
      expect(decoded, preset.name).toMatchObject({ ok: true, skipped: [] })
    }
  })

  it('encodes all defaults as just the version letter', () => {
    expect(encodeSandboxCode(sandboxDefaults(table), table)).toBe('A')
  })

  it('switches options off the way the game does', () => {
    const selection = { ...sandboxDefaults(table), StaminaUsage: 0 }
    expect(sandboxDisabledBy(selection, table).StaminaRegen?.name).toBe('StaminaUsage')
    expect(sandboxDisabledBy(sandboxDefaults(table), table).StaminaRegen).toBeUndefined()
  })
})

describe('decodeSandboxCode', () => {
  it('is case-insensitive and tolerates surrounding whitespace', () => {
    const { code } = appliedFixture()
    const upper = decodeSandboxCode(code, table)
    const lower = decodeSandboxCode(`  ${code.toLowerCase()}\n`, table)
    expect(lower).toEqual(upper)
  })

  it('rejects empty, non-letter and other-version codes', () => {
    expect(decodeSandboxCode('  ', table)).toEqual({ ok: false, error: 'empty' })
    expect(decodeSandboxCode('AAA-B', table)).toEqual({ ok: false, error: 'malformed' })
    expect(decodeSandboxCode('BAAA', table)).toEqual({ ok: false, error: 'wrong-version', codeVersion: 'B' })
  })

  it('skips triples this build cannot place, like the game', () => {
    // ZZA: option 675 doesn't exist. AAZ: RangedDamage has 13 values, not 26.
    // AAM: RangedDamage = 12, valid. AB: a trailing partial triple.
    const decoded = decodeSandboxCode('AZZAAAZAAMAB', table)
    expect(decoded).toMatchObject({ ok: true, skipped: ['ZZA', 'AAZ', 'AB'] })
    if (decoded.ok) expect(decoded.selection.RangedDamage).toBe(12)
  })
})

describe('presets', () => {
  const withPresets: SandboxTable = {
    ...table,
    presets: [
      { name: 'Hard', label: 'Hard', description: '', group: 'Difficulty', groupLabel: 'Difficulty', rating: 5, isDefault: false, isUser: false, isModded: false, values: { RangedDamage: 3 } },
      { name: 'Nomad', label: 'Nomad', description: '', group: 'Difficulty', groupLabel: 'Difficulty', rating: 3, isDefault: true, isUser: false, isModded: false, values: {} },
    ],
  }

  it('replaces the whole ruleset rather than layering on top', () => {
    const selection = sandboxSelectionFromPreset(withPresets.presets[0], withPresets)
    expect(selection.RangedDamage).toBe(3)
    expect(selection.MeleeDamage).toBe(table.options.find((o) => o.name === 'MeleeDamage')!.defaultIndex)
  })

  it('finds the preset a selection matches, by value', () => {
    expect(sandboxPresetMatching(sandboxDefaults(withPresets), withPresets)?.name).toBe('Nomad')
    expect(sandboxPresetMatching({ ...sandboxDefaults(withPresets), RangedDamage: 3 }, withPresets)?.name).toBe('Hard')
    expect(sandboxPresetMatching({ ...sandboxDefaults(withPresets), RangedDamage: 4 }, withPresets)).toBeUndefined()
  })
})
