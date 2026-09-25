# TrafficSim — a shop with no customers, given some

Simulated traffic for Tendero's laboratory (ADR 0037): people in headless
browsers through the real storefront, agents over MCP and UCP, from one declared
model and one seed. **It shows the model's assumptions, not a market** — every
number in `Model/ShopModel.cs` names its source and its grade.

## Run it

From the Aspire dashboard: `traffic-sim` is listed and never starts by itself.
Start it, stop it; the seed is the parameter `traffic-sim-seed` (default
`20260925`) and a simulated day lasts an hour.

By hand, against a running stack:

```bash
dotnet run --project tools/TrafficSim -- --TrafficSim:Seed=42 --TrafficSim:Sessions=20 \
  --TrafficSim:StartHour=20 --TrafficSim:DayLength=00:10:00
```

| Setting | Default | |
|---|---|---|
| `TrafficSim:Seed` | drawn, and logged | the run's seed; the same seed replays the same sessions |
| `TrafficSim:DayLength` | `01:00:00` | a simulated day in real time |
| `TrafficSim:StartHour` | the machine's hour | where in the day the run starts |
| `TrafficSim:MaxBrowsers` | `6` | browser sessions at once; the rest wait, none is dropped |
| `TrafficSim:Sessions` | unset | stop after this many sessions |
| `TrafficSim:Storefront`, `:Backoffice`, `:Api` | from Aspire, else `localhost:4200`, `:4201`, `:5130` | where to go |

Playwright installs its Chromium on the first run. With
`OTEL_EXPORTER_OTLP_ENDPOINT` set, the run's and each session's spans go to the
collector.

## How it works

- **The model** (`Model/`): each persona is a Customer Behavior Model Graph
  (Menascé), a Markov chain over page types. Two people who come to shop — a
  window shopper and a searcher —, a customer coming back to an order to return
  it, the shopkeeper in the backoffice (who ships and delivers, so that there is
  anything to return), and two agents, over MCP and over UCP's REST binding. The transition
  probabilities are ours; `ShopModelTests` walks each person 200 000 times and
  holds what they add up to — add to cart ~6 %, abandonment ~70.22 % (Baymard),
  conversion 1.6–2.9 % — to the published rates.
- **Arrivals** (`Scheduling/Arrivals.cs`): a non-homogeneous Poisson process over
  a 24-hour profile, compressed into `DayLength`, drawn by thinning.
- **Reproducible** (`Scheduling/Seeds.cs`): a session's seed depends only on the
  run's seed and its index, and its whole journey — steps, queries, card,
  consent — is drawn before its first request, so neither concurrency nor the
  shop's answers change it.
- **Drivers** (`Driving/`): a browser for a person, clicking what a person would;
  the MCP client and plain HTTP for an agent. A step that cannot be done is
  skipped and tagged, never retried.
- **Labelled** (ADR 0037, decision 4): `X-Tendero-Synthetic`, W3C baggage with the
  run and the persona, a User-Agent suffix. The API promotes them to its spans
  with `user_agent.synthetic.type=test`; the storefront's OTel adapter reads the
  suffix and labels everything the page sends.

## What it costs

Measured on 2026-09-25 (Windows, Chromium headless shell 1243): about **155 MB**
for the browser with no session open, and **150–210 MB** more for each session
open at once (one renderer process each).
