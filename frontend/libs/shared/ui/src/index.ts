// There is no shared primitive yet, and that is correct: the two surfaces share
// tokens and voice, not components (ADR 0010). The library stays declared so the
// first one that earns it has somewhere to go, without the empty component the
// Nx generator left behind.
export {};
