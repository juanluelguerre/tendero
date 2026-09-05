# shared-agent

The WebMCP registration mechanism: `AgentTools.register(...)` hands tool
descriptors to `navigator.modelContext` when the browser has one and does
nothing when it does not, which is the whole degradation story. The browser's
context is an injection token defaulting to null, so the absence is testable in
jsdom. The TOOLS themselves are identity and live in each app (`P6-1`).

`npx nx test shared-agent`
