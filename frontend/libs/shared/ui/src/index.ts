// Primitives whose difficulty is behaviour, shared by both apps (ADR 0010). The
// first is the theme: following the system, applying before paint, and
// surviving storage that refuses.
export * from './lib/theme';
export * from './lib/copy-text/copy-text';
export * from './lib/notice/notice';
export * from './lib/status-pill/status-pill';
export * from './lib/icon/icon';
export * from './lib/switches/culture-switch';
export * from './lib/switches/theme-switch';
export * from './lib/formatting/formatting.pipes';
// A conversation with an agent, in either app: Steward's panel and Tino's.
export * from './lib/conversation/follows-media';
export * from './lib/conversation/modal-sheet';
export * from './lib/conversation/conversation-scroll';
export * from './lib/conversation/composer';
