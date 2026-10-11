/**
 * 7 Days to Die 3.0+ SandboxCode codec.
 *
 * 3.0 moved the world/gameplay ruleset out of serverconfig.xml into the in-game
 * Sandbox and packs it into one `SandboxCode`. The format, from the game's
 * SandboxOptionPreset.saveOptionsToCode / SandboxOptionManager.LoadOptionsFromCode:
 *
 *   code   := <version letter> <triple>*
 *   triple := <option index, base 26, two letters> <value index, one letter>
 *
 * - Letters A–Z only, case-insensitive on read.
 * - The option index is the option's position in the SandboxOptions enum; the
 *   value index is a position in THAT option's own value list, not a value.
 * - Options at their default are left out (loading resets to defaults first).
 * - The game writes triples in category order, then registration order. The
 *   table's `options` array is already in that order, so encoding walks it and
 *   gives the same code as the game's own copy button.
 *
 * The table is read live from the running game (GET /api/config/sandbox), so
 * it always matches the build the code will be applied on — the same letters
 * can mean different values on another build.
 */

export interface SandboxValue {
  label: string
  value: string
}

export interface SandboxOptionDef {
  index: number
  name: string
  label: string
  description: string
  category: string
  kind: 'int' | 'float' | 'bool' | string
  defaultIndex: number
  values: SandboxValue[]
  /** Options this one switches off at some of its values. */
  disables?: { options: string[]; whenValueIndices: number[] } | null
}

export interface SandboxPreset {
  name: string
  label: string
  description: string
  group: string
  groupLabel: string
  rating: number
  isDefault: boolean
  isUser: boolean
  isModded: boolean
  /** option name → value index; options at their default are omitted. */
  values: Record<string, number>
}

export interface SandboxTable {
  gameVersion: string
  codeVersion: string
  categories: { name: string; label: string }[]
  options: SandboxOptionDef[]
  presets: SandboxPreset[]
}

/** option name → value index, for every option in the table. */
export type SandboxSelection = Record<string, number>

export type DecodeResult =
  | {
      ok: true
      selection: SandboxSelection
      /** Triples this build can't place (unknown option or value), which the game skips too. */
      skipped: string[]
    }
  | { ok: false; error: 'empty' | 'malformed' | 'wrong-version'; codeVersion?: string }

const A = 65

function letter(index: number): string {
  if (index < 0 || index >= 26) throw new RangeError(`value index ${index} doesn't fit one letter`)
  return String.fromCharCode(A + index)
}

function twoLetters(index: number): string {
  if (index < 0 || index >= 676) throw new RangeError(`option index ${index} doesn't fit two letters`)
  return String.fromCharCode(A + Math.floor(index / 26)) + String.fromCharCode(A + (index % 26))
}

/** Every option at its default. */
export function sandboxDefaults(table: SandboxTable): SandboxSelection {
  const selection: SandboxSelection = {}
  for (const o of table.options) selection[o.name] = o.defaultIndex
  return selection
}

/**
 * Decode a code into a full selection (defaults for anything it leaves out),
 * accepting exactly what the game accepts.
 */
export function decodeSandboxCode(code: string, table: SandboxTable): DecodeResult {
  const text = code.trim().toUpperCase()
  if (text === '') return { ok: false, error: 'empty' }
  if (!/^[A-Z]+$/.test(text)) return { ok: false, error: 'malformed' }
  if (text[0] !== table.codeVersion.toUpperCase()) {
    return { ok: false, error: 'wrong-version', codeVersion: text[0] }
  }

  const byIndex = new Map(table.options.map((o) => [o.index, o]))
  const selection = sandboxDefaults(table)
  const skipped: string[] = []
  const body = text.slice(1)
  // The game reads floor(length / 3) triples and ignores a trailing partial one.
  for (let i = 0; i + 3 <= body.length; i += 3) {
    const triple = body.slice(i, i + 3)
    const optionIndex = (triple.charCodeAt(0) - A) * 26 + (triple.charCodeAt(1) - A)
    const valueIndex = triple.charCodeAt(2) - A
    const option = byIndex.get(optionIndex)
    if (!option || valueIndex >= option.values.length) {
      skipped.push(triple)
      continue
    }
    selection[option.name] = valueIndex
  }
  if (body.length % 3 !== 0) skipped.push(body.slice(body.length - (body.length % 3)))
  return { ok: true, selection, skipped }
}

/** Encode a selection the way the game does: only non-defaults, in table order. */
export function encodeSandboxCode(selection: SandboxSelection, table: SandboxTable): string {
  let code = table.codeVersion
  for (const o of table.options) {
    const valueIndex = selection[o.name] ?? o.defaultIndex
    if (valueIndex !== o.defaultIndex) code += twoLetters(o.index) + letter(valueIndex)
  }
  return code
}

/** A preset's full selection: like the game, a preset replaces the ruleset rather than layering on it. */
export function sandboxSelectionFromPreset(preset: SandboxPreset, table: SandboxTable): SandboxSelection {
  return { ...sandboxDefaults(table), ...preset.values }
}

/**
 * The preset these settings are, if any. Compared by resolved values rather
 * than code text, so it still matches whatever order a code was written in.
 */
export function sandboxPresetMatching(selection: SandboxSelection, table: SandboxTable): SandboxPreset | undefined {
  return table.presets.find((p) => {
    const expected = sandboxSelectionFromPreset(p, table)
    return table.options.every((o) => (selection[o.name] ?? o.defaultIndex) === expected[o.name])
  })
}

/**
 * Options switched off by another option's current value, mapped to the
 * option doing it — the same rule as the game's SandboxOptionManager.SetOption.
 */
export function sandboxDisabledBy(selection: SandboxSelection, table: SandboxTable): Record<string, SandboxOptionDef> {
  const disabled: Record<string, SandboxOptionDef> = {}
  for (const o of table.options) {
    if (!o.disables) continue
    if (!o.disables.whenValueIndices.includes(selection[o.name] ?? o.defaultIndex)) continue
    for (const target of o.disables.options) disabled[target] ??= o
  }
  return disabled
}
