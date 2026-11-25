/**
 * Countdown to the next blood moon, from the day the game has actually picked
 * (its BloodMoonDay stat). Rounding the current day up to a multiple of
 * BloodMoonFrequency gets it wrong as soon as BloodMoonRange adds random days.
 */

export interface BloodMoonCountdown {
  /** The day the next blood moon falls on. */
  nextDay: number
  /** 0 on the blood moon day itself. */
  daysUntil: number
  isTonight: boolean
}

/**
 * Null when blood moons are off (bloodMoonDay 0), the current day isn't known
 * yet, or a blood moon is still running past midnight: the game keeps the stat
 * on it until dawn, which puts it one day behind.
 */
export function bloodMoonCountdown(currentDay: number, bloodMoonDay: number): BloodMoonCountdown | null {
  if (!(currentDay > 0) || !(bloodMoonDay > 0)) return null
  const daysUntil = bloodMoonDay - currentDay
  if (daysUntil < 0) return null
  return { nextDay: bloodMoonDay, daysUntil, isTonight: daysUntil === 0 }
}
