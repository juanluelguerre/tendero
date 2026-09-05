# shared-auth

Signing in as a redirect (Authorization Code + PKCE) against whichever OIDC
issuer the API declares at `GET /api/auth/config` — the development issuer or
Keycloak, with no build-time flag. The `AuthStore`, the token interceptor and the
route guard live here; the sign-in SCREEN stays per app.
