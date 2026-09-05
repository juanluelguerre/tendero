# shared-i18n

The Transloco wiring, the culture store and the `provideTenderoI18n` bootstrap —
not the translations, which belong to each app. It also hosts the repository-wide
tests that are about neither app: the design-token rule (no hex outside
`design/tokens.css`), the app icons and the refusal copy.

`npx nx test shared-i18n`
