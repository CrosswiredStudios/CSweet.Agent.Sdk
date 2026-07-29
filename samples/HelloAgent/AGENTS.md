# HelloAgent sample instructions

This is the checked-in golden output for a minimal standalone C-Sweet agent. Keep package ID
`com.csweet.sample.hello`, version `0.1.0`, capability `hello.say.v1`, manifest, implementation,
tests, and README synchronized.

Use only SDK callbacks and typed platform clients. Never add transport, credentials, direct
database access, Docker access, or unrestricted networking. Run `dotnet test` from this directory
and `dotnet test CSweetAgentSdk.slnx` from the SDK root after every change.
