# Extra trusted root CAs for the Docker build

Any `.crt` files dropped in this folder get installed into the trust store of both image stages
in `api.Dockerfile` (via `update-ca-certificates`).

## Why this exists

On a network that does TLS inspection (Zscaler, Netskope, corporate proxies, etc.), HTTPS
traffic is re-signed with a corporate root CA. Your host machine trusts that CA, which is why
`dotnet restore` works fine directly on the host — but a fresh container ships a stock CA bundle
that doesn't, so `dotnet restore` inside `docker build` fails with:

```
error NU1301: Unable to load the service index for source https://api.nuget.org/v3/index.json.
```

That error reads like a network/firewall problem, but the connection actually succeeds and it's
TLS verification that fails. You can confirm which case you're in with:

```sh
docker run --rm alpine sh -c "apk add --no-cache wget >/dev/null 2>&1; \
  wget -S -O /dev/null https://api.nuget.org/v3/index.json"
```

`certificate verify failed` means you need the CA here. To find yours:

```sh
openssl s_client -connect api.nuget.org:443 -servername api.nuget.org </dev/null 2>/dev/null \
  | grep -E "^ *[0-9]+ s:"        # shows who signed it, e.g. "O = Zscaler Inc."
ls /usr/local/share/ca-certificates/   # Linux hosts usually already have it here
```

## On a normal network

You don't need anything here. The folder can stay empty (`.gitkeep` keeps it present so the
Dockerfile's `COPY certs/ ...` still works), and `update-ca-certificates` simply no-ops.

## A note on committing certs

These are **public root certificates, not private keys** — nothing secret leaks by having one
here. Some teams still prefer to keep internal infrastructure details out of version control; if
that's you, add `certs/*.crt` to `.gitignore` and have each developer drop in their own.
