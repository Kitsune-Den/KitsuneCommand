import { describe, it, expect, vi } from 'vitest'
import { createI18n } from 'vue-i18n'

// setup.ts stubs vue-i18n out; this test needs the real message compiler.
vi.unmock('vue-i18n')
import en from '@/i18n/locales/en'
import de from '@/i18n/locales/de'
import fr from '@/i18n/locales/fr'
import es from '@/i18n/locales/es'
import zhCN from '@/i18n/locales/zh-CN'
import zhTW from '@/i18n/locales/zh-TW'
import ja from '@/i18n/locales/ja'
import ko from '@/i18n/locales/ko'

const messages = { en, de, fr, es, 'zh-CN': zhCN, 'zh-TW': zhTW, ja, ko }

// Yields [dotted.key, value] for every string message in a locale.
function* strings(obj: unknown, prefix = ''): Generator<[string, string]> {
  for (const [k, v] of Object.entries(obj as Record<string, unknown>)) {
    const key = prefix ? `${prefix}.${k}` : k
    if (typeof v === 'string') yield [key, v]
    else if (v && typeof v === 'object') yield* strings(v, key)
  }
}

describe('locale messages', () => {
  // vue-i18n treats a bare `|` as the plural-form separator, so t() returns only
  // one branch. A literal pipe has to be written as {'|'}.
  it.each(Object.keys(messages))('%s has no bare pipe characters', (locale) => {
    const bare = [...strings(messages[locale as keyof typeof messages])]
      .filter(([, v]) => v.replace(/\{'\|'\}/g, '').includes('|'))
      .map(([k]) => k)
    expect(bare).toEqual([])
  })

  it.each(Object.keys(messages))('%s renders the full login footer', (locale) => {
    const i18n = createI18n({ legacy: false, locale, messages })
    const footer = i18n.global.t('login.footer')
    expect(footer.split(' | ')).toHaveLength(3)
  })
})
