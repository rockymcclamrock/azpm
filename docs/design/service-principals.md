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

## Storage — in the profile dir

```
~/.azpm/profiles/<name>/
  config/        # az's AZURE_CONFIG_DIR — az already persists its own SP entry + token cache here
  meta.json      # gains "kind": "service-principal"
  sp.json        # { clientId, tenantId, auth, secret? | secretProtected?, certificatePath? }
```

- **Windows:** the secret is stored in `secretProtected` — a base64 **DPAPI** blob
  (`CryptProtectData`, per-user, `CRYPTPROTECT_UI_FORBIDDEN`). It decrypts only for the same
  Windows user on the same machine; copy `sp.json` elsewhere and the read fails cleanly. No key
  for azpm to manage.
- **POSIX:** the secret is plaintext in `secret`, file mode `0600`.
- A pre-DPAPI `sp.json` with a plaintext `secret` still reads on Windows; it's re-encrypted on
  the next `azpm login` / rotate.

**Still not the OS keychain** — tracked in
[#9](https://github.com/rockymcclamrock/azpm/issues/9); DPAPI is the Windows down-payment on it.
One interim gap remains ([#17](https://github.com/rockymcclamrock/azpm/issues/17)): the secret is
briefly on the `az login` argument list during authentication.

## Behaviour

- `azpm ls` marks SP profiles (`account` column shows the client id, `(sp)` suffix).
- `azpm login <name>` on an SP profile reads `sp.json` and runs
  `az login --service-principal -u <clientId> -p <secret|certPath> --tenant <tid>` — no prompt.
- `azpm login <name> --reset` clears `config/` but keeps `sp.json`.
- `azpm rm <name>` deletes the whole profile dir, `sp.json` included.
- Cert auth passes the PEM path as `-p`; the file is referenced in place, not copied.
