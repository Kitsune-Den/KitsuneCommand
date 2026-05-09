import { createI18n } from 'vue-i18n'
import en from './locales/en'
import de from './locales/de'
import fr from './locales/fr'
import zhCN from './locales/zh-CN'
import zhTW from './locales/zh-TW'
import ja from './locales/ja'
import ko from './locales/ko'

export const SUPPORTED_LOCALES = [
  { code: 'en', name: 'English' },
  { code: 'de', name: 'Deutsch' },
  { code: 'fr', name: 'Français' },
  { code: 'zh-CN', name: '简体中文' },
  { code: 'zh-TW', name: '繁體中文' },
  { code: 'ja', name: '日本語' },
  { code: 'ko', name: '한국어' },
] as const

export type LocaleCode = (typeof SUPPORTED_LOCALES)[number]['code']

function getSavedLocale(): LocaleCode {
  const saved = localStorage.getItem('kc-locale')
  if (saved && SUPPORTED_LOCALES.some((l) => l.code === saved)) {
    return saved as LocaleCode
  }
  // Try to match browser language
  const browserLang = navigator.language
  if (browserLang.startsWith('zh')) {
    return browserLang.includes('TW') || browserLang.includes('Hant') ? 'zh-TW' : 'zh-CN'
  }
  if (browserLang.startsWith('ja')) return 'ja'
  if (browserLang.startsWith('ko')) return 'ko'
  if (browserLang.startsWith('de')) return 'de'
  if (browserLang.startsWith('fr')) return 'fr'
  return 'en'
}

const i18n = createI18n({
  legacy: false,
  locale: getSavedLocale(),
  fallbackLocale: 'en',
  messages: {
    en,
    de,
    fr,
    'zh-CN': zhCN,
    'zh-TW': zhTW,
    ja,
    ko,
  },
})

export function setLocale(locale: LocaleCode) {
  ;(i18n.global.locale as any).value = locale
  localStorage.setItem('kc-locale', locale)
  document.documentElement.lang = locale
}

export default i18n
