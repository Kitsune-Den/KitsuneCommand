import { describe, it, expect } from 'vitest'
import { bloodMoonCountdown } from '@/lib/bloodMoon'

describe('bloodMoonCountdown', () => {
  it('counts down to the day the game picked', () => {
    expect(bloodMoonCountdown(3, 7)).toEqual({ nextDay: 7, daysUntil: 4, isTonight: false })
  })

  it('is tonight on the blood moon day', () => {
    expect(bloodMoonCountdown(7, 7)).toEqual({ nextDay: 7, daysUntil: 0, isTonight: true })
  })

  it('follows a day BloodMoonRange moved, not the frequency', () => {
    // Frequency 7 with a range that landed on day 9: day 7 is not the blood moon.
    expect(bloodMoonCountdown(7, 9)).toEqual({ nextDay: 9, daysUntil: 2, isTonight: false })
  })

  it('is null when blood moons are off', () => {
    expect(bloodMoonCountdown(5, 0)).toBeNull()
  })

  it('is null before the day is known', () => {
    expect(bloodMoonCountdown(0, 7)).toBeNull()
  })

  it('is null while a blood moon runs past midnight', () => {
    // The game keeps the stat on day 7 until dawn on day 8.
    expect(bloodMoonCountdown(8, 7)).toBeNull()
  })

  it('treats missing values as unknown', () => {
    expect(bloodMoonCountdown(NaN, 7)).toBeNull()
    expect(bloodMoonCountdown(5, undefined as unknown as number)).toBeNull()
  })
})
