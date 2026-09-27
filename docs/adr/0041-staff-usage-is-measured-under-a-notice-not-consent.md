# ADR 0041 — Staff usage is measured under a notice, not by consent

Status: accepted · 2026-09-27 · decided by the author · Related: [ADR 0036](0036-observability-is-a-second-stack-behind-a-switch.md) · Spec: [`redesign.md`](../specs/redesign.md) R-0, *Decisions* 2

## Context

The redesign measures whether the backoffice's jobs get faster (R-0): which
screens the shopkeeper opens, what they do there, and how the server answered.
The browser's telemetry has two kinds of event (ADR 0036). A *technical* event is
always sent. A *behavioural* event waits for analytics consent, which the
storefront asks for in a banner. The backoffice has no banner, so a behavioural
event there would never be sent.

Asking the shopkeeper for consent is the wrong fix. They are staff, and consent
freely given is hard to show for an employee, because of the power imbalance
(EDPB guidelines 05/2020). An employer's tools usually rest on *legitimate
interest* instead: knowing whether its own software works. That basis holds
only with a notice, data minimisation and a way out. Spanish law adds rules on
monitoring at work (LOPDGDD arts. 87–91, Estatuto de los Trabajadores art. 20.3).
This is the design a real shop could deploy, not legal advice.

## Decision

1. **A third kind of event, `staff`**, declared in the tracking plan like the other
   two: the shop measuring its own tools. It is sent unless this staff member has
   switched it off.
2. **A notice, shown once**, at the top of the backoffice, es/en. It says what is
   recorded (the screen, the action, whether it worked) and what is not (who). It
   has *Understood* and *Switch it off*. A line at the foot of every screen says
   whether measuring is on and switches it either way, so the choice never means
   finding a settings page.
3. **Nothing in a staff event names a person.** Only the view, a closed list of
   actions and the outcome, and the tracking plan's test forbids id attributes.
   The figures are read in aggregate, never as anyone's performance.
4. **The first two events**: `backoffice_view_opened {view}` from the shell on
   every navigation, and `backoffice_action_taken {view, action, outcome}` from an
   RxJS operator piped onto the call that does the action. The outcome is `done`,
   `refused` (a 4xx: a rule said no) or `failed`.

## Consequences

- Time on task (R-0, R-6) comes from real use, not only from the walkthrough.
- The switch is per browser, like analytics consent, because nothing in a staff
  event could tie the choice to a person. A shop that wanted one per account
  would need an account setting, which is a server change.
- Retention is the observability stack's. A deployment sets it and says it in
  the notice; the laboratory keeps the stack's defaults.
