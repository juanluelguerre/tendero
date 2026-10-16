// What the apps' unit tests provide by default, without the library's components
// and HTTP service: test-providers.ts runs before Angular compiles anything, and the
// main entry's HttpClient fails to compile there.
export { NOTHING_LISTENS, SPEECH_TO_TEXT } from './lib/voice/speech-to-text';
