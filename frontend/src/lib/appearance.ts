import elementsImage from '@/assets/backgrounds/elements.jpg'

/**
 * How the app looks: the page background and the palette that goes with it. Several of the people using Finish Genius
 * read the screen with difficulty, so each choice keeps text on a solid panel — the picture stays behind the page and
 * never sits under words — and the two contrast settings exist for when nothing else is readable enough.
 * The choice is remembered in this browser and applied before the first screen is drawn (see main.tsx).
 */
export interface Appearance {
  key: string
  name: string
  description: string
  /** Palette: light, dark, or one of the two maximum-contrast ones. */
  theme: 'light' | 'dark' | 'contrast' | 'contrast-light'
  /** Picture behind the page, if any. */
  image?: string
  /** Swatch shown in the appearance menu. */
  swatch: string
}

export const APPEARANCES: Appearance[] = [
  {
    key: 'elements',
    name: 'Elements (dark)',
    description: 'The old site’s look: picture behind, white panels with black text.',
    theme: 'dark',
    image: elementsImage,
    swatch: `center / cover url(${elementsImage})`,
  },
  {
    key: 'charcoal',
    name: 'Charcoal',
    description: 'Plain dark background, no picture.',
    theme: 'dark',
    swatch: 'hsl(222 30% 9%)',
  },
  {
    key: 'light',
    name: 'Light',
    description: 'Light grey background with dark text.',
    theme: 'light',
    swatch: 'hsl(210 20% 98%)',
  },
  {
    key: 'contrast',
    name: 'High contrast (dark)',
    description: 'Black background, white text, stronger outlines.',
    theme: 'contrast',
    swatch: '#000',
  },
  {
    key: 'contrast-light',
    name: 'High contrast (light)',
    description: 'White background, black text, stronger outlines.',
    theme: 'contrast-light',
    swatch: '#fff',
  },
]

/** What everyone gets until they choose otherwise. */
export const DEFAULT_APPEARANCE = APPEARANCES[0]

const KEY = 'fg.appearance'
/** The light/dark toggle this setting replaces. Its old value is dropped: everybody starts on the default again. */
const OLD_THEME_KEY = 'fg.theme'

export function find(key: string | null | undefined): Appearance {
  return APPEARANCES.find((a) => a.key === key) ?? DEFAULT_APPEARANCE
}

export function storedAppearance(): Appearance {
  try {
    const saved = localStorage.getItem(KEY)
    return saved ? find(saved) : DEFAULT_APPEARANCE
  } catch {
    return DEFAULT_APPEARANCE // private mode / blocked storage: everyone still gets the default look
  }
}

/** Paints the whole app (every screen, including sign-in) in this appearance. */
export function applyAppearance(appearance: Appearance, remember = true) {
  const root = document.documentElement
  root.dataset.theme = appearance.theme
  root.dataset.appearance = appearance.key
  root.style.setProperty('--app-background-image', appearance.image ? `url(${appearance.image})` : 'none')
  if (!remember) return
  try {
    localStorage.setItem(KEY, appearance.key)
    localStorage.removeItem(OLD_THEME_KEY)
  } catch {
    /* the look still applies for this visit */
  }
}
