# shared-a11y

The accessibility checks both apps' end-to-end suites run (R-1 of the redesign,
decided 2026-09-27: our own checks, no axe-core, which is MPL-2.0).

- `contrastOf(foreground, background)`: WCAG 2.2's contrast ratio from two CSS
  colours, with alpha composited, held to known pairs in its spec.
- `auditPage(page)`: runs in the browser through Playwright. It reports
  horizontal overflow and what causes it, images without `alt`, controls
  without an accessible name, fields without a label, targets under 24 px
  (2.5.8) and text under its contrast minimum (1.4.3).

Keyboard paths and focus order stay a person's job.
