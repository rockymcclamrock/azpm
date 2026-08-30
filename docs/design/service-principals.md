# Service-principal profiles

## Commands

```
azpm add <name> --service-principal --client-id <appId> --tenant <tid> \
    ( --client-secret <secret> | --client-secret-stdin | --certificate <pem-path> )

azpm login <name>                       # silent re-auth from the stored credential
azpm login <name> --client-secret <s>   # rotate the stored secret, then re-auth
```

`--sp` is a short alias for `--service-principal`. `--client-secret-stdin` reads the secret from
stdin (keeps it out of shell history / the arg list).

## Storage — plaintext in the profile dir (v0.2)

```
~/.azpm/profiles/<name>/
  config/        # az's AZURE_CONFIG_DIR — az already persists its own SP entry + token cache here
  meta.json      # gains "kind": "service-principal"
  sp.json        # { clientId, tenantId, auth: "secret"|"certificate", secret? , certificatePath? }
```

`sp.json` is written `chmod 600` on POSIX. On Windows it inherits the ACL of `%USERPROFILE%\.azpm`
(current user only). This mirrors what `az` itself does with tokens in `config/`.

**This is deliberately not the OS keychain yet** — tracked in
[#9](https://github.com/rockymcclamrock/azpm/issues/9). The secret is also briefly visible on the
`az login` arg list; the keychain work will fix that too.

## Behaviour

- `azpm ls` marks SP profiles (`account` column shows the client id, `(sp)` suffix).
- `azpm login <name>` on an SP profile reads `sp.json` and runs
  `az login --service-principal -u <clientId> -p <secret|certPath> --tenant <tid>` — no prompt.
- `azpm login <name> --reset` clears `config/` but keeps `sp.json`.
- `azpm rm <name>` deletes the whole profile dir, `sp.json` included.
- Cert auth passes the PEM path as `-p`; the file is referenced in place, not copied.
